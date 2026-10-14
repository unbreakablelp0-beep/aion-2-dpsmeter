using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Merkt die zuletzt erkannte Gruppe lokal (%LOCALAPPDATA%\Aion2DpsMeter\gruppe.json: Größe, Namen, Zeitpunkt), damit
/// sie nach einem Neustart des Meters sofort wieder gilt – das Spiel schickt die Gruppenliste nur gelegentlich. Höchstens
/// <see cref="MaxAge"/> alt. Nur lokal; eine fremde Datei wird nie überschrieben.
/// </summary>
public sealed class PartyStore(string path)
{
    public const string Marker = "Aion2DpsMeter-Gruppe";
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2); // Austritt nicht erkennbar: lieber kurz merken (bis v41: 6 h)
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary>Gemerkte Gruppe oder null (fehlt, zu alt, fremd oder kaputt).</summary>
    public (int Size, IReadOnlyList<string> Names)? Load(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json);
            if (file?.Kind != Marker || file.Names is not { Count: > 0 } names || now - file.SavedAt > MaxAge || file.Size is < 1 or > 12) return null;
            return (file.Size, names);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Speichert; false, wenn eine fremde Datei im Weg ist.</summary>
    public bool Save(int size, IReadOnlyList<string> names, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(names);
        try
        {
            if (File.Exists(Path) && JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json)?.Kind != Marker) return false;
        }
        catch (JsonException)
        {
            return false;
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(new FileContent(Marker, size, names, now), Json));
        return true;
    }

    private sealed record FileContent(string Kind, int Size, IReadOnlyList<string>? Names, DateTimeOffset SavedAt);
}
