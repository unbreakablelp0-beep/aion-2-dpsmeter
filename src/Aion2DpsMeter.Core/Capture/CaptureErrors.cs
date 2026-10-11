namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Arten von Fehlern bei der Netzwerkaufnahme.
/// </summary>
public enum CaptureErrorKind
{
    /// <summary>Npcap ist nicht installiert oder die Npcap-Bibliothek kann nicht geladen werden.</summary>
    NpcapNotInstalled,

    /// <summary>Keine Berechtigung (z. B. Npcap ist auf Administratoren beschränkt).</summary>
    AccessDenied,

    /// <summary>Es wurde kein Netzwerkadapter gefunden.</summary>
    NoAdaptersFound,

    /// <summary>Die Adapterliste konnte nicht gelesen werden.</summary>
    AdapterListFailed,

    /// <summary>Der gewählte Adapter existiert nicht (mehr).</summary>
    AdapterNotFound,

    /// <summary>Der Adapter konnte nicht geöffnet werden.</summary>
    AdapterOpenFailed,

    /// <summary>Fehler während der laufenden Aufnahme.</summary>
    CaptureFailed,

    /// <summary>Die Aufnahme endete unerwartet, ohne dass sie gestoppt wurde.</summary>
    CaptureAborted,

    /// <summary>Die Aufzeichnungsdatei wurde nicht gefunden.</summary>
    FileNotFound,

    /// <summary>Die Datei ist keine lesbare pcap-/pcapng-Aufzeichnung.</summary>
    InvalidFileFormat,

    /// <summary>Unbekannter Fehler.</summary>
    Unknown,
}

/// <summary>
/// Verständliche deutsche Fehlertexte.
/// </summary>
public static class CaptureErrorMessages
{
    /// <summary>Liefert den Anzeigetext für eine Fehlerart.</summary>
    public static string Describe(CaptureErrorKind kind) => kind switch
    {
        CaptureErrorKind.NpcapNotInstalled =>
            "Npcap wurde nicht gefunden. Bitte Npcap von https://npcap.com installieren.",
        CaptureErrorKind.AccessDenied =>
            "Keine Berechtigung für die Netzwerkaufnahme. Npcap ist eventuell nur für Administratoren freigegeben.",
        CaptureErrorKind.NoAdaptersFound =>
            "Es wurde kein Netzwerkadapter gefunden.",
        CaptureErrorKind.AdapterListFailed =>
            "Die Liste der Netzwerkadapter konnte nicht gelesen werden.",
        CaptureErrorKind.AdapterNotFound =>
            "Der gewählte Netzwerkadapter wurde nicht gefunden. Bitte Adapterliste neu laden.",
        CaptureErrorKind.AdapterOpenFailed =>
            "Der Netzwerkadapter konnte nicht geöffnet werden.",
        CaptureErrorKind.CaptureFailed =>
            "Bei der Aufnahme ist ein Fehler aufgetreten. Die Aufnahme wurde beendet.",
        CaptureErrorKind.CaptureAborted =>
            "Die Aufnahme wurde unerwartet beendet (z. B. Adapter getrennt).",
        CaptureErrorKind.FileNotFound =>
            "Die Aufzeichnungsdatei wurde nicht gefunden.",
        CaptureErrorKind.InvalidFileFormat =>
            "Die Datei ist keine lesbare Paketaufzeichnung (.pcap oder .pcapng) oder sie ist beschädigt.",
        _ =>
            "Unbekannter Fehler bei der Netzwerkaufnahme.",
    };
}

/// <summary>
/// Fehler einer Aufnahme-Anbindung (z. B. Npcap) mit eingeordneter Fehlerart.
/// </summary>
public sealed class CaptureException : Exception
{
    public CaptureException(CaptureErrorKind kind, string? detail = null, Exception? innerException = null)
        : base(detail ?? CaptureErrorMessages.Describe(kind), innerException)
    {
        Kind = kind;
    }

    /// <summary>Fehlerart.</summary>
    public CaptureErrorKind Kind { get; }
}
