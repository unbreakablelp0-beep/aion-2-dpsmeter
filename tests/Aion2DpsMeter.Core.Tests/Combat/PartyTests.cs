using System.Text;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Gruppenliste (02 97) und Anzeige „Ich + Gruppe“ (selbst gebaute Bytefolgen, erfundene Namen).</summary>
public class PartyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Kopf wie beschrieben, danach Mitglieder je mit Längenbyte + Name und etwas Füllung.</summary>
    private static byte[] Roster(int size, params string[] members)
    {
        var bytes = new List<byte> { 1, 2, 3, 4, 0, (byte)size };
        bytes.AddRange(new byte[17]);
        bytes.Add((byte)members.Length);
        byte slot = 0;
        foreach (var m in members)
        {
            var name = Encoding.UTF8.GetBytes(m);
            bytes.AddRange(new byte[] { 0x01, slot++, 9, 9, 9, 9, 9, 9, 0x2A, 0x05 });
            bytes.Add((byte)name.Length);
            bytes.AddRange(name);
            bytes.AddRange(new byte[23]);
        }

        return bytes.ToArray();
    }

    [Fact]
    public void ParsesRosterAndFindsMemberNames()
    {
        var roster = PartyRosterHypothesis.Parse(Roster(3, "Bogenfrau", "Heiler", "Zauberin"))!;
        Assert.Equal(3, roster.Size);
        Assert.True(roster.Contains("Heiler"));
        Assert.True(roster.Contains("Zauberin"));
        Assert.False(roster.Contains("Heil")); // Teil eines Namens zählt nicht (Längenbyte passt nicht)
        Assert.False(roster.Contains("Fremder"));
        Assert.Null(PartyRosterHypothesis.Parse(new byte[] { 1, 2, 3, 4, 0, 0 })); // Größe 0
        Assert.Null(PartyRosterHypothesis.Parse(new byte[] { 1, 2 }));
    }

    [Fact]
    public void OwnModeShowsOwnPlusPartyButNotStrangers()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Bogenfrau", isSelf: true);
        meter.ReportPlayer(2, "Heiler", isSelf: false);
        meter.ReportPlayer(3, "Fremder", isSelf: false);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1000, false, false));
        meter.Add(new CombatHit(T0, 2, 900, 17_010_000, 2000, false, false));
        meter.Add(new CombatHit(T0, 3, 900, 15_010_000, 9000, false, false));

        Assert.Empty(meter.PartyMembers());
        Assert.Single(OverlayFormat.Build(meter.Snapshot(T0), 8, ownOnly: true, party: meter.PartyMembers()).Rows);

        meter.ReportParty(PartyRosterHypothesis.Parse(Roster(2, "Bogenfrau", "Heiler"))!);
        Assert.Equal(2, meter.PartySize);
        Assert.Equal(new HashSet<uint> { 1, 2 }, meter.PartyMembers().ToHashSet());

        var rows = OverlayFormat.Build(meter.Snapshot(T0), 8, ownOnly: true, party: meter.PartyMembers()).Rows;
        Assert.Equal(new uint[] { 2, 1 }, rows.Select(r => r.Actor)); // nach Schaden, ohne den Fremden
    }

    [Fact]
    public void EnemiesAreRemovedFromPlayerListAndShares()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.ReportPlayer(2, "Heiler", isSelf: false);
        meter.Add(new CombatHit(T0, 1, 500, 14_020_340, 3000, false, false));     // ich → Boss 500
        meter.Add(new CombatHit(T0, 2, 500, 17_010_000, 1000, false, false));     // Heiler → Boss
        meter.Add(new CombatHit(T0, 500, 1, 17_200_000, 9000, false, false));     // Boss → ich (Fertigkeit wie „Kleriker“)
        meter.Add(new CombatHit(T0, 600, 2, 15_010_000, 800, false, false));      // namenloser Angreifer → Heiler
        meter.Add(new CombatHit(T0, 700, 900, 15_010_000, 2000, false, false));   // namenlos, fremdes Ziel: unklar, bleibt
        meter.Add(new CombatHit(T0, 800, 901, 18_010_000, 1000, false, false));   // namenlos, bekommt 00 8D

        meter.MarkEnemy(800);
        var actors = meter.Snapshot(T0).Actors;
        Assert.Equal(new uint[] { 1, 700, 2 }, actors.Select(a => a.Actor));
        Assert.Equal(0.5, actors[0].Share); // 3000 von 6000 – Gegnerschaden zählt nicht mit
        Assert.Equal(3, meter.HiddenEnemyActors);
    }

    [Fact]
    public void PlayerViewDefaultsToAutomaticAndMigratesOldSetting()
    {
        Assert.Equal(OverlaySettings.ViewAuto, new OverlaySettings().Normalized().PlayerView);
        Assert.False(new OverlaySettings(ShowAllPlayers: true).Normalized().ShowAllPlayers);
        Assert.Equal(OverlaySettings.ViewAuto, new OverlaySettings(PlayerView: "Quatsch", PlayerViewChosen: true).Normalized().PlayerView);
        Assert.Equal(OverlaySettings.ViewOwn, new OverlaySettings(PlayerView: OverlaySettings.ViewOwn, PlayerViewChosen: true).Normalized().PlayerView);
    }

    /// <summary>
    /// Der Fehler aus dem Dungeon: Bis v22 speicherte der Overlay-Knopf „Nur ich“ dauerhaft (so stand es in der Datei des
    /// Besitzers). Nicht in den Einstellungen gewählte Ansichten fallen jetzt beim Laden auf die Automatik zurück.
    /// </summary>
    [Fact]
    public void ViewSetByOldOverlayButtonFallsBackToAutomatic()
    {
        Assert.Equal(OverlaySettings.ViewAuto, new OverlaySettings(PlayerView: OverlaySettings.ViewOwn).Normalized().PlayerView);
        Assert.Equal(OverlaySettings.ViewAuto, new OverlaySettings(PlayerView: OverlaySettings.ViewAll).Normalized().PlayerView);
        Assert.Equal(OverlaySettings.ViewAuto, new OverlaySettings(ShowAllPlayers: true).Normalized().PlayerView);
    }

    /// <summary>Mitglied wie beschrieben: Kopf (Maske, Platz, Kennung mit Server 0x052A), Name, Klasse, Stufe, GS, Flags, Server, u16, Byte, KP.</summary>
    private static byte[] RosterWithPower(params (string Name, int Gear, long Power)[] members)
    {
        var bytes = new List<byte> { 1, 2, 3, 4, 0, (byte)members.Length };
        bytes.AddRange(new byte[17]);
        bytes.Add((byte)members.Length);
        byte slot = 0;
        foreach (var (name, gear, power) in members)
        {
            var n = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(new byte[] { 0x00, slot++, 9, 9, 9, 9, 9, 9, 0x2A, 0x05, (byte)n.Length });
            bytes.AddRange(n);
            bytes.AddRange(BitConverter.GetBytes(14)); // Klasse
            bytes.AddRange(BitConverter.GetBytes(45)); // Stufe
            bytes.AddRange(BitConverter.GetBytes(gear));
            bytes.AddRange(new byte[] { 0x01, 0x00 }); // Flags
            bytes.AddRange(new byte[] { 0x2A, 0x05, 0x00, 0x00, 0x07 }); // Server, u16, Byte
            bytes.AddRange(BitConverter.GetBytes(power));
            bytes.AddRange(new byte[6]);
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// Wie der erste Eintrag in der Aufnahme 20261014-164636: Server-Nummer in der Kennung (00 09) passt nicht zum Feld
    /// danach (05 09). Die Kampfkraft steht trotzdem 5 Bytes nach dem Ausrüstungswert (0x01AD7C = 109.948).
    /// </summary>
    [Fact]
    public void ReadsCombatPowerWhenIdServerDiffers()
    {
        var name = Encoding.UTF8.GetBytes("Korvin");
        var roster = new byte[30]
            .Concat(Convert.FromHexString("03051C01C569030000000009"))
            .Append((byte)name.Length).Concat(name)
            .Concat(Convert.FromHexString("200000002D000000710900000509F210047CAD010000000000004000000000000000"))
            .ToArray();
        Assert.Equal(new PlayerPower(109_948, 2_417), CombatPowerHypothesis.FromRoster(roster, "Korvin"));
    }

    [Fact]
    public void ReadsCombatPowerFromRosterAndSelfRecord()
    {
        var roster = RosterWithPower(("Bogenfrau", 2182, 96_770), ("Heiler", 2322, 114_901));
        Assert.Equal(new PlayerPower(96_770, 2182), CombatPowerHypothesis.FromRoster(roster, "Bogenfrau"));
        Assert.Equal(new PlayerPower(114_901, 2322), CombatPowerHypothesis.FromRoster(roster, "Heiler"));
        Assert.Null(CombatPowerHypothesis.FromRoster(roster, "Fremder"));

        // Eigener Datensatz: am Ende aktuelle und höchste Kampfkraft (u64), dahinter noch etwas.
        var self = new byte[100].Concat(BitConverter.GetBytes(92_999L)).Concat(BitConverter.GetBytes(93_500L)).Concat(new byte[48]).ToArray();
        Assert.Equal(92_999, CombatPowerHypothesis.FromSelfRecord(self));
        Assert.Equal(0, CombatPowerHypothesis.FromSelfRecord(new byte[200]));
    }

    [Fact]
    public void MeterGivesCombatPowerForOwnAndPartyOnly()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Bogenfrau", isSelf: true);
        meter.ReportPlayer(2, "Heiler", isSelf: false);
        meter.ReportPlayer(3, "Fremder", isSelf: false);
        meter.ReportOwnCombatPower(96_770);
        meter.ReportParty(PartyRosterHypothesis.Parse(RosterWithPower(("Bogenfrau", 2182, 90_000), ("Heiler", 2322, 114_901)))!);

        Assert.Equal(96_770, meter.CombatPowerOf(1)); // eigener Datensatz geht vor
        Assert.Equal(114_901, meter.CombatPowerOf(2));
        Assert.Null(meter.CombatPowerOf(3));

        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1000, false, false));
        var row = OverlayFormat.Build(meter.Snapshot(T0), 8, combatPower: meter.CombatPowerOf).Rows.Single();
        Assert.Equal(96_770, row.CombatPower);
        Assert.Equal("96,8K", OverlayFormat.Compact(row.CombatPower!.Value));
    }

    [Fact]
    public void RememberedPartyWorksUntilRealRosterArrives()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Lyra", isSelf: true);
        meter.ReportPlayer(2, "Korvin", isSelf: false);
        meter.ReportPlayer(3, "Mira", isSelf: false);
        meter.RememberParty(3, ["Lyra", "Korvin", "Mira"]);

        Assert.True(meter.PartyIsRemembered);
        Assert.Equal(new HashSet<uint> { 1, 2, 3 }, meter.PartyMembers().ToHashSet());
        Assert.Null(meter.CombatPowerOf(2)); // gemerkte Gruppe hat keine Kampfkraft

        meter.ReportParty(PartyRosterHypothesis.Parse(Roster(2, "Lyra", "Korvin"))!);
        Assert.False(meter.PartyIsRemembered);
        Assert.Equal(new HashSet<uint> { 1, 2 }, meter.PartyMembers().ToHashSet());

        meter.RememberParty(3, ["Lyra", "Mira"]); // echte Liste bleibt
        Assert.Equal(new HashSet<uint> { 1, 2 }, meter.PartyMembers().ToHashSet());
        var snapshot = meter.PartySnapshot()!.Value;
        Assert.Equal(2, snapshot.Size);
        Assert.Equal(["Korvin", "Lyra"], snapshot.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PartyStoreKeepsGroupForTwoHoursAndRespectsForeignFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aion2dps-party-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "gruppe.json");
            var store = new PartyStore(path);
            Assert.Null(store.Load(T0));
            Assert.True(store.Save(3, ["Lyra", "Korvin", "Mira"], T0));
            Assert.Equal(3, store.Load(T0.AddMinutes(110))!.Value.Names.Count);
            Assert.Null(store.Load(T0.AddHours(3))); // zu alt (seit v42: 2 h)

            File.WriteAllText(path, "{\"Kind\":\"fremd\"}");
            Assert.False(store.Save(1, ["X"], T0));
            Assert.Equal("{\"Kind\":\"fremd\"}", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OwnClassKnownBeforeFirstHit()
    {
        // Neustart: gemerkter Charakter mit Klasse 14 (Bogenschütze), neue Kennung nach dem Einloggen, noch kein Treffer.
        var meter = new LiveCombatMeter();
        meter.Remember(new RememberedOwnCharacter(10_275, "Lyra", T0, ClassPrefix: 14));
        meter.ReportPlayer(3_521, "Lyra", isSelf: true);
        Assert.Equal("Bogenschütze", meter.ClassNameOf(3_521));
        Assert.Null(meter.ClassNameOf(7));

        // Treffer überschreiben die gemerkte Klasse.
        meter.Add(new CombatHit(T0, 3_521, 900, 15_010_000, 100, false, false));
        Assert.Equal("Magier", meter.ClassNameOf(3_521));
    }

    [Fact]
    public void OwnRowStaysWhenPartyExceedsRowLimit()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Ich", isSelf: true);
        string[] names = ["Ich", "A1", "A2", "A3", "A4"];
        for (uint i = 2; i <= 5; i++) meter.ReportPlayer(i, names[i - 1], isSelf: false);
        for (uint i = 1; i <= 5; i++) meter.Add(new CombatHit(T0, i, 900, 14_020_340, i == 1 ? 10 : 1000 * i, false, false));
        meter.ReportParty(PartyRosterHypothesis.Parse(Roster(5, names))!);

        var rows = OverlayFormat.Build(meter.Snapshot(T0), 3, ownOnly: true, party: meter.PartyMembers()).Rows;
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.IsOwn);
    }
}
