namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Ein Netzwerkadapter, auf dem aufgenommen werden kann.
/// </summary>
/// <param name="Id">Technischer Name (bei Npcap z. B. \Device\NPF_{…}).</param>
/// <param name="Description">Beschreibung, z. B. „Intel(R) Ethernet Connection“.</param>
/// <param name="IsLoopback">true für den Loopback-Adapter (lokaler Verkehr).</param>
/// <param name="Addresses">IP-Adressen des Adapters.</param>
public sealed record NetworkAdapterInfo(string Id, string Description, bool IsLoopback, IReadOnlyList<string> Addresses)
{
    /// <summary>Anzeigename: Beschreibung oder, falls leer, die Id.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Id : Description;
}

/// <summary>
/// Ein empfangenes Paket. Bewusst nur Zeit und Länge – der Inhalt wird in der Diagnose nicht gespeichert.
/// </summary>
/// <param name="Timestamp">Empfangszeit.</param>
/// <param name="Length">Länge in Bytes.</param>
public readonly record struct CapturedPacket(DateTimeOffset Timestamp, int Length);

/// <summary>
/// Ergebnis der Verfügbarkeitsprüfung einer Aufnahme-Anbindung.
/// </summary>
/// <param name="IsAvailable">true, wenn Aufnahmen grundsätzlich möglich sind.</param>
/// <param name="Version">Versionstext (z. B. Npcap-Version) oder null.</param>
/// <param name="Error">Fehlerart, wenn nicht verfügbar.</param>
/// <param name="Detail">Technische Zusatzinfo oder null.</param>
public sealed record CaptureBackendInfo(bool IsAvailable, string? Version, CaptureErrorKind? Error = null, string? Detail = null);

/// <summary>
/// Anbindung an eine Paketerfassung (z. B. Npcap). Nur lesend/passiv:
/// Implementierungen dürfen keine Pakete senden oder verändern.
/// </summary>
public interface IPacketCaptureBackend
{
    /// <summary>Prüft, ob die Aufnahme grundsätzlich möglich ist. Wirft keine Ausnahmen.</summary>
    CaptureBackendInfo GetInfo();

    /// <summary>Listet die Netzwerkadapter auf.</summary>
    /// <exception cref="CaptureException">Wenn die Liste nicht gelesen werden kann.</exception>
    IReadOnlyList<NetworkAdapterInfo> GetAdapters();

    /// <summary>
    /// Nimmt auf dem Adapter auf und ruft <paramref name="onPacket"/> für jedes Paket auf,
    /// bis <paramref name="cancellationToken"/> ausgelöst wird.
    /// </summary>
    /// <exception cref="CaptureException">
    /// Bei Fehlern (Adapter fehlt, kann nicht geöffnet werden, ungültiger Filter, Datei nicht schreibbar, Lesefehler).
    /// </exception>
    /// <exception cref="OperationCanceledException">Darf beim Abbruch geworfen werden.</exception>
    Task CaptureAsync(string adapterId, CaptureOptions options, Action<CapturedPacket> onPacket, CancellationToken cancellationToken);
}

/// <summary>
/// Optionen einer Aufnahme.
/// </summary>
/// <param name="Filter">
/// Paketfilter in libpcap/BPF-Syntax (z. B. „tcp port 1234“), der schon im Treiber angewendet wird;
/// null = alle Pakete.
/// </param>
/// <param name="SaveFilePath">Pfad einer .pcap-Datei, in die die gefilterten Pakete lokal gespeichert werden; null = nicht speichern.</param>
/// <param name="Process">
/// Nur Pakete dieses Programms melden und speichern; die Verbindungen werden laufend nachgelesen
/// (<see cref="ProcessPacketSelector{T}"/>). Pakete erscheinen dadurch etwa 2 s verzögert. null = keine Programmauswahl.
/// </param>
/// <param name="OnRawPacket">
/// Bekommt jedes (ausgewählte) Paket mit Inhalt, z. B. für die Live-Auswertung. Der Inhalt bleibt im Speicher und
/// wird dadurch nicht gespeichert. Wird auf dem Aufnahme-Thread aufgerufen; null = nur Zeit und Länge melden.
/// </param>
public sealed record CaptureOptions(
    string? Filter = null,
    string? SaveFilePath = null,
    ProcessSelection? Process = null,
    Action<Analysis.RawPacket>? OnRawPacket = null)
{
    /// <summary>Keine Filterung, nichts speichern.</summary>
    public static CaptureOptions Default { get; } = new();
}
