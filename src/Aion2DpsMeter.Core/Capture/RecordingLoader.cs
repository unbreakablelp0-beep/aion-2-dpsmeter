namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Quelle für gespeicherte Paketaufzeichnungen (.pcap/.pcapng).
/// </summary>
public interface IPacketFileSource
{
    /// <summary>
    /// Liest alle Pakete der Datei und ruft <paramref name="onPacket"/> für jedes auf.
    /// </summary>
    /// <exception cref="CaptureException">
    /// <see cref="CaptureErrorKind.FileNotFound"/> oder <see cref="CaptureErrorKind.InvalidFileFormat"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Beim Abbruch.</exception>
    Task ReadAsync(string path, Action<CapturedPacket> onPacket, CancellationToken cancellationToken);
}

/// <summary>
/// Ergebnis des Ladens einer Aufzeichnung.
/// </summary>
/// <param name="Path">Dateipfad.</param>
/// <param name="Statistics">Gezählte Pakete (bei Fehlern: bis zum Fehler).</param>
/// <param name="Error">Fehlerart oder null bei Erfolg.</param>
/// <param name="Message">Deutscher Text für die Anzeige.</param>
/// <param name="Detail">Technische Zusatzinfo oder null.</param>
public sealed record RecordingResult(
    string Path,
    PacketStatistics Statistics,
    CaptureErrorKind? Error,
    string Message,
    string? Detail = null)
{
    /// <summary>true, wenn die Datei vollständig gelesen wurde.</summary>
    public bool Success => Error is null;
}

/// <summary>
/// Lädt eine Paketaufzeichnung und zählt die Pakete – damit lässt sich die Auswertung
/// ohne laufendes Spiel prüfen. Paketinhalte werden (noch) nicht ausgewertet.
/// </summary>
public sealed class RecordingLoader
{
    private readonly IPacketFileSource _source;

    public RecordingLoader(IPacketFileSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Lädt die Datei. Fehler werden im Ergebnis gemeldet, Abbruch wirft <see cref="OperationCanceledException"/>.</summary>
    public async Task<RecordingResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var counter = new PacketCounter();
        try
        {
            await _source.ReadAsync(path, counter.Add, cancellationToken).ConfigureAwait(false);
        }
        catch (CaptureException ex)
        {
            return new RecordingResult(path, counter.Snapshot(), ex.Kind, CaptureErrorMessages.Describe(ex.Kind), ex.Message);
        }

        var stats = counter.Snapshot();
        return new RecordingResult(path, stats, null, $"Aufzeichnung geladen: {stats.PacketCount} Pakete, {stats.ByteCount} Bytes");
    }
}
