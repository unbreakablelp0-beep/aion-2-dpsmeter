using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Selbst vergebene Namen für Fertigkeitsnummern (die Daten enthalten nur Nummern). Liegt als kleine JSON-Datei
/// lokal; eine vorhandene Datei wird nur ersetzt, wenn sie vom Programm stammt. Threadsicher.
/// </summary>
public sealed class SkillNameStore(string path)
{
    public const string Marker = "Aion2DpsMeter-SkillNames";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _lock = new();
    private Dictionary<uint, string> _names = new();
    private bool _foreign;
    private bool _loaded;

    public string Path { get; } = path;

    /// <summary>true, wenn eine vorhandene Datei nicht lesbar war oder nicht vom Programm stammt (sie bleibt unverändert).</summary>
    public bool IsForeign
    {
        get { lock (_lock) return _foreign; }
    }

    /// <summary>Liest die Datei (falls vorhanden). Fehlt sie, beginnt die Liste leer.</summary>
    public void Load()
    {
        lock (_lock)
        {
            _loaded = true;
            _names = new Dictionary<uint, string>();
            if (!File.Exists(Path)) return;
            try
            {
                var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json);
                if (file?.Kind != Marker || file.Names is null)
                {
                    _foreign = true;
                    return;
                }

                foreach (var (key, value) in file.Names)
                {
                    if (uint.TryParse(key, out var skill) && !string.IsNullOrWhiteSpace(value)) _names[skill] = value.Trim();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _foreign = true;
            }
        }
    }

    /// <summary>Name oder null.</summary>
    public string? NameOf(uint skill)
    {
        lock (_lock) return _names.GetValueOrDefault(skill);
    }

    /// <summary>Optionale Community-Liste als Rückfall (nur gelesen).</summary>
    public CommunitySkillNames Community { get; set; } = CommunitySkillNames.Empty;

    /// <summary>
    /// Anzeigename: eigener Name, sonst Community-Name mit „*“ (nicht abgeglichen), sonst „#Nummer“.
    /// <paramref name="fromCommunity"/> sagt, ob der Name aus der Community-Liste stammt.
    /// </summary>
    public string DisplayName(uint skill, out bool fromCommunity)
    {
        fromCommunity = false;
        if (NameOf(skill) is { } own) return own;
        if (Community.NameOf(skill) is { } community)
        {
            fromCommunity = true;
            return community + "*";
        }

        return $"#{skill.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Setzt (oder löscht bei leerem Text) einen Namen und speichert. false, wenn die Datei fremd ist (nichts geschrieben).
    /// </summary>
    public bool SetName(uint skill, string? name)
    {
        lock (_lock)
        {
            if (!_loaded) throw new InvalidOperationException("Erst Load() aufrufen.");
            if (_foreign) return false;
            if (File.Exists(Path) && !IsOwnFile()) return false;

            if (string.IsNullOrWhiteSpace(name)) _names.Remove(skill);
            else _names[skill] = name.Trim()[..Math.Min(name.Trim().Length, 60)];

            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var content = new FileContent(Marker, _names.OrderBy(n => n.Key).ToDictionary(n => n.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), n => n.Value));
            File.WriteAllText(Path, JsonSerializer.Serialize(content, Json));
            return true;
        }
    }

    private bool IsOwnFile()
    {
        try
        {
            if (JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json)?.Kind == Marker) return true;
        }
        catch (JsonException)
        {
        }

        _foreign = true;
        return false;
    }

    private sealed record FileContent(string Kind, Dictionary<string, string>? Names);
}
