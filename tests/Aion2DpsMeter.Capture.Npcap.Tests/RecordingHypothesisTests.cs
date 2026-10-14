using Aion2DpsMeter.Core.Analysis;
using Xunit.Abstractions;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Wertet eine vorhandene, eigene Aufnahme aus (nur lesen) und gibt den Textbericht mit der
/// Hypothesen-Prüfung aus. Läuft nur, wenn ausdrücklich eingeschaltet:
/// Umgebungsvariable AION2DPS_HYPOTHESIS_PCAP = Pfad zur .pcap-Datei,
/// optional AION2DPS_HYPOTHESIS_LOCAL = eigene IP-Adresse(n), durch Komma getrennt.
/// Die Datei wird nicht verändert; der Bericht enthält nur Zahlen, keine Paketinhalte.
/// </summary>
[Trait("Category", "RequiresNpcap")]
[Trait("Category", "LocalRecording")]
public class RecordingHypothesisTests(ITestOutputHelper output)
{
    private const string PathVariable = "AION2DPS_HYPOTHESIS_PCAP";
    private const string LocalVariable = "AION2DPS_HYPOTHESIS_LOCAL";
    private const string CsvVariable = "AION2DPS_HYPOTHESIS_CSV"; // optional: Zielpfad der CSV (wird nie überschrieben)

    [Fact]
    public async Task ReportsHypothesesForOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var local = Environment.GetEnvironmentVariable(LocalVariable)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var analysis = await RecordingReport.AnalyzeAsync(new PcapFileSource(), path, local)
            .WaitAsync(TimeSpan.FromMinutes(2));

        output.WriteLine(RecordingReport.Format(analysis, Path.GetFileName(path)));
        Assert.True(analysis.TotalPackets > 0);
    }

    /// <summary>
    /// Liest 02 97 nach der Community-Beschreibung (Gruppen-Schlüssel u32, Name u8+UTF-8, Größe u8, 4+2+8+3 Bytes,
    /// Anzahl varint, je Mitglied Maske u8, Platz u8, Kennung u64, Name u8+UTF-8 …) und vergleicht die Namen mit den
    /// Spielern, die Schaden machen. Namen nur maskiert.
    /// </summary>
    [Fact]
    public async Task ChecksPartyRosterAgainstDamageDealers()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        static string Mask(string name) => name.Length == 0 ? "–" : $"{name[0]}…({name.Length})";
        DateTimeOffset? start = null;
        var rosters = new List<(DateTimeOffset Time, int Size, List<string> Names, string Problem)>();
        var analyzer = new RecordingAnalyzer(null, new HashSet<ushort> { 0x0297, 0x0036 }, (_, frame) =>
        {
            start ??= frame.Time;
            if (frame.Type != 0x0297) return;
            var b = frame.Body;
            if (b.Length < 60) output.WriteLine($"KURZE 02 97 bei {(frame.Time - start.Value).TotalSeconds:0.0} s: {b.Length} B {Convert.ToHexString(b)}");
            var names = new List<string>();
            var o = 4;
            string problem = "";
            int size = -1;
            try
            {
                int partyName = b[o++];
                o += partyName;
                size = b[o];
                o += 1 + 4 + 2 + 8 + 3;
                ReadVarint(b, ref o, out var count);
                for (var i = 0; i < (int)count && o + 11 <= b.Length; i++)
                {
                    int nameLength = b[o + 10];
                    if (nameLength == 0 || o + 11 + nameLength > b.Length) break;
                    names.Add(System.Text.Encoding.UTF8.GetString(b, o + 11, nameLength));
                    // Nächstes Mitglied: nach Platz-Nummer i+1 suchen (Kopf: Maske, Platz, Kennung, Namenslänge).
                    var next = -1;
                    for (var at = o + 11 + nameLength; at + 11 < b.Length && at < o + 11 + nameLength + 120; at++)
                    {
                        if (b[at + 1] == i + 1 && b[at + 10] is > 0 and < 40 && at + 11 + b[at + 10] <= b.Length &&
                            System.Text.Encoding.UTF8.GetString(b, at + 11, b[at + 10]).All(c => char.IsLetterOrDigit(c)))
                        {
                            next = at;
                            break;
                        }
                    }

                    if (next < 0) break;
                    o = next;
                }
            }
            catch (IndexOutOfRangeException)
            {
                problem = "Rahmen zu kurz";
            }

            rosters.Add((frame.Time, size, names, problem));
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();

        // Schadensverursacher mit Namen (gleicher Weg wie live).
        var meter = new Core.Combat.LiveCombatMeter(TimeSpan.FromHours(1));
        var decoder = new LiveCombatDecoder(meter.Add, p => meter.ReportPlayer(p.EntityId, p.Name, p.IsSelf), s => meter.ReportSummon(s.RawId, s.Owner));
        await new PcapFileSource().ReadRawAsync(path, decoder.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        var actors = meter.Snapshot(DateTimeOffset.MaxValue).Actors;

        // Robust: kommt „Längenbyte + Name“ eines Spielers irgendwo in einer Gruppenliste vor?
        var bodies = new List<byte[]>();
        var collect = new RecordingAnalyzer(null, new HashSet<ushort> { 0x0297 }, (_, frame) => bodies.Add(frame.Body));
        await new PcapFileSource().ReadRawAsync(path, collect.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        collect.GetResult();
        bool InRoster(string name)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name);
            var needle = new byte[bytes.Length + 1];
            needle[0] = (byte)bytes.Length;
            bytes.CopyTo(needle, 1);
            return bodies.Any(b => b.AsSpan().IndexOf(needle) >= 0);
        }

        var rosterNames = actors.Where(a => a.Name is not null && InRoster(a.Name)).Select(a => a.Name!).ToHashSet();

        // Eingebettete Gruppenlisten: „02 97“ mitten in anderen Rahmen, danach ein passender Kopf mit bekannten Namen?
        var named = actors.Where(a => a.Name is not null).Select(a => a.Name!).ToList();
        var embeddedHits = new List<(DateTimeOffset Time, ushort Type, int Size, int Names)>();
        var scan = new RecordingAnalyzer(null, Enumerable.Range(0, 0x10000).Select(t => (ushort)t).Where(t => t != 0x0297).ToHashSet(), (_, frame) =>
        {
            var b = frame.Body;
            for (var i = 0; i + 8 < b.Length; i++)
            {
                if (b[i] != 0x02 || b[i + 1] != 0x97) continue;
                if (PartyRosterHypothesis.Parse(b.AsSpan(i + 2)) is not { } roster) continue;
                var found = named.Count(roster.Contains);
                if (found > 0) embeddedHits.Add((frame.Time, frame.Type, roster.Size, found));
            }
        });
        await new PcapFileSource().ReadRawAsync(path, scan.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        scan.GetResult();
        output.WriteLine($"Eingebettete Gruppenlisten mit bekannten Namen: {embeddedHits.Count}");

        // Kampfkraft: eigener Datensatz (33 36) und Gruppenmitglieder aus der letzten Gruppenliste (Namen maskiert).
        var selfBodies = new List<byte[]>();
        var selfScan = new RecordingAnalyzer(null, new HashSet<ushort> { 0x3336 }, (_, frame) => selfBodies.Add(frame.Body));
        await new PcapFileSource().ReadRawAsync(path, selfScan.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        selfScan.GetResult();
        foreach (var body in selfBodies) output.WriteLine($"KP eigener Datensatz ({body.Length} B): {CombatPowerHypothesis.FromSelfRecord(body)}");
        if (bodies.LastOrDefault(b => b.Length > 50) is { } lastRoster)
        {
            foreach (var a in actors.Where(a => a.Name is not null))
            {
                var power = CombatPowerHypothesis.FromRoster(lastRoster, a.Name!);
                output.WriteLine($"KP {Mask(a.Name!)} {a.ClassName}{(a.IsOwn ? " (ich)" : "")}: Kampfkraft {power?.CombatPower.ToString() ?? "–"}, GS {power?.GearScore.ToString() ?? "–"}");
            }
        }
        // Je Gruppenliste und Spieler: Bytes vor und nach dem Namen (Namen selbst nicht ausgeben).
        if (Environment.GetEnvironmentVariable("AION2DPS_DUMP_ROSTER") == "1")
        {
            foreach (var body in bodies)
            {
                output.WriteLine($"LISTE {body.Length} B");
                foreach (var a in actors.Where(a => a.Name is not null))
                {
                    var nb = System.Text.Encoding.UTF8.GetBytes(a.Name!);
                    var needle = new byte[nb.Length + 1];
                    needle[0] = (byte)nb.Length;
                    nb.CopyTo(needle, 1);
                    var at = body.AsSpan().IndexOf(needle);
                    if (at < 0) continue;
                    var before = Convert.ToHexString(body, Math.Max(0, at - 12), Math.Min(12, at));
                    var afterStart = at + needle.Length;
                    var after = Convert.ToHexString(body, afterStart, Math.Min(48, body.Length - afterStart));
                    var power = CombatPowerHypothesis.FromRoster(body, a.Name!);
                    output.WriteLine($"  {Mask(a.Name!)} @{at}: vor {before} | nach {after} | KP {power?.CombatPower.ToString() ?? "–"}");
                }
            }
        }

        foreach (var h in embeddedHits.Take(10))
        {
            output.WriteLine($"  bei {(h.Time - start!.Value).TotalSeconds:0.0} s in Typ {h.Type >> 8:X2} {h.Type & 0xFF:X2}: Größe {h.Size}, Namen {h.Names}");
        }
        output.WriteLine($"Spieler mit Namen in einer Gruppenliste (Suche Längenbyte + Name): {rosterNames.Count}; Spieler mit Namen gesamt: {actors.Count(a => a.Name is not null)}");
        foreach (var r in rosters)
        {
            output.WriteLine($"02 97 bei {(r.Time - start!.Value).TotalSeconds:0.0} s: Größe {r.Size}, Mitglieder gelesen {r.Names.Count}: {string.Join(", ", r.Names.Select(Mask))} {r.Problem}");
        }

        foreach (var a in actors.Where(a => a.Name is not null).Take(12))
        {
            output.WriteLine($"  Spieler {a.Actor} [{Mask(a.Name!)}] {a.ClassName}{(a.IsOwn ? " (ich)" : "")}: Anteil {a.Share:P1}, in Gruppenliste: {(rosterNames.Contains(a.Name!) ? "ja" : "nein")}");
        }
    }

    /// <summary>
    /// Suche nach Spieler-Toden: 00 8D mit Art ≠ 2 (Spieler-HP) bis 0, 42 36 und 1B 92 mit einer Spieler-Kennung vorn.
    /// Nur Zahlen, Namen maskiert.
    /// </summary>
    [Fact]
    public async Task SearchesPlayerDeaths()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        static string Mask(string name) => name.Length == 0 ? "–" : $"{name[0]}…({name.Length})";
        var meter = new Core.Combat.LiveCombatMeter(TimeSpan.FromHours(1));
        var decoder = new LiveCombatDecoder(meter.Add, p => meter.ReportPlayer(p.EntityId, p.Name, p.IsSelf), s => meter.ReportSummon(s.RawId, s.Owner));
        await new PcapFileSource().ReadRawAsync(path, decoder.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        var players = meter.Snapshot(DateTimeOffset.MaxValue).Actors.Where(a => a.Name is not null).ToDictionary(a => a.Actor, a => a.Name!);

        DateTimeOffset? start = null;
        var hpByPlayer = new Dictionary<uint, List<(double T, ulong Kind, long Hp)>>();
        var kinds = new Dictionary<ulong, int>();
        var lead = new Dictionary<ushort, List<(double T, uint Actor, int Length)>>();
        var analyzer = new RecordingAnalyzer(null, new HashSet<ushort> { 0x008D, 0x4236, 0x1B92 }, (_, frame) =>
        {
            start ??= frame.Time;
            var t = (frame.Time - start.Value).TotalSeconds;
            var b = frame.Body;
            var o = 0;
            if (!ReadVarint(b, ref o, out var target)) return;
            if (frame.Type == 0x008D)
            {
                if (!ReadVarint(b, ref o, out var kind) || !ReadVarint(b, ref o, out ulong _) || !ReadVarint(b, ref o, out ulong _) || o + 8 > b.Length) return;
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                if (!players.ContainsKey((uint)target)) return;
                (hpByPlayer.TryGetValue((uint)target, out var l) ? l : hpByPlayer[(uint)target] = []).Add((t, kind, BitConverter.ToInt64(b, o)));
                return;
            }

            if (players.ContainsKey((uint)target))
            {
                (lead.TryGetValue(frame.Type, out var l) ? l : lead[frame.Type] = []).Add((t, (uint)target, b.Length));
            }
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();

        output.WriteLine($"00 8D Arten: {string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}");
        foreach (var (actor, list) in hpByPlayer)
        {
            var zeros = list.Where(x => x.Hp == 0).Select(x => x.T.ToString("0.0")).ToList();
            output.WriteLine($"Spieler {Mask(players[actor])}: {list.Count} HP-Meldungen (Arten {string.Join("/", list.Select(x => x.Kind).Distinct())}), min {list.Min(x => x.Hp)}, max {list.Max(x => x.Hp)}, HP=0 bei: {string.Join(" ", zeros.Take(10))}");
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Hp != 0 || (i > 0 && list[i - 1].Hp == 0)) continue;
                var before = i > 0 ? list[i - 1] : default;
                var back = list.Skip(i + 1).FirstOrDefault(x => x.Hp > 0);
                output.WriteLine($"   0 bei {list[i].T:0.0} s (Art {list[i].Kind}), davor {before.Hp} bei {before.T:0.0} s, wieder {back.Hp} nach {(back.T > 0 ? (back.T - list[i].T).ToString("0.0") : "–")} s");
            }
        }

        // Tode aus 1B 92: Wechsel lebt → tot.
        var deaths = new Dictionary<uint, List<double>>();
        var alive = new Dictionary<uint, bool>();
        var parsed = 0;
        DateTimeOffset? first = null;
        var hpScan = new RecordingAnalyzer(null, new HashSet<ushort> { PartyHpHypothesis.Type }, (_, frame) =>
        {
            first ??= frame.Time;
            if (PartyHpHypothesis.Parse(frame.Body) is not { } m) return;
            parsed++;
            if (alive.TryGetValue(m.Target, out var was) && was && !m.Alive)
            {
                (deaths.TryGetValue(m.Target, out var l) ? l : deaths[m.Target] = []).Add((frame.Time - first.Value).TotalSeconds);
            }

            if (alive.TryGetValue(m.Target, out var wasAlive) && !wasAlive && m.Alive && deaths.TryGetValue(m.Target, out var dl))
            {
                output.WriteLine($"WIEDER #{m.Target % 1000} nach {(frame.Time - first.Value).TotalSeconds - dl[^1]:0.0} s");
            }

            alive[m.Target] = m.Alive;
        });
        await new PcapFileSource().ReadRawAsync(path, hpScan.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        hpScan.GetResult();
        output.WriteLine($"1B 92 gelesen: {parsed}, Mitglieder: {alive.Count}");
        foreach (var id in alive.Keys)
        {
            var name = players.TryGetValue(id, out var n) ? Mask(n) : $"#{id}";
            var list = deaths.GetValueOrDefault(id) ?? [];
            output.WriteLine($"TOD {name}: {list.Count} ({string.Join(" ", list.Select(x => x.ToString("0.0")))})");
        }

        // Gleicher Weg wie live: Decoder → Zähler (eine Stunde Leerlauf = ein Kampf).
        var live = new Core.Combat.LiveCombatMeter(TimeSpan.FromHours(1));
        var liveDecoder = new LiveCombatDecoder(live.Add, p => live.ReportPlayer(p.EntityId, p.Name, p.IsSelf), s => live.ReportSummon(s.RawId, s.Owner), onDeath: live.ReportDeath);
        await new PcapFileSource().ReadRawAsync(path, liveDecoder.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        foreach (var a in live.Snapshot(DateTimeOffset.MaxValue).Actors.Where(a => a.Name is not null))
        {
            output.WriteLine($"LIVE-TODE {Mask(a.Name!)}{(a.IsOwn ? " (ich)" : "")}: {a.Deaths}");
        }

        foreach (var (type, list) in lead)
        {
            output.WriteLine($"{type >> 8:X2} {type & 0xFF:X2} mit Spieler vorn: {list.Count}");
            foreach (var x in list.Take(15)) output.WriteLine($"   {x.T:0.0} s {Mask(players[x.Actor])} {x.Length} B");
        }
    }

    /// <summary>Inventar aller Nachrichtentypen (Server → Client): Anzahl, Größen. Nur gezählt, nicht gedeutet.</summary>
    [Fact]
    public async Task ListsAllMessageTypesInOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var stats = new Dictionary<ushort, (int Count, int Min, int Max, long Sum)>();
        var all = Enumerable.Range(0, 0x10000).Select(t => (ushort)t).ToHashSet();
        var analyzer = new RecordingAnalyzer(null, all, (_, frame) =>
        {
            var s = stats.GetValueOrDefault(frame.Type, (0, int.MaxValue, 0, 0));
            stats[frame.Type] = (s.Count + 1, Math.Min(s.Min, frame.Body.Length), Math.Max(s.Max, frame.Body.Length), s.Sum + frame.Body.Length);
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();
        foreach (var (type, s) in stats.OrderByDescending(kv => kv.Value.Count))
        {
            output.WriteLine($"TYP {type >> 8:X2} {type & 0xFF:X2}: {s.Count}, Inhalt {s.Min}–{s.Max} B, Ø {s.Sum / s.Count}");
        }

        // Stichproben für die Einschätzung: Heil-Effekte (05 38), Selbst-Ziel-Datensätze (04 38) und je 3 Beispiele weiterer Typen.
        int heals = 0, damageEffects = 0;
        long healSum = 0;
        var samples = new Dictionary<ushort, List<string>>();
        ushort[] sampleTypes = [0x4236, 0x048D, 0x1B92, 0x0061, 0x2136, 0x2336, 0x1D37, 0x0638];
        var second = new RecordingAnalyzer(null, new HashSet<ushort>(sampleTypes) { DotRecordHypothesis.Type }, (_, frame) =>
        {
            if (frame.Type == DotRecordHypothesis.Type)
            {
                if (DotRecordHypothesis.Parse(frame.Body) is { } d)
                {
                    if (d.IsHealEffect) { heals++; healSum += d.Amount; }
                    else if (d.IsDamageEffect) damageEffects++;
                }

                return;
            }

            var list = samples.TryGetValue(frame.Type, out var l) ? l : samples[frame.Type] = [];
            if (list.Count < 3) list.Add(Convert.ToHexString(frame.Body.AsSpan(0, Math.Min(24, frame.Body.Length))));
        });
        await new PcapFileSource().ReadRawAsync(path, second.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        second.GetResult();
        output.WriteLine($"05 38: Heil-Effekte {heals} (Summe {healSum}), Schadens-Effekte {damageEffects}");
        foreach (var t in sampleTypes)
        {
            output.WriteLine($"PROBE {t >> 8:X2} {t & 0xFF:X2}: " + (samples.TryGetValue(t, out var l) ? string.Join(" | ", l) : "kommt nicht vor"));
        }
    }

    /// <summary>
    /// Sucht einen möglichen Berserk-Zeitpunkt: In allen Rahmen rund um den Kampfbeginn des größten Gegners nach u64-
    /// Zeitstempeln (ms) bzw. u32-Sekunden suchen, die 1–30 min NACH dem Kampfbeginn liegen. Gibt Typ und Wert aus.
    /// </summary>
    [Fact]
    public async Task SearchesBerserkTimestampAroundBossPull()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path) || Environment.GetEnvironmentVariable("AION2DPS_PULL") is not { } pullText)
        {
            output.WriteLine("Nicht ausgeführt: AION2DPS_HYPOTHESIS_PCAP und AION2DPS_PULL (Ortszeit HH:mm:ss) setzen.");
            return;
        }

        var all = Enumerable.Range(0, 0x10000).Select(t => (ushort)t).ToHashSet();
        var hits = new List<string>();
        DateTimeOffset? pull = null;
        var analyzer = new RecordingAnalyzer(null, all, (_, frame) =>
        {
            pull ??= new DateTimeOffset(frame.Time.ToLocalTime().Date + TimeSpan.Parse(pullText), frame.Time.ToLocalTime().Offset);
            if (frame.Time < pull.Value.AddSeconds(-30) || frame.Time > pull.Value.AddSeconds(30)) return;
            var b = frame.Body;
            for (var i = 0; i + 8 <= b.Length; i++)
            {
                var ms = BitConverter.ToInt64(b, i);
                if (ms > 1_600_000_000_000 && ms < 2_600_000_000_000)
                {
                    var at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                    var after = at - pull.Value;
                    if (after > TimeSpan.FromMinutes(1) && after < TimeSpan.FromMinutes(30) && hits.Count < 40)
                    {
                        hits.Add($"{frame.Time.ToLocalTime():HH:mm:ss} Typ {frame.Type >> 8:X2} {frame.Type & 0xFF:X2} @{i}: {at.ToLocalTime():HH:mm:ss} (+{after:mm\\:ss} nach Kampfbeginn)");
                    }
                }
            }
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();
        output.WriteLine($"Zeitstempel 1–30 min nach Kampfbeginn in Rahmen ±30 s um {pullText}: {hits.Count}");
        foreach (var h in hits) output.WriteLine("  " + h);
    }

    /// <summary>21 8D (Community: Kampfzustand, bei Bossen mit Zeitlimit + Deadline u64 ms) – alle Rahmen mit Deadline ausgeben.</summary>
    [Fact]
    public async Task ReadsBattleStateDeadlines()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var npcCodes = new Dictionary<uint, int>();
        var deaths = new Dictionary<uint, DateTimeOffset>();
        int states = 0, engaged = 0, withDeadline = 0;
        var lines = new List<string>();
        var analyzer = new RecordingAnalyzer(null, new HashSet<ushort> { 0x218D, 0x4136, 0x008D }, (_, frame) =>
        {
            if (frame.Type == 0x4136)
            {
                if (EnemyHpHypothesis.ParseSpawn(frame.Body) is { NpcCode: { } code } s) npcCodes[s.Target] = code;
                return;
            }

            if (frame.Type == 0x008D)
            {
                if (EnemyHpHypothesis.ParseRemaining(frame.Body) is { Hp: 0 } dead) deaths.TryAdd(dead.Target, frame.Time);
                return;
            }

            if (BattleStateHypothesis.Parse(frame.Body) is not { } state) return;
            states++;
            if (state.Engaged) engaged++;
            if (state.Deadline is not { } deadline) return;
            withDeadline++;
            lines.Add($"{frame.Time.ToLocalTime():HH:mm:ss} Gegner #{state.Target} (NPC {(npcCodes.TryGetValue(state.Target, out var c) ? c : 0)}): Deadline {deadline.ToLocalTime():HH:mm:ss} = +{deadline - frame.Time:mm\\:ss}");
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();
        output.WriteLine($"21 8D: {states} gelesen, Kampfbeginn {engaged}, mit Deadline {withDeadline}");
        foreach (var l in lines) output.WriteLine("  " + l);
        foreach (var l in lines)
        {
            var id = uint.Parse(l.Split('#')[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
            output.WriteLine($"  → Gegner #{id}: HP 0 um {(deaths.TryGetValue(id, out var d) ? d.ToLocalTime().ToString("HH:mm:ss") : "–")}");
        }
    }

    /// <summary>Feldboss-Liste (01 91) lesen und ausgeben: Karte, Plätze, lebt/tot, Zeit (Ortszeit) relativ zur Aufnahme.</summary>
    [Fact]
    public async Task ReadsFieldBossListsInOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var analyzer = new RecordingAnalyzer(null, new HashSet<ushort> { FieldBossListHypothesis.Type }, (_, frame) =>
        {
            var list = FieldBossListHypothesis.Parse(frame.Body);
            output.WriteLine($"01 91 um {frame.Time.ToLocalTime():HH:mm:ss}, {frame.Body.Length} B: " + (list is null
                ? "Kopf passt nicht: " + Convert.ToHexString(frame.Body.AsSpan(0, Math.Min(24, frame.Body.Length)))
                : $"Karte {list.Map}, Plätze {list.Count}, gelesen {list.Slots.Count}"));
            if (list is not null && Core.Events.WorldBossTimes.Apply(Core.Events.WorldBossTimes.Default, list) is { } gartua)
            {
                output.WriteLine($"  → Gartua laut Liste: {gartua[0].RespawnAt?.ToLocalTime():dd.MM. HH:mm:ss}");
            }

            foreach (var s in list?.Slots ?? [])
            {
                var when = s.Time is { } t ? $"{t.ToLocalTime():dd.MM. HH:mm:ss} ({(t - frame.Time).TotalMinutes:+0.0;-0.0} min)" : "keine Zeit";
                output.WriteLine($"    Platz {s.Slot}: {(s.Alive ? "lebt seit" : "tot, zurück")} {when}");
            }
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();
    }

    /// <summary>Sucht die Community-Hypothese „Gruppenliste = Typ 02 97“ (auch eingebettet in anderen Rahmen).</summary>
    [Fact]
    public async Task SearchesPartyRosterTypeInOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var all = Enumerable.Range(0, 0x10000).Select(t => (ushort)t).ToHashSet();
        int frames = 0, typed = 0, embedded = 0, fieldBoss = 0;
        var analyzer = new RecordingAnalyzer(null, all, (_, frame) =>
        {
            frames++;
            if (frame.Type == 0x0191) fieldBoss++; // Community-Hypothese: Feldboss-Liste einer Karte
            if (frame.Type == 0x0297) typed++;
            else if (frame.Body.AsSpan().IndexOf(new byte[] { 0x02, 0x97 }) >= 0) embedded++;
        });
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();
        output.WriteLine($"Rahmen: {frames}, Typ 02 97: {typed}, andere Rahmen mit Bytefolge 02 97: {embedded}, Typ 01 91: {fieldBoss}");
    }

    /// <summary>
    /// Community-Hypothese „00 8D = Rest-HP“: Ziel varint, Art varint, varint, varint, HP u64. Prüfung: Sinkt die HP
    /// zwischen zwei Meldungen genau um den gemessenen Schaden (04 38 + 05 38) auf dieses Ziel?
    /// </summary>
    [Fact]
    public async Task ChecksRemainingHpHypothesisOnOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var types = new HashSet<ushort> { 0x008D, DamageRecordHypothesis.Type, DotRecordHypothesis.Type };
        var frames = new List<ObservedFrame>();
        var analyzer = new RecordingAnalyzer(null, types, (_, frame) => frames.Add(frame));
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();

        static bool Varint(byte[] b, ref int o, out ulong value)
        {
            value = 0;
            for (var shift = 0; o < b.Length && shift < 64; shift += 7)
            {
                var x = b[o++];
                value |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return true;
            }

            return false;
        }

        var withExtra = Environment.GetEnvironmentVariable("AION2DPS_HP_WITH_EXTRA") == "1";
        output.WriteLine(withExtra ? "Schaden inkl. Zusatzwerte (ExtraSum)" : "Schaden ohne Zusatzwerte");
        var lastHp = new Dictionary<uint, long>();
        var pending = new Dictionary<uint, long>();
        var totalDrop = new Dictionary<uint, long>();
        var totalDamage = new Dictionary<uint, long>();
        var maxHp = new Dictionary<uint, long>();
        int readings = 0, unparsed = 0, compared = 0, exact = 0, noDamage = 0;
        var kinds = new Dictionary<ulong, int>();
        var examples = new List<string>();
        foreach (var f in frames)
        {
            if (f.Type == DamageRecordHypothesis.Type)
            {
                foreach (var r in DamageRecordHypothesis.Parse(f.Body).Records.Where(r => !r.SelfTarget))
                {
                    pending[r.Target] = pending.GetValueOrDefault(r.Target) + r.Candidate + (withExtra ? r.ExtraSum : 0);
                }

                continue;
            }

            if (f.Type == DotRecordHypothesis.Type)
            {
                if (DotRecordHypothesis.Parse(f.Body) is { IsDamageEffect: true } d && d.Actor != d.Target)
                {
                    pending[d.Target] = pending.GetValueOrDefault(d.Target) + d.Amount;
                }

                continue;
            }

            var o = 0;
            if (!Varint(f.Body, ref o, out var entity) || !Varint(f.Body, ref o, out var kind) ||
                !Varint(f.Body, ref o, out _) || !Varint(f.Body, ref o, out _) || o + 8 > f.Body.Length)
            {
                unparsed++;
                continue;
            }

            readings++;
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            var hp = BitConverter.ToInt64(f.Body, o);
            var id = (uint)entity;
            var damage = pending.GetValueOrDefault(id);
            pending[id] = 0;
            maxHp[id] = Math.Max(maxHp.GetValueOrDefault(id), hp);
            if (lastHp.TryGetValue(id, out var before))
            {
                if (before > hp) totalDrop[id] = totalDrop.GetValueOrDefault(id) + (before - hp);
                totalDamage[id] = totalDamage.GetValueOrDefault(id) + damage;
                if (damage == 0) noDamage++;
                else
                {
                    compared++;
                    if (before - hp == damage) exact++;
                    else if (examples.Count < 8) examples.Add($"Ziel {id}: HP {before} → {hp} (−{before - hp}), gemessener Schaden {damage}");
                }
            }

            lastHp[id] = hp;
        }

        output.WriteLine($"00 8D: {readings} gelesen, {unparsed} nicht passend; Arten: {string.Join(", ", kinds.Select(k => $"{k.Key}={k.Value}"))}");
        output.WriteLine($"Abgleich zwischen zwei Meldungen mit Schaden dazwischen: {compared}, davon HP-Abnahme = Schaden genau: {exact}; ohne Schaden dazwischen: {noDamage}");
        foreach (var e in examples) output.WriteLine("  Abweichung: " + e);
        // Zeitverlauf des größten Gegners (für Phasen/Berserk-Fragen): erste/letzte Meldung, Zeitpunkt von HP 0.
        var hpTimes = new Dictionary<uint, List<(DateTimeOffset T, long Hp)>>();
        foreach (var f in frames.Where(f => f.Type == 0x008D))
        {
            if (EnemyHpHypothesis.ParseRemaining(f.Body) is { } r)
            {
                (hpTimes.TryGetValue(r.Target, out var l) ? l : hpTimes[r.Target] = []).Add((f.Time, r.Hp));
            }
        }

        if (maxHp.Count > 0)
        {
            var biggest = maxHp.Where(m => m.Value <= 10_000_000_000).MaxBy(m => m.Value).Key;
            if (hpTimes.TryGetValue(biggest, out var series))
            {
                var zero = series.FirstOrDefault(s => s.Hp == 0);
                output.WriteLine($"Größter Gegner #{biggest}: erste HP-Meldung {series[0].T.ToLocalTime():HH:mm:ss}, letzte {series[^1].T.ToLocalTime():HH:mm:ss}, HP 0 um {(zero.T == default ? "–" : zero.T.ToLocalTime().ToString("HH:mm:ss"))}");
            }
        }

        foreach (var (id, drop) in totalDrop.OrderByDescending(d => d.Value).Take(8))
        {
            var dmg = totalDamage.GetValueOrDefault(id);
            output.WriteLine($"  Gegner {id}: Max-HP {maxHp[id]}, HP-Abnahme gesamt {drop}, gemessener Schaden {dmg}, Verhältnis {(dmg == 0 ? 0 : (double)drop / dmg):0.00}");
        }
    }

    /// <summary>
    /// Community-Hypothese „41 36 enthält die HP als 01 + aktuell + max“: Passt das Max zur höchsten danach in 00 8D
    /// gemeldeten HP dieses Gegners (bis er wieder erscheint)?
    /// </summary>
    private static bool ReadVarint(byte[] b, ref int o, out ulong value)
    {
        value = 0;
        for (var shift = 0; o < b.Length && shift < 64; shift += 7)
        {
            var x = b[o++];
            value |= (ulong)(x & 0x7F) << shift;
            if ((x & 0x80) == 0) return true;
        }

        return false;
    }

    [Fact]
    public async Task ChecksSpawnMaxHpHypothesisOnOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var frames = new List<ObservedFrame>();
        var analyzer = new RecordingAnalyzer(null, new HashSet<ushort> { 0x4136, 0x008D }, (_, frame) => frames.Add(frame));
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();

        // Je Erscheinen: Kandidaten (aktuell, max) aus „01 varint varint“; danach die höchste gemeldete HP.
        var open = new Dictionary<uint, (List<(ulong Cur, ulong Max)> Candidates, long MaxSeen)>();
        int spawns = 0, withReading = 0, matched = 0, noCandidate = 0;
        var examples = new List<string>();

        void Close(uint id)
        {
            if (!open.Remove(id, out var s) || s.MaxSeen <= 0) return;
            withReading++;
            if (s.Candidates.Count == 0) noCandidate++;
            if (s.Candidates.Any(c => (long)c.Max == s.MaxSeen)) matched++;
            else if (examples.Count < 6) examples.Add($"Gegner {id}: höchste HP {s.MaxSeen}, Kandidaten {string.Join(" ", s.Candidates.Take(5).Select(c => $"{c.Cur}/{c.Max}"))}");
        }

        foreach (var f in frames)
        {
            var b = f.Body;
            var o = 0;
            if (f.Type == 0x4136)
            {
                if (!ReadVarint(b, ref o, out var rawValue) || rawValue is 0 or > uint.MaxValue) continue;
                var raw = (uint)rawValue;
                spawns++;
                Close(raw);
                var candidates = new List<(ulong, ulong)>();
                for (var i = o; i < b.Length - 2; i++)
                {
                    if (b[i] != 0x01) continue;
                    var q = i + 1;
                    if (ReadVarint(b, ref q, out var cur) && ReadVarint(b, ref q, out var max) &&
                        max is >= 100 and <= 10_000_000_000 && cur <= max && cur > 0)
                    {
                        candidates.Add((cur, max));
                    }
                }

                open[raw] = (candidates, 0);
                continue;
            }

            // 00 8D: Ziel, Art (2 = Gegner), zwei Varints, HP u64.
            if (!ReadVarint(b, ref o, out var idValue) || !ReadVarint(b, ref o, out var kind) || kind != 2 ||
                !ReadVarint(b, ref o, out _) || !ReadVarint(b, ref o, out _) || o + 8 > b.Length) continue;
            var hp = BitConverter.ToInt64(b, o);
            var id = (uint)idValue;
            if (open.TryGetValue(id, out var state) && hp is > 0 and <= 10_000_000_000) open[id] = (state.Candidates, Math.Max(state.MaxSeen, hp));
        }

        foreach (var id in open.Keys.ToList()) Close(id);

        // Gegner-Nummern (NPC-Vorlage) der Gegner mit HP-Meldungen; Namen aus lokaler Liste, falls per Umgebungsvariable angegeben.
        var npcFile = Environment.GetEnvironmentVariable("AION2DPS_NPCS");
        var npcNames = npcFile is not null && File.Exists(npcFile)
            ? System.Text.Json.JsonDocument.Parse(File.ReadAllText(npcFile)).RootElement
            : (System.Text.Json.JsonElement?)null;
        var codes = new Dictionary<int, int>();
        foreach (var f in frames.Where(f => f.Type == 0x4136))
        {
            if (EnemyHpHypothesis.ParseNpcCode(f.Body) is { } code) codes[code] = codes.GetValueOrDefault(code) + 1;
        }

        foreach (var f in frames.Where(f => f.Type == 0x4136))
        {
            if (EnemyHpHypothesis.ParseSpawn(f.Body) is { MaxHp: >= 1_000_000 } big && EnemyHpHypothesis.ParseNpcCode(f.Body) is { } bigCode)
            {
                var bigName = npcNames is { } nn && nn.TryGetProperty(bigCode.ToString(System.Globalization.CultureInfo.InvariantCulture), out var be) ? $"{be.GetProperty("name").GetString()} (Boss: {(be.TryGetProperty("isBoss", out var ib) && ib.GetBoolean())})" : "?";
                output.WriteLine($"  GROSS Gegner #{big.Target}: Max-HP {big.MaxHp}, NPC {bigCode} – {bigName}");
            }
        }

        output.WriteLine($"Gegner-Nummern gefunden in {codes.Values.Sum()} von {frames.Count(f => f.Type == 0x4136)} Erscheinen-Rahmen, verschiedene: {codes.Count}");
        foreach (var (code, count) in codes.OrderByDescending(c => c.Value).Take(15))
        {
            var name = npcNames is { } n && n.TryGetProperty(code.ToString(System.Globalization.CultureInfo.InvariantCulture), out var e) ? e.GetProperty("name").GetString() : "?";
            output.WriteLine($"  NPC {code}: {count}× – {name}");
        }
        output.WriteLine($"41 36: {spawns} Erscheinen; mit HP-Meldung danach: {withReading}; Max-Kandidat = höchste gemeldete HP: {matched}; ohne Kandidat: {noCandidate}");
        foreach (var e in examples) output.WriteLine("  Abweichung: " + e);
    }

    [Fact]
    public async Task ChecksPlayerInfoHypothesisOnOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var types = new HashSet<ushort>(PlayerInfoHypothesis.Types) { DamageRecordHypothesis.Type };
        var frames = new List<(TcpStreamKey Key, ObservedFrame Frame)>();
        var analyzer = new RecordingAnalyzer(null, types, (key, frame) => frames.Add((key, frame)));
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();

        // Schaden je Akteur (04 38, ohne Selbsttreffer) zum Abgleich.
        var damage = frames.Where(f => f.Frame.Type == DamageRecordHypothesis.Type)
            .SelectMany(f => DamageRecordHypothesis.Parse(f.Frame.Body).Records)
            .Where(r => !r.SelfTarget)
            .GroupBy(r => r.Actor)
            .ToDictionary(g => g.Key, g => g.Sum(r => (long)r.Candidate));

        // Namen nur maskiert ausgeben (erster Buchstabe + Länge) – fremde Namen gehören nicht in Protokolle.
        static string Mask(string name) => $"{name[0]}…({name.Length})";

        foreach (var group in frames.Where(f => f.Frame.Type != DamageRecordHypothesis.Type).GroupBy(f => f.Frame.Type))
        {
            var parsed = group.Select(f => PlayerInfoHypothesis.Parse(f.Frame.Type, f.Frame.Body)).ToList();
            var ok = parsed.OfType<PlayerInfoCandidate>().ToList();
            output.WriteLine($"Typ {group.Key >> 8:X2} {group.Key & 0xFF:X2}: {group.Count()} Rahmen, Felder passen: {ok.Count}, verschiedene Kennungen: {ok.Select(c => c.EntityId).Distinct().Count()}, mit Server: {ok.Count(c => c.ServerId is not null)}");
            var nameConflicts = ok.GroupBy(c => c.EntityId).Count(g => g.Select(c => c.Name).Distinct().Count() > 1);
            output.WriteLine($"  Kennungen mit mehreren verschiedenen Namen: {nameConflicts}");
            foreach (var c in ok.DistinctBy(c => c.EntityId).Take(40))
            {
                output.WriteLine($"  {c.EntityId}: {Mask(c.Name)}, Server {c.ServerId?.ToString() ?? "–"}, Schaden 04 38: {(damage.TryGetValue(c.EntityId, out var d) ? d : 0)}");
            }
        }

        // Zeitpunkte (Sekunden seit dem ersten beobachteten Rahmen): eigener Charakter und Häufung von Spielernachrichten.
        var start = frames.Count > 0 ? frames.Min(f => f.Frame.Time) : DateTimeOffset.MinValue;
        foreach (var self in frames.Where(f => f.Frame.Type == PlayerInfoHypothesis.SelfType))
        {
            output.WriteLine($"33 36 bei {(self.Frame.Time - start).TotalSeconds:0.0} s, im Bündel: {self.Frame.InBundle}");
        }

        output.WriteLine("45/44 36 je 5 s: " + string.Join(", ", frames
            .Where(f => f.Frame.Type is PlayerInfoHypothesis.PlayerType or PlayerInfoHypothesis.OldPlayerType)
            .GroupBy(f => (int)((f.Frame.Time - start).TotalSeconds / 5) * 5)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}s:{g.Count()}")));
        var firstDamage = frames.Where(f => f.Frame.Type == DamageRecordHypothesis.Type).Select(f => f.Frame.Time).DefaultIfEmpty(start).Min();
        output.WriteLine($"erster 04-38-Rahmen bei {(firstDamage - start).TotalSeconds:0.0} s");

        var named = frames.Where(f => f.Frame.Type != DamageRecordHypothesis.Type)
            .Select(f => PlayerInfoHypothesis.Parse(f.Frame.Type, f.Frame.Body))
            .OfType<PlayerInfoCandidate>()
            .Select(c => c.EntityId)
            .ToHashSet();
        output.WriteLine($"Akteure mit Schaden: {damage.Count}, davon mit Namen: {damage.Keys.Count(named.Contains)}; Schaden mit Namen: {damage.Where(kv => named.Contains(kv.Key)).Sum(kv => kv.Value)} von {damage.Values.Sum()}");
    }

    [Fact]
    public async Task ChecksSummonOwnerHypothesisOnOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var types = new HashSet<ushort> { SummonOwnerHypothesis.Type, DamageRecordHypothesis.Type, DotRecordHypothesis.Type };
        var frames = new List<ObservedFrame>();
        var analyzer = new RecordingAnalyzer(null, types, (_, frame) => frames.Add(frame));
        await new PcapFileSource().ReadRawAsync(path, analyzer.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        analyzer.GetResult();

        var hits = frames.Where(f => f.Type == DamageRecordHypothesis.Type)
            .SelectMany(f => DamageRecordHypothesis.Parse(f.Body).Records)
            .Where(r => !r.SelfTarget)
            .Select(r => (r.Actor, r.Skill, Amount: (long)r.Candidate))
            .Concat(frames.Where(f => f.Type == DotRecordHypothesis.Type)
                .Select(f => DotRecordHypothesis.Parse(f.Body))
                .Where(d => d is { Effect: 0x0A } && d.Actor != d.Target)
                .Select(d => (d!.Actor, d.Skill, Amount: (long)d.Amount)))
            .ToList();
        var actors = hits.GroupBy(h => h.Actor).ToDictionary(
            g => g.Key,
            g => (Damage: g.Sum(h => h.Amount), Prefix: g.Select(h => Core.Combat.SkillClassMap.Prefix(h.Skill)).OfType<int>().GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => (int?)x.Key).FirstOrDefault(),
                  SkillMin: g.Min(h => h.Skill), SkillMax: g.Max(h => h.Skill)));

        var spawns = frames.Where(f => f.Type == SummonOwnerHypothesis.Type).ToList();
        var owners = spawns.Select(f => SummonOwnerHypothesis.Parse(f.Body)).OfType<SummonOwnerCandidate>().ToList();
        output.WriteLine($"41 36: {spawns.Count} Rahmen, mit Besitzer: {owners.Count} (Elternverweis {owners.Count(o => o.Source == SummonOwnerSource.ParentLink)}, Anker {owners.Count(o => o.Source == SummonOwnerSource.Anchor)}), Arten: {string.Join(", ", owners.GroupBy(o => o.Kind).Select(g => $"{g.Key:X2}={g.Count()}"))}");

        foreach (var o in owners.DistinctBy(o => (o.RawId, o.Owner)))
        {
            var asRaw = actors.TryGetValue(o.RawId, out var r) ? $"roh als Akteur: {r.Damage} (Präfix {r.Prefix?.ToString() ?? "–"}, Fertigkeiten {r.SkillMin}–{r.SkillMax})" : "roh: kein Schaden";
            var asMapped = o.MappedId != o.RawId && actors.TryGetValue(o.MappedId, out var m) ? $", umgerechnet als Akteur: {m.Damage}" : "";
            var owner = actors.TryGetValue(o.Owner, out var w) ? $"Besitzer {o.Owner}: Schaden {w.Damage}, Präfix {w.Prefix?.ToString() ?? "–"}" : $"Besitzer {o.Owner}: kein Schaden";
            output.WriteLine($"  {o.RawId}/{o.MappedId} Art {o.Kind:X2} {o.Source}: {asRaw}{asMapped}; {owner}");
        }

        output.WriteLine("Akteure ohne Klassen-Präfix (mögliche Pets/Effekte): " + string.Join("; ",
            actors.Where(a => a.Value.Prefix is null).OrderByDescending(a => a.Value.Damage).Take(15)
                .Select(a => $"{a.Key}: {a.Value.Damage}, Fertigkeiten {a.Value.SkillMin}–{a.Value.SkillMax}")));
        output.WriteLine("Fertigkeiten mit 7-stelliger Nummer (1.000.000–9.999.999): " + string.Join("; ",
            hits.Where(h => h.Skill is >= 1_000_000 and <= 9_999_999).GroupBy(h => h.Actor)
                .Select(g => $"{g.Key}: {g.Count()}× {g.Min(h => h.Skill)}–{g.Max(h => h.Skill)}")));
    }

    [Fact]
    public async Task ReplaysOwnRecordingThroughLiveMeter()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        // Gleicher Weg wie live, nur aus der Datei statt vom Adapter; Leerlaufzeit groß, damit es ein Kampf bleibt.
        var meter = new Core.Combat.LiveCombatMeter(TimeSpan.FromHours(1));
        var enemies = new Core.Combat.EnemyHpTracker();
        var hpUpdates = 0;
        var bossSamples = new List<string>();
        var decoder = new LiveCombatDecoder(meter.Add, p => meter.ReportPlayer(p.EntityId, p.Name, p.IsSelf),
            s => meter.ReportSummon(s.RawId, s.Owner),
            (hp, time) =>
            {
                enemies.Report(hp.Target, hp.Hp, hp.MaxHp, time);
                if (hp.MaxHp is null) meter.MarkEnemy(hp.Target);
                // Alle 200 HP-Meldungen: was die Boss-Leiste jetzt zeigen würde.
                if (++hpUpdates % 200 == 0 && meter.MainTarget(time, TimeSpan.FromSeconds(10)) is { } target && enemies.Status(target, time) is { } boss)
                {
                    bossSamples.Add($"Gegner #{boss.Target}: {Core.Combat.OverlayFormat.BossText(boss)}" + (boss.Deadline is { } dl ? " · " + Core.Combat.OverlayFormat.BerserkTextUntil(dl, time) : ""));
                }
            },
            meter.ReportParty,
            onBattleState: enemies.ReportBattleState);
        await new PcapFileSource().ReadRawAsync(path, decoder.Add, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        output.WriteLine($"Als Gegner aus der Liste genommen: {meter.HiddenEnemyActors}");
        // Wen treffen die namenlosen Verursacher? (Spieler mit Namen bzw. eigener Charakter = freundlich)
        var final = meter.Snapshot(DateTimeOffset.MaxValue);
        var friendlyIds = final.Actors.Where(a => a.Name is not null || a.IsOwn).Select(a => a.Actor).ToHashSet();
        foreach (var a in final.Actors.Where(a => a.Name is null && !a.IsLikelyPet).Take(8))
        {
            var targets = meter.Detail(a.Actor)?.Targets ?? [];
            output.WriteLine($"  Namenlos {a.Actor}: Ziele {targets.Count}, davon Spieler mit Namen: {targets.Count(t => friendlyIds.Contains(t.Target))}, Schaden auf sie: {targets.Where(t => friendlyIds.Contains(t.Target)).Sum(t => t.Damage)} von {a.Damage}");
        }
        output.WriteLine($"Gegner-HP-Meldungen: {hpUpdates}; Gruppe: Größe {meter.PartySize?.ToString() ?? "–"}, erkannte Mitglieder {meter.PartyMembers().Count}");
        foreach (var b in bossSamples.Take(10)) output.WriteLine("  Boss-Leiste: " + b);

        var snapshot = meter.Snapshot(DateTimeOffset.MaxValue);
        output.WriteLine($"Treffer gelesen: {decoder.HitCount}, Spielerangaben: {decoder.PlayerInfoCount}, eigener Charakter erkannt: {meter.DetectedOwnActor?.ToString() ?? "nein"}, verworfene Selbsttreffer: {snapshot.IgnoredSelfHits}, Kampfdauer {snapshot.Duration.TotalSeconds:0.0} s");
        if (meter.OwnActor is { } activeId && meter.Detail(activeId) is { } own)
        {
            output.WriteLine($"Eigener Charakter: Dauer {own.Duration.TotalSeconds:0.0} s, aktiv {own.ActiveTime.TotalSeconds:0.0} s, DPS {own.Dps:0}, aktive DPS {own.ActiveDps:0}, Zauber {own.Hits}, Treffer inkl. Mehrfach {own.HitsWithExtra}, Krit {own.CriticalRate:P1}");
            // Fähigkeiten des eigenen Charakters (für den Namensabgleich mit einem Vergleichsmeter).
            foreach (var s in own.Skills)
            {
                output.WriteLine($"    Fähigkeit {s.Skill} {s.Kind}: {s.Damage} ({Core.Combat.OverlayFormat.Compact(s.Damage)}), Treffer {s.Hits}");
            }
        }

        if (meter.OwnActor is { } ownId && meter.Detail(ownId)?.Flags is { } f)
        {
            output.WriteLine($"Community-Felder eigener Charakter: Parade {f.ParryRate:P1}, Perfekt {f.PerfectRate:P1}, Doppelt {f.DoubleRate:P1}, Rücken {f.BackRate:P1}, Front {f.FrontRate:P1}, Multi {f.MultiRate:P1} (Modifikator bei {f.WithModifier} von {f.DirectHits} Treffern)");
        }

        foreach (var a in snapshot.Actors)
        {
            // Namen nur maskiert (erster Buchstabe + Länge).
            var name = a.Name is { } n ? $"{n[0]}…({n.Length})" : "–";
            output.WriteLine($"  {a.Actor} [{name}] {a.ClassName ?? "–"}{(a.IsOwn ? " (ich)" : "")}{(a.IsLikelyPet ? " (Pet?)" : "")}: Schaden {a.Damage} (davon über Zeit {a.OverTimeDamage}, Pet {a.PetDamage}), Treffer {a.Hits}, krit. {a.CriticalHits}, DPS {a.Dps:0}, Anteil {a.Share:P1}");
        }
    }

    [Fact]
    public async Task ReportsDamageCandidatesForOwnRecording()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine($"Nicht ausgeführt: {PathVariable} ist nicht gesetzt.");
            return;
        }

        var local = Environment.GetEnvironmentVariable(LocalVariable)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var analysis = await DamageCandidateReport.AnalyzeAsync(new PcapFileSource(), path, local)
            .WaitAsync(TimeSpan.FromMinutes(2));

        output.WriteLine(DamageCandidateReport.Format(analysis, Path.GetFileName(path), maxRows: 40));
        if (Environment.GetEnvironmentVariable(CsvVariable) is { Length: > 0 } csvPath && !File.Exists(csvPath))
        {
            await File.WriteAllTextAsync(csvPath, DamageCandidateReport.ToCsv(analysis), new System.Text.UTF8Encoding(true));
            output.WriteLine($"CSV: {csvPath}");
        }
        Assert.True(analysis.Recording.TotalPackets > 0);
    }
}
