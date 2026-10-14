using System.Globalization;
using System.Text;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Ein 04-38-Rahmen mit Zeitpunkt, Richtung und Zerlegung.</summary>
public sealed record DamageCandidateFrame(int Number, DateTimeOffset Time, TcpStreamKey Stream, bool InBundle, byte[] Body, DamageFrameResult Result);

/// <summary>Ein 05-38-Rahmen mit Zeitpunkt, Richtung und Zerlegung (null = Felder passten nicht).</summary>
public sealed record DotCandidateFrame(int Number, DateTimeOffset Time, TcpStreamKey Stream, bool InBundle, byte[] Body, DotRecordCandidate? Record);

/// <summary>Alle 04-38- und 05-38-Rahmen einer Aufnahme.</summary>
public sealed record DamageCandidateAnalysis(
    RecordingAnalysis Recording,
    IReadOnlyList<DamageCandidateFrame> Frames,
    IReadOnlyList<DotCandidateFrame>? DotFrames = null)
{
    public IReadOnlyList<DotCandidateFrame> Dots => DotFrames ?? [];
}

/// <summary>
/// Liest Rahmen vom Typ 04 38 aus einer Aufnahme und zerlegt sie nach der Community-Hypothese
/// (<see cref="DamageRecordHypothesis"/>). Ausgegeben werden nur Zählungen und „Kandidatenwerte“ zum Abgleich
/// mit dem Spiel – nichts davon gilt als gesicherter Schaden.
/// </summary>
public static class DamageCandidateReport
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly IReadOnlySet<ushort> Types = new HashSet<ushort> { DamageRecordHypothesis.Type, DotRecordHypothesis.Type };

    /// <summary>Wertet die Datei aus (nur lesend).</summary>
    public static async Task<DamageCandidateAnalysis> AnalyzeAsync(
        IRawPacketFileSource source,
        string path,
        IEnumerable<string>? localAddresses = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var analyzer = Create(localAddresses, out var frames, out var dots);
        await source.ReadRawAsync(path, analyzer.Add, cancellationToken).ConfigureAwait(false);
        return Finish(analyzer, frames, dots);
    }

    /// <summary>Für Tests und eigene Paketquellen: Auswertung aus einer Paketfolge.</summary>
    public static DamageCandidateAnalysis Analyze(IEnumerable<RawPacket> packets, IEnumerable<string>? localAddresses = null)
    {
        ArgumentNullException.ThrowIfNull(packets);
        var analyzer = Create(localAddresses, out var frames, out var dots);
        foreach (var packet in packets) analyzer.Add(packet);
        return Finish(analyzer, frames, dots);
    }

    private static RecordingAnalyzer Create(
        IEnumerable<string>? localAddresses, out List<DamageCandidateFrame> frames, out List<DotCandidateFrame> dots)
    {
        var damage = new List<DamageCandidateFrame>();
        var dot = new List<DotCandidateFrame>();
        frames = damage;
        dots = dot;
        return new RecordingAnalyzer(localAddresses, Types, (key, frame) =>
        {
            if (frame.Type == DamageRecordHypothesis.Type)
            {
                damage.Add(new DamageCandidateFrame(damage.Count + 1, frame.Time, key, frame.InBundle, frame.Body,
                    DamageRecordHypothesis.Parse(frame.Body)));
            }
            else
            {
                dot.Add(new DotCandidateFrame(dot.Count + 1, frame.Time, key, frame.InBundle, frame.Body,
                    DotRecordHypothesis.Parse(frame.Body)));
            }
        });
    }

    private static DamageCandidateAnalysis Finish(RecordingAnalyzer analyzer, List<DamageCandidateFrame> frames, List<DotCandidateFrame> dots)
    {
        var recording = analyzer.GetResult(); // liefert am Ende noch wartende Rahmen
        return new DamageCandidateAnalysis(recording, frames, dots);
    }

    /// <summary>Deutscher Textbericht: Zusammenfassung je Richtung und die ersten <paramref name="maxRows"/> Datensätze.</summary>
    public static string Format(DamageCandidateAnalysis analysis, string title, int maxRows = 300)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var sb = new StringBuilder();
        sb.AppendLine($"04-38-Kandidaten: {title}");
        sb.AppendLine("Hypothese aus einer Community-Beschreibung (cyberbadger6969/aion2-dps-meter), nicht gesichert.");
        sb.AppendLine("„Kandidatenwert“ = das Feld, das dort als Schaden beschrieben wird. Erst ein Abgleich mit den Zahlen im Spiel zeigt, ob es stimmt.");

        if (analysis.Frames.Count == 0 && analysis.Dots.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("Keine Rahmen vom Typ 04 38 oder 05 38 gefunden (oder die Rahmen-Hypothese ging in keiner Richtung auf).");
            return sb.ToString();
        }

        foreach (var group in analysis.Frames.GroupBy(f => f.Stream))
        {
            AppendSummary(sb, group.Key, group.ToList(), analysis.Recording.First);
        }

        foreach (var group in analysis.Dots.GroupBy(f => f.Stream))
        {
            AppendDotSummary(sb, group.Key, group.ToList());
        }

        var rows = Rows(analysis).Take(maxRows).ToList();
        sb.AppendLine();
        sb.AppendLine($"Datensätze der Reihe nach (erste {Count(rows.Count)}; alle stehen in der CSV-Datei):");
        sb.AppendLine("  Zeit          s       Rahmen  Ziel      Akteur    Fertigkeit  Art  Kandidat     Zusatz  Zustand");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Create(German,
                $"  {r.Time.ToLocalTime():HH:mm:ss.fff}  {r.Seconds,6:0.000}  {r.Frame,6}  {r.Record?.Target,-8}  {r.Record?.Actor,-8}  {r.Record?.Skill,-10}  {r.Record?.DamageType,-3}  {r.Record?.Candidate,-11}  {(r.Record is { ExtraHits: > 0 } x ? $"{x.ExtraHits}×{x.ExtraSum}" : ""),-6}  {r.State}"));
        }

        return sb.ToString();
    }

    private static void AppendSummary(StringBuilder sb, TcpStreamKey stream, List<DamageCandidateFrame> frames, DateTimeOffset? start)
    {
        var complete = frames.Count(f => f.Result.Status == DamageFrameStatus.Complete);
        var partly = frames.Count(f => f.Result.Status == DamageFrameStatus.PartlyParsed);
        var cast = frames.Count(f => f.Result.Status == DamageFrameStatus.CastMarker);
        var rejected = frames.Where(f => f.Result.Status == DamageFrameStatus.Rejected).ToList();
        var records = frames.SelectMany(f => f.Result.Records).ToList();
        var others = records.Where(r => !r.SelfTarget).ToList();
        var self = records.Where(r => r.SelfTarget).ToList();

        sb.AppendLine();
        sb.AppendLine($"Richtung {stream}: {Count(frames.Count)} Rahmen 04 38");
        sb.AppendLine($"  vollständig zerlegt: {Count(complete)} ({Percent(complete, frames.Count)}), teilweise: {Count(partly)}, Ankündigung (Layout 0): {Count(cast)}, nicht passend: {Count(rejected.Count)} – {Verdict(complete, cast, frames.Count)}");
        if (rejected.Count > 0)
        {
            sb.AppendLine("  Gründe „nicht passend“: " + string.Join(", ",
                rejected.GroupBy(f => f.Result.Problem).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}: {Count(g.Count())}")));
        }

        if (records.Count == 0) return;
        sb.AppendLine($"  Datensätze: {Count(records.Count)} (Akteur ≠ Ziel: {Count(others.Count)}, Akteur = Ziel: {Count(self.Count)})");
        sb.AppendLine($"  verschiedene mutmaßliche Ziele: {Count(records.Select(r => r.Target).Distinct().Count())}, Akteure: {Count(records.Select(r => r.Actor).Distinct().Count())}, Fertigkeiten: {Count(records.Select(r => r.Skill).Distinct().Count())}");
        sb.AppendLine("  Layouts: " + string.Join(", ", records.GroupBy(r => r.Layout).OrderBy(g => g.Key).Select(g => $"{g.Key}: {Count(g.Count())}"))
            + "; Art-Feld: " + string.Join(", ", records.GroupBy(r => r.DamageType).OrderBy(g => g.Key).Select(g => $"{g.Key}: {Count(g.Count())}")));
        if (others.Count > 0)
        {
            sb.AppendLine($"  Kandidatenwert (Akteur ≠ Ziel): Summe {Count(others.Sum(r => (long)r.Candidate))}, Min {Count(others.Min(r => r.Candidate))}, Max {Count(others.Max(r => r.Candidate))}, Ø {others.Average(r => (double)r.Candidate).ToString("0", German)}");
            var top = others.GroupBy(r => r.Actor).OrderByDescending(g => g.Sum(r => (long)r.Candidate)).Take(5)
                .Select(g => $"{g.Key}: {Count(g.Count())} Datensätze, Summe {Count(g.Sum(r => (long)r.Candidate))}");
            sb.AppendLine("  nach mutmaßlichem Akteur: " + string.Join("; ", top));
        }

        if (self.Count > 0)
        {
            sb.AppendLine($"  Kandidatenwert (Akteur = Ziel): Summe {Count(self.Sum(r => (long)r.Candidate))}");
        }

        if (start is { } s && frames.Count > 0)
        {
            sb.AppendLine(string.Create(German, $"  Zeitraum der Rahmen: {(frames[0].Time - s).TotalSeconds:0.0} s bis {(frames[^1].Time - s).TotalSeconds:0.0} s nach Aufnahmebeginn"));
        }
    }

    private static void AppendDotSummary(StringBuilder sb, TcpStreamKey stream, List<DotCandidateFrame> frames)
    {
        var parsed = frames.Where(f => f.Record is not null).Select(f => f.Record!).ToList();
        var complete = parsed.Count(r => r.Complete);

        sb.AppendLine();
        sb.AppendLine($"Richtung {stream}: {Count(frames.Count)} Rahmen 05 38 (Community: Schaden/Heilung über Zeit)");
        sb.AppendLine($"  Felder passen: {Count(parsed.Count)}, davon Rahmen genau zu Ende: {Count(complete)} ({Percent(complete, frames.Count)}) – {Verdict(complete, 0, frames.Count)}");
        if (parsed.Count == 0) return;
        sb.AppendLine("  Effekt-Byte: " + string.Join(", ", parsed.GroupBy(r => r.Effect).OrderBy(g => g.Key).Select(g => $"{g.Key:X2}: {Count(g.Count())}")));
        var top = parsed.GroupBy(r => r.Actor)
            .OrderByDescending(g => g.Where(r => r.IsDamageEffect).Sum(r => (long)r.Amount))
            .Take(5)
            .Select(g => $"{g.Key}: Schadenseffekt {Count(g.Where(r => r.IsDamageEffect).Sum(r => (long)r.Amount))} ({Count(g.Count(r => r.IsDamageEffect))}×), Heileffekt {Count(g.Where(r => r.IsHealEffect).Sum(r => (long)r.Amount))}, sonst {Count(g.Count(r => !r.IsDamageEffect && !r.IsHealEffect))}×");
        sb.AppendLine("  nach mutmaßlichem Akteur: " + string.Join("; ", top));
    }

    /// <summary>
    /// „bestätigt“ ab 90 % vollständig zerlegter Rahmen (Ankündigungen zählen als passend), „nicht bestätigt“ unter 50 %.
    /// </summary>
    private static string Verdict(int complete, int cast, int total)
    {
        if (total < 20) return "zu wenig Rahmen";
        var share = (double)(complete + cast) / total;
        return share >= 0.9 ? "Hypothese bestätigt (Aufbau)" : share < 0.5 ? "Hypothese nicht bestätigt" : "unklar";
    }

    private sealed record Row(DateTimeOffset Time, double Seconds, int Frame, int Index, DamageCandidateFrame Source, DamageRecordCandidate? Record, string State);

    private static IEnumerable<Row> Rows(DamageCandidateAnalysis analysis)
    {
        var start = analysis.Recording.First ?? analysis.Frames.FirstOrDefault()?.Time ?? DateTimeOffset.MinValue;
        foreach (var f in analysis.Frames)
        {
            var seconds = (f.Time - start).TotalSeconds;
            var state = f.Result.Status switch
            {
                DamageFrameStatus.Complete => "vollständig",
                DamageFrameStatus.PartlyParsed => "teilweise: " + f.Result.Problem,
                DamageFrameStatus.CastMarker => "Ankündigung",
                _ => "nicht passend: " + f.Result.Problem,
            };
            if (f.Result.Records.Count == 0)
            {
                yield return new Row(f.Time, seconds, f.Number, 0, f, null, state);
                continue;
            }

            for (var i = 0; i < f.Result.Records.Count; i++)
            {
                yield return new Row(f.Time, seconds, f.Number, i + 1, f, f.Result.Records[i], state);
            }
        }
    }

    /// <summary>
    /// CSV (Semikolon, UTF-8 mit BOM für Excel): eine Zeile je Datensatz, Rahmen ohne Datensatz als eigene Zeile.
    /// Zahlen ohne Tausendertrennzeichen.
    /// </summary>
    public static string ToCsv(DamageCandidateAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var sb = new StringBuilder();
        sb.AppendLine("Zeit;Sekunden;Richtung;Rahmen;Datensatz;InBuendel;Zustand;Ziel;Akteur;AkteurGleichZiel;Fertigkeit;Layout;Schalter;Flag;Trefferbyte;Art;Modifikator;Richtungsbyte;Uebersprungen;Wert1;Kandidatenwert;Zusatzanzahl;Zusatzsumme;Lebensraub;Rohbytes;Typ");
        static string N(object? v) => v is null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        var start = analysis.Recording.First ?? analysis.Frames.FirstOrDefault()?.Time ?? DateTimeOffset.MinValue;

        // 04-38- und 05-38-Zeilen nach Zeit zusammenführen (bei gleicher Zeit 04 38 zuerst).
        var lines = new List<(DateTimeOffset Time, int Order, string Line)>();
        var order = 0;
        foreach (var r in Rows(analysis))
        {
            var x = r.Record;
            lines.Add((r.Time, order++, string.Join(";",
                r.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                r.Seconds.ToString("0.000", CultureInfo.InvariantCulture),
                Csv(r.Source.Stream.ToString()),
                N(r.Frame),
                N(r.Index),
                r.Source.InBundle ? "ja" : "nein",
                Csv(r.State),
                N(x?.Target),
                N(x?.Actor),
                x is null ? "" : x.SelfTarget ? "ja" : "nein",
                N(x?.Skill),
                N(x?.Layout),
                N(x?.Switch),
                N(x?.Flag),
                N(x?.HitByte),
                N(x?.DamageType),
                N(x?.Modifier),
                N(x?.Direction),
                N(x?.ValueSkip),
                N(x?.Scalar),
                N(x?.Candidate),
                N(x?.ExtraHits),
                N(x?.ExtraSum),
                N(x?.LifeSteal),
                r.Index <= 1 ? Convert.ToHexString(r.Source.Body) : "",
                "04 38")));
        }

        foreach (var d in analysis.Dots)
        {
            var x = d.Record;
            var state = x is null ? "nicht passend" : x.Complete ? "vollständig" : "teilweise";
            lines.Add((d.Time, order++, string.Join(";",
                d.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                (d.Time - start).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
                Csv(d.Stream.ToString()),
                N(d.Number),
                "1",
                d.InBundle ? "ja" : "nein",
                state,
                N(x?.Target),
                N(x?.Actor),
                x is null ? "" : x.Actor == x.Target ? "ja" : "nein",
                N(x?.Skill),
                "", "", "", "",
                x is null ? "" : $"0x{x.Effect:X2}",
                "", "",
                "",
                N(x?.Skipped),
                N(x?.Amount),
                "", "", "",
                Convert.ToHexString(d.Body),
                "05 38")));
        }

        foreach (var line in lines.OrderBy(l => l.Time).ThenBy(l => l.Order)) sb.AppendLine(line.Line);
        return sb.ToString();
    }

    /// <summary>Freier Dateiname neben anderen Auswertungen; vorhandene Dateien werden nie überschrieben.</summary>
    public static string CreateCsvPath(string directory, string recordingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        var name = Path.GetFileNameWithoutExtension(recordingPath) + "-0438";
        var path = Path.Combine(directory, name + ".csv");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{name}-{i}.csv");
        return path;
    }

    private static string Csv(string value) =>
        value.Contains(';') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static string Percent(int part, int total) =>
        total == 0 ? "–" : ((double)part / total * 100).ToString("0.0", German) + " %";

    private static string Count(long value) => CaptureDisplay.FormatCount(value);
}
