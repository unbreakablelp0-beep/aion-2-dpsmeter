using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>Zuletzt erkannter eigener Charakter (nur eigene Daten).</summary>
/// <param name="Id">Kennung aus Typ 33 36 – gilt nur bis zum nächsten Einloggen.</param>
/// <param name="Name">Name des eigenen Charakters.</param>
/// <param name="DetectedAt">Zeitpunkt der Erkennung.</param>
/// <param name="ClassPrefix">Zuletzt erkannte Klasse (Fertigkeits-Präfix), damit das Symbol schon vor dem ersten Treffer stimmt.</param>
public sealed record RememberedOwnCharacter(uint Id, string Name, DateTimeOffset DetectedAt, int? ClassPrefix = null);

/// <summary>
/// Merkt sich den eigenen Charakter lokal, damit er nach einem Neustart des Programms (ohne neues Einloggen)
/// weiter erkannt wird. Wie bei den Overlay-Einstellungen wird eine vorhandene Datei nur ersetzt, wenn sie vom
/// Programm stammt. Namen anderer Spieler werden hier nie gespeichert.
/// </summary>
public sealed class OwnCharacterStore(string path)
{
    public const string Marker = "Aion2DpsMeter-OwnCharacter";

    /// <summary>Älter als das wird ein gemerkter Charakter nicht mehr verwendet.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private bool _foreign;

    public string Path { get; } = path;

    /// <summary>Gemerkter Charakter oder null (fehlt, zu alt, fremde/kaputte Datei).</summary>
    public RememberedOwnCharacter? Load(DateTimeOffset now)
    {
        if (!File.Exists(Path)) return null;
        try
        {
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json);
            if (file?.Kind != Marker || file.Character is not { } c || string.IsNullOrWhiteSpace(c.Name))
            {
                _foreign = true;
                return null;
            }

            return now - c.DetectedAt <= MaxAge && c.DetectedAt <= now + TimeSpan.FromMinutes(5) ? c : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _foreign = true;
            return null;
        }
    }

    /// <summary>Speichert. false, wenn eine fremde oder kaputte Datei im Weg ist (sie bleibt unverändert).</summary>
    public bool Save(RememberedOwnCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_foreign) return false;
        if (File.Exists(Path))
        {
            // Nur eigene Dateien ersetzen.
            try
            {
                if (JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json)?.Kind != Marker)
                {
                    _foreign = true;
                    return false;
                }
            }
            catch (JsonException)
            {
                _foreign = true;
                return false;
            }
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(new FileContent(Marker, character), Json));
        return true;
    }

    private sealed record FileContent(string Kind, RememberedOwnCharacter? Character);
}
