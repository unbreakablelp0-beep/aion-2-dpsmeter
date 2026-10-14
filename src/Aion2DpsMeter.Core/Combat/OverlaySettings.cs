using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>Gemerkte Einstellungen des Overlays.</summary>
/// <param name="Left">Linke Kante (Bildschirmpunkte).</param>
/// <param name="Top">Obere Kante.</param>
/// <param name="Width">Breite.</param>
/// <param name="Height">Höhe.</param>
/// <param name="BackgroundOpacity">Deckkraft des Hintergrunds (0,1–1).</param>
/// <param name="FontSize">Schriftgröße (9–24).</param>
/// <param name="ClickThrough">Klicks gehen ans Spiel durch. Wird nie gespeichert – nach dem Start immer aus.</param>
/// <param name="MaxRows">Höchstens so viele Spieler (3–20).</param>
/// <param name="IdleSeconds">Kampfende nach so vielen Sekunden ohne Treffer (3–120).</param>
/// <param name="HideTinyRows">Spieler unter 1 % Anteil ausblenden.</param>
/// <param name="ShowAllPlayers">Veraltet (v13–v17), wird nicht mehr verwendet.</param>
/// <param name="ShowPartyHint">Hinweis „Gruppe x/y erkannt“ zeigen, wenn Namen fehlen (nur im und bis 60 s nach dem Kampf); Standard an.</param>
/// <param name="ShowOd">Zusatz-Od-Energie (blauer Wert) in der Fußzeile zeigen; Standard an.</param>
/// <param name="OdTotal">Od-Energie zusammengerechnet zeigen (z. B. 110) statt aufgeteilt („15 (+95)“); Standard an.</param>
/// <param name="ShowLobby">Gruppen-Lobby (Mitglieder mit Stufe, GS, KS) zeigen, solange kein Kampf läuft; Standard an.</param>
/// <param name="ShowPing">Ping zum Spielserver in der Fußzeile zeigen (passiv gemessen); Standard an.</param>
/// <param name="CheckForUpdates">Beim Start und alle 6 h bei GitHub nach einer neuen Version fragen (nur lesen); Standard an.</param>
/// <param name="Contrast">Lesbarkeit: „Normal“, „Hoch“ (Standard) oder „Sehr hoch“ – hellere Schrift, Schatten, Mindest-Deckkraft.</param>
/// <param name="AutoHideSeconds">Overlay nach so vielen Sekunden ohne Kampf ausblenden (10/30/60/120), 0 = nie (Standard).</param>
/// <param name="Theme">Farbthema (Akzentfarbe): Violett, Türkis, Gold, Schlicht.</param>
/// <param name="ShowFightEndCard">Kampfende-Karte (DPS, Anteil, Platz …) nach jedem Kampf zeigen; Standard aus.</param>
/// <param name="NextEventMinutes">„Nächstes Event“ erst so viele Minuten vorher im Overlay zeigen: 5/10/15/30, 0 = immer (Standard 10).</param>
/// <param name="BerserkSeconds">Veraltet (v29–v38, manuelle Berserk-Zeiten); wird ignoriert – der Timer läuft nur automatisch.</param>
/// <param name="ShowCombatPower">Kampfkraft neben dem Namen zeigen (eigener Charakter und Gruppe).</param>
/// <param name="PlayerViewChosen">true, wenn die Ansicht in den Einstellungen gewählt wurde (nur dann gilt sie dauerhaft).</param>
/// <param name="Effects">Effekte und Animationen (Aufblitzen, Pulsieren, Nachziehen, Schatten); aus = ruhige Anzeige.</param>
/// <param name="PlayerView">„Automatik“ (Standard: allein nur ich, in einer Gruppe ich + Gruppe), „Nur ich“ oder „Alle“.</param>
/// <param name="ShowBossBar">HP-Leiste des Hauptziels im Overlay (Standard an).</param>
public sealed record OverlaySettings(
    double Left = 40,
    double Top = 120,
    double Width = 360,
    double Height = 220,
    double BackgroundOpacity = 0.6,
    double FontSize = 13,
    bool ClickThrough = false,
    int MaxRows = 8,
    int IdleSeconds = 10,
    bool HideTinyRows = true,
    IReadOnlyDictionary<string, int>? EventAlarms = null,
    double AlarmVolume = 0.6,
    string AlarmTone = "Glocke",
    bool ShowNextEvent = true,
    bool ShowAllPlayers = false,
    bool ShowBossBar = true,
    string PlayerView = OverlaySettings.ViewAuto,
    bool Effects = true,
    bool PlayerViewChosen = false,
    bool ShowCombatPower = true,
    IReadOnlyDictionary<string, int>? BerserkSeconds = null,
    int NextEventMinutes = 10,
    string Theme = "Violett",
    bool ShowFightEndCard = false,
    int AutoHideSeconds = 0,
    string Contrast = OverlayContrast.High,
    bool ShowPartyHint = true,
    bool CheckForUpdates = true,
    bool ShowPing = true,
    bool ShowOd = true,
    bool ShowLobby = true,
    bool OdTotal = true)
{
    /// <summary>Wählbare Zeiten fürs automatische Ausblenden (0 = aus).</summary>
    public static readonly int[] AutoHideChoices = [0, 10, 30, 60, 120];

    /// <summary>Overlay ausblenden? Nur wenn eingeschaltet und seit dem letzten Kampf länger als die gewählte Zeit vergangen ist.</summary>
    public bool ShouldAutoHide(TimeSpan sinceLastFight) => AutoHideSeconds > 0 && sinceLastFight > TimeSpan.FromSeconds(AutoHideSeconds);

    /// <summary>Wählbare Farbthemen.</summary>
    public static readonly string[] Themes = ["Violett", "Türkis", "Gold", "Schlicht"];

    /// <summary>Automatik: allein nur ich, in einer Gruppe ich + Gruppe (Standard).</summary>
    public const string ViewAuto = "Automatik";

    /// <summary>Nur der eigene Charakter, auch in einer Gruppe.</summary>
    public const string ViewOwn = "Nur ich";

    /// <summary>Alle Spieler, auch Fremde.</summary>
    public const string ViewAll = "Alle";

    /// <summary>Wählbare Ansichten.</summary>
    public static readonly string[] PlayerViews = [ViewAuto, ViewOwn, ViewAll];

    /// <summary>Erlaubte Vorlaufzeiten der Event-Alarme in Minuten.</summary>
    public static readonly int[] AlarmLeadTimes = [1, 5, 10, 15];

    /// <summary>Wählbare Vorlaufzeiten für die Zeile „Nächstes …“ (0 = immer zeigen).</summary>
    public static readonly int[] NextEventLeadTimes = [5, 10, 15, 30, 0];

    /// <summary>Soll die Zeile „Nächstes …“ bei <paramref name="remaining"/> Restzeit schon erscheinen?</summary>
    public bool ShowNextEventNow(TimeSpan remaining) => NextEventMinutes == 0 || remaining <= TimeSpan.FromMinutes(NextEventMinutes);

    /// <summary>Verfügbare Töne (selbst erzeugt).</summary>
    public static readonly string[] AlarmTones = ["Glocke", "Piep", "Gong"];

    /// <summary>Kennung in der Datei: nur Dateien mit diesem Wert werden vom Programm überschrieben.</summary>
    public const string Marker = "Aion2DpsMeter-Overlay";

    /// <summary>Werte auf erlaubte Bereiche begrenzen (z. B. nach Handänderung der Datei).</summary>
    public OverlaySettings Normalized() => this with
    {
        Width = Math.Clamp(Width, 200, 2000),
        Height = Math.Clamp(Height, 80, 2000),
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.1, 1),
        FontSize = Math.Clamp(FontSize, 9, 24),
        MaxRows = Math.Clamp(MaxRows, 3, 20),
        IdleSeconds = Math.Clamp(IdleSeconds == 0 ? 10 : IdleSeconds, 3, 120),
        AlarmVolume = double.IsFinite(AlarmVolume) ? Math.Clamp(AlarmVolume, 0, 1) : 0.6,
        AlarmTone = AlarmTones.Contains(AlarmTone) ? AlarmTone : "Glocke",
        // Alte Dateien (v13–v17) kannten nur „Alle Spieler anzeigen“: übernehmen und dann nicht mehr verwenden.
        // Nur eine bewusst in den Einstellungen gewählte Ansicht gilt dauerhaft. Bis v22 speicherte auch der Knopf im
        // Overlay die Ansicht – so blieb man versehentlich in „Nur ich“ hängen (Gruppe im Dungeon fehlte). Das wird zurückgesetzt.
        PlayerView = !PlayerViewChosen ? ViewAuto : PlayerViews.Contains(PlayerView) ? PlayerView : ViewAuto,
        ShowAllPlayers = false,
        NextEventMinutes = NextEventLeadTimes.Contains(NextEventMinutes) ? NextEventMinutes : 10,
        Theme = Themes.Contains(Theme) ? Theme : "Violett",
        AutoHideSeconds = AutoHideChoices.Contains(AutoHideSeconds) ? AutoHideSeconds : 0,
        Contrast = OverlayContrast.Levels.Contains(Contrast) ? Contrast : OverlayContrast.High,
        BerserkSeconds = null, // manuelle Berserk-Zeiten gibt es nicht mehr (v39): alte Einträge werden ignoriert
        EventAlarms = EventAlarms?
            .Where(a => AlarmLeadTimes.Contains(a.Value))
            .ToDictionary(a => a.Key, a => a.Value),
        Left = double.IsFinite(Left) ? Left : 40,
        Top = double.IsFinite(Top) ? Top : 120,
    };
}

/// <summary>Ergebnis beim Laden.</summary>
public enum OverlaySettingsFileState
{
    /// <summary>Noch keine Datei – Standardwerte, Speichern legt sie an.</summary>
    Missing,

    /// <summary>Eigene Datei gelesen – Speichern darf sie ersetzen.</summary>
    Loaded,

    /// <summary>Datei vorhanden, aber nicht lesbar oder nicht vom Programm – wird nicht überschrieben.</summary>
    Foreign,
}

/// <summary>
/// Liest und schreibt die Einstellungen als kleine JSON-Datei. Eine vorhandene Datei wird nur ersetzt, wenn sie
/// nachweislich vom Programm stammt (Feld <see cref="OverlaySettings.Marker"/>); sonst bleibt sie unangetastet.
/// </summary>
public sealed class OverlaySettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; } = path;

    public OverlaySettingsFileState State { get; private set; } = OverlaySettingsFileState.Missing;

    public OverlaySettings Load()
    {
        if (!File.Exists(Path))
        {
            State = OverlaySettingsFileState.Missing;
            return new OverlaySettings();
        }

        try
        {
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json);
            if (file?.Kind != OverlaySettings.Marker || file.Settings is null)
            {
                State = OverlaySettingsFileState.Foreign;
                return new OverlaySettings();
            }

            State = OverlaySettingsFileState.Loaded;
            return file.Settings.Normalized() with { ClickThrough = false }; // nach dem Start immer aus (Sicherheit)
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            State = OverlaySettingsFileState.Foreign;
            return new OverlaySettings();
        }
    }

    /// <summary>Speichert. false, wenn die vorhandene Datei nicht vom Programm stammt (dann wird nichts geschrieben).</summary>
    public bool Save(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (State == OverlaySettingsFileState.Foreign) return false;
        if (State == OverlaySettingsFileState.Missing && File.Exists(Path)) return false; // inzwischen angelegt: nicht überschreiben

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(new FileContent(OverlaySettings.Marker, settings.Normalized() with { ClickThrough = false }), Json));
        State = OverlaySettingsFileState.Loaded;
        return true;
    }

    private sealed record FileContent(string Kind, OverlaySettings? Settings);
}
