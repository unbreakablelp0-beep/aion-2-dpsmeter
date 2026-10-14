using System.Globalization;
using System.Text;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Quelle für Rohpakete aus Aufzeichnungsdateien (.pcap/.pcapng).
/// </summary>
public interface IRawPacketFileSource
{
    /// <summary>Liest alle Pakete mit Rohdaten.</summary>
    /// <exception cref="CaptureException"><see cref="CaptureErrorKind.FileNotFound"/> oder <see cref="CaptureErrorKind.InvalidFileFormat"/>.</exception>
    /// <exception cref="OperationCanceledException">Beim Abbruch.</exception>
    Task ReadRawAsync(string path, Action<RawPacket> onPacket, CancellationToken cancellationToken);
}

/// <summary>
/// Liest eine Aufzeichnung, wertet sie aus und erzeugt einen deutschen Textbericht.
/// </summary>
public static class RecordingReport
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Wertet die Datei aus. Dateifehler werden als <see cref="CaptureException"/> weitergegeben.</summary>
    public static async Task<RecordingAnalysis> AnalyzeAsync(
        IRawPacketFileSource source,
        string path,
        IEnumerable<string>? localAddresses = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var analyzer = new RecordingAnalyzer(localAddresses);
        await source.ReadRawAsync(path, analyzer.Add, cancellationToken).ConfigureAwait(false);
        return analyzer.GetResult();
    }

    /// <summary>Erzeugt den Textbericht.</summary>
    /// <param name="analysis">Ergebnis.</param>
    /// <param name="title">Überschrift, z. B. der Dateiname.</param>
    /// <param name="maxConversations">Höchstens so viele Verbindungen ausführlich auflisten.</param>
    public static string Format(RecordingAnalysis analysis, string title, int maxConversations = 20)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var sb = new StringBuilder();
        sb.AppendLine($"Auswertung: {title}");
        sb.AppendLine("Hinweis: Es werden nur Standard-Kopfzeilen (IP, TCP, UDP) gelesen. Das Spielprotokoll wird nicht gedeutet.");
        sb.AppendLine();
        sb.AppendLine($"Pakete gesamt:        {Count(analysis.TotalPackets)}");
        sb.AppendLine($"Nicht auswertbar:     {Count(analysis.UnparsedPackets)} (kein IPv4/IPv6 mit TCP/UDP, Fragment o. Ä.)");
        if (analysis.First is { } first && analysis.Last is { } last)
        {
            sb.AppendLine($"Zeitraum:             {first.ToLocalTime():dd.MM.yyyy HH:mm:ss} – {last.ToLocalTime():HH:mm:ss} ({Seconds((last - first).TotalSeconds)} s)");
        }

        sb.AppendLine($"Verbindungen:         {analysis.Conversations.Count}");

        var shown = analysis.Conversations.Take(maxConversations).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var c = shown[i];
            var (aLabel, bLabel) = c.AIsLocal == true ? ("lokal", "Gegenstelle") : ("A", "B");

            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {(c.Protocol == TransportProtocol.Tcp ? "TCP" : "UDP")}  {c.A} ({aLabel})  ↔  {c.B} ({bLabel})");
            sb.AppendLine($"    {aLabel} → {bLabel}: {Count(c.AToB.Packets)} Pakete, {CaptureDisplay.FormatBytes(c.AToB.WireBytes)} gesamt, {CaptureDisplay.FormatBytes(c.AToB.PayloadBytes)} Nutzdaten");
            sb.AppendLine($"    {bLabel} → {aLabel}: {Count(c.BToA.Packets)} Pakete, {CaptureDisplay.FormatBytes(c.BToA.WireBytes)} gesamt, {CaptureDisplay.FormatBytes(c.BToA.PayloadBytes)} Nutzdaten");
            sb.AppendLine($"    Dauer: {Seconds(c.Duration.TotalSeconds)} s, Rate: {(c.PacketsPerSecond is { } pps ? string.Create(German, $"{pps:0.#} Pakete/s") : "–")}");
            sb.AppendLine($"    Nutzdatengrößen: {string.Join(", ", c.PayloadSizes.Where(s => s.Count > 0).Select(s => $"{s.Label}: {Count(s.Count)}"))}");
            sb.AppendLine($"    Entropie (Schätzung): {DescribeEntropy(c.Entropy)}");
            AppendHypothesis(sb, $"{aLabel} → {bLabel}", c.AToBHypothesis);
            AppendHypothesis(sb, $"{bLabel} → {aLabel}", c.BToAHypothesis);
        }

        if (analysis.Conversations.Count > shown.Count)
        {
            sb.AppendLine();
            sb.AppendLine($"… {analysis.Conversations.Count - shown.Count} weitere, kleinere Verbindungen nicht aufgeführt.");
        }

        return sb.ToString();
    }

    /// <summary>Deutscher Text zur Entropie-Schätzung.</summary>
    public static string DescribeEntropy(EntropyEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        var value = estimate.BitsPerByte is { } bits ? string.Create(German, $"{bits:0.00} Bit/Byte – ") : string.Empty;
        return value + estimate.Assessment switch
        {
            EntropyAssessment.TooLittleData =>
                $"zu wenig Nutzdaten ({Count(estimate.SampleBytes)} B, mindestens {Count(EntropyEstimate.MinimumSampleBytes)} B nötig)",
            EntropyAssessment.LikelyPlain => "wirkt eher unverschlüsselt (nur Schätzung)",
            EntropyAssessment.LikelyEncryptedOrCompressed => "wirkt verschlüsselt oder komprimiert (nur Schätzung)",
            _ => "keine klare Aussage möglich (nur Schätzung)",
        };
    }

    /// <summary>Deutscher Text zu einem Hypothesen-Ergebnis.</summary>
    public static string DescribeVerdict(HypothesisVerdict verdict) => verdict switch
    {
        HypothesisVerdict.Confirmed => "Hypothese bestätigt",
        HypothesisVerdict.NotConfirmed => "Hypothese nicht bestätigt",
        HypothesisVerdict.TooLittleData => "zu wenig Daten",
        _ => "unklar",
    };

    private static void AppendHypothesis(StringBuilder sb, string direction, StreamHypothesisResult? h)
    {
        if (h is null || h.StreamBytes == 0) return;

        sb.AppendLine($"    Hypothesen-Prüfung {direction} (Community-Angaben, nicht gesichert): {Count(h.StreamBytes)} B Strom, {Count(h.Gaps)} Lücken");
        var rate = h.HeartbeatsPerSecond is { } hps ? string.Create(German, $", {hps:0.0}/s") : string.Empty;
        sb.AppendLine($"      Heartbeat 0E 00 36: {Count(h.HeartbeatHits)}, 06 00 36: {Count(h.ShortHeartbeatHits)}{rate} – {DescribeVerdict(h.Heartbeat)}");
        var alignment = h.HeartbeatAlignment is { } al ? Percent(al) : "–";
        var noSync = h.HadSyncPoint ? string.Empty : " (kein Aufsetzpunkt: Aufnahme ohne Verbindungsanfang und kein Heartbeat)";
        sb.AppendLine($"      Varint-Rahmen: {Percent(h.FramedShare)} des Stroms in {Count(h.Frames)} Rahmen, {Count(h.Resyncs)} Neuaufsetzer, Heartbeats am Rahmenanfang: {alignment} – {DescribeVerdict(h.Framing)}{noSync}");
        sb.AppendLine($"      LZ4-Bündel: {Count(h.BundleCandidates)} gefunden, {Count(h.BundlesDecoded)} entpackt, {Count(h.BundlesComplete)} vollständig zerlegt ({Count(h.InnerFrames)} innere Rahmen) – {DescribeVerdict(h.Lz4)}");
        AppendMessageTypes(sb, h);
    }

    /// <summary>Community-Hypothese: Typ 04 38 trage Schadensereignisse. Nicht gesichert, wird nicht gedeutet.</summary>
    public const ushort DamageTypeHypothesis = 0x0438;

    /// <summary>Höchstens so viele Nachrichtentypen werden je Richtung aufgelistet.</summary>
    public const int MaxListedMessageTypes = 20;

    private static void AppendMessageTypes(StringBuilder sb, StreamHypothesisResult h)
    {
        // Ohne bestätigte Rahmen wären die „Typen“ nur zufällige Bytepaare.
        if (h.Framing != HypothesisVerdict.Confirmed || h.MessageTypes is not { Count: > 0 } types) return;

        var total = types.Sum(t => t.Count);
        sb.AppendLine($"      Nachrichtentypen (2 Bytes nach der Länge; nur gezählt, nicht gedeutet): {Count(types.Count)} verschiedene, {Count(total)} Rahmen; Größe = ganzer Rahmen");
        if (total >= 100 && types.Count * 2 > total)
        {
            sb.AppendLine("      Achtung: Mehr als die Hälfte der Rahmen hat einen anderen Wert – die 2 Bytes wirken in dieser Richtung nicht wie ein Typfeld (z. B. verschlüsselt oder anders aufgebaut).");
        }
        foreach (var t in types.Take(MaxListedMessageTypes)) sb.AppendLine("        " + DescribeType(t, h.Seconds));
        if (types.Count > MaxListedMessageTypes)
        {
            var rest = types.Skip(MaxListedMessageTypes).ToList();
            sb.AppendLine($"        … {Count(rest.Count)} weitere Typen mit zusammen {Count(rest.Sum(t => t.Count))} Rahmen");
        }

        var damage = h.FindMessageType(DamageTypeHypothesis);
        var reversed = h.FindMessageType((ushort)((DamageTypeHypothesis << 8 | DamageTypeHypothesis >> 8) & 0xFFFF));
        sb.AppendLine("      Typ 04 38 (Community-Hypothese „Schaden“, nicht gesichert, nicht gedeutet): " +
            (damage is null ? "kommt nicht vor" : DescribeType(damage, h.Seconds)) +
            $"; umgekehrte Bytefolge 38 04: {Count(reversed?.Count ?? 0)}");
    }

    private static string DescribeType(MessageTypeStatistics t, double seconds)
    {
        var rate = seconds > 0 ? string.Create(German, $", {t.Count / seconds:0.0}/s") : string.Empty;
        var sizes = t.MinSize == t.MaxSize
            ? $"immer {Count(t.MinSize)} B"
            : string.Create(German, $"{t.MinSize}–{t.MaxSize} B, Ø {t.AverageSize:0.0}, häufigste {t.MostCommonSize} B, {t.DistinctSizes} Größen");
        return $"{t.Hex}: {Count(t.Count)}{rate}, davon {Count(t.InBundles)} in Bündeln, {sizes}";
    }

    private static string Percent(double share) => (share * 100).ToString("0.0", German) + " %";

    private static string Count(long value) => CaptureDisplay.FormatCount(value);

    private static string Seconds(double value) => value.ToString("0.0", German);
}
