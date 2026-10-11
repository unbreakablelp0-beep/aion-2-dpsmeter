namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Zustand der Netzwerkaufnahme.
/// </summary>
public enum CaptureState
{
    /// <summary>Die Netzwerkaufnahme ist noch nicht eingerichtet bzw. noch nicht geprüft.</summary>
    NotConfigured,

    /// <summary>Die Aufnahme ist nicht möglich (z. B. Npcap fehlt, keine Berechtigung, keine Adapter).</summary>
    Unavailable,

    /// <summary>Npcap und Adapter sind verfügbar; die Aufnahme kann gestartet werden.</summary>
    Ready,

    /// <summary>Die Aufnahme läuft.</summary>
    Capturing,

    /// <summary>Die Aufnahme wurde regulär beendet.</summary>
    Stopped,

    /// <summary>Die Aufnahme ist mit einem Fehler abgebrochen.</summary>
    Faulted,
}

/// <summary>
/// Beschreibt den aktuellen Zustand der Netzwerkaufnahme mit einem
/// verständlichen deutschen Text für die Oberfläche.
/// </summary>
/// <param name="State">Zustand.</param>
/// <param name="Message">Deutscher Text für die Anzeige.</param>
/// <param name="Error">Fehlerart bei <see cref="CaptureState.Unavailable"/> oder <see cref="CaptureState.Faulted"/>.</param>
/// <param name="Detail">Technische Zusatzinfo (z. B. Originalmeldung von Npcap) oder null.</param>
public sealed record CaptureStatus(CaptureState State, string Message, CaptureErrorKind? Error = null, string? Detail = null)
{
    /// <summary>Status, solange die Aufnahme noch nicht geprüft wurde.</summary>
    public static CaptureStatus NotConfigured { get; } =
        new(CaptureState.NotConfigured, "Netzwerkaufnahme noch nicht eingerichtet");

    /// <summary>Aufnahme nicht möglich.</summary>
    public static CaptureStatus Unavailable(CaptureErrorKind error, string? detail = null) =>
        new(CaptureState.Unavailable, CaptureErrorMessages.Describe(error), error, detail);

    /// <summary>Bereit; <paramref name="adapterCount"/> Adapter gefunden.</summary>
    public static CaptureStatus Ready(int adapterCount) =>
        new(CaptureState.Ready, $"Bereit – {adapterCount} Netzwerkadapter gefunden");

    /// <summary>Aufnahme läuft auf dem genannten Adapter.</summary>
    public static CaptureStatus Capturing(NetworkAdapterInfo adapter) =>
        new(CaptureState.Capturing, $"Aufnahme läuft: {adapter.DisplayName}");

    /// <summary>Aufnahme regulär beendet.</summary>
    public static CaptureStatus Stopped { get; } =
        new(CaptureState.Stopped, "Aufnahme beendet");

    /// <summary>Aufnahme mit Fehler abgebrochen.</summary>
    public static CaptureStatus Faulted(CaptureErrorKind error, string? detail = null) =>
        new(CaptureState.Faulted, CaptureErrorMessages.Describe(error), error, detail);
}
