using System.Globalization;
using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Optionale, nur lokal vorhandene Namensliste aus der Community (Format <c>{ "14020340": "Name", … }</c>). Sie wird
/// nicht mitgeliefert und nie geschrieben, das Programm liest sie nur. Die Namen sind Vorschläge, nicht abgeglichen.
/// Gibt es eine Nummer nicht, gilt die Grundnummer (letzte vier Stellen 0). Beispiel: 14800007 → 14800000. Das hat
/// sich an den eigenen Aufnahmen als passend gezeigt (README).
/// </summary>
public sealed class CommunitySkillNames
{
    private readonly Dictionary<uint, string> _names;

    private CommunitySkillNames(Dictionary<uint, string> names, string? problem)
    {
        _names = names;
        Problem = problem;
    }

    /// <summary>Leere Liste (Datei fehlt).</summary>
    public static CommunitySkillNames Empty { get; } = new(new Dictionary<uint, string>(), null);

    /// <summary>Anzahl Einträge.</summary>
    public int Count => _names.Count;

    /// <summary>Hinweis, falls die Datei vorhanden, aber nicht lesbar war.</summary>
    public string? Problem { get; }

    /// <summary>Liest die Datei; fehlt sie, ist die Liste leer. Fehler machen die Liste leer und stehen in <see cref="Problem"/>.</summary>
    public static CommunitySkillNames Load(string path)
    {
        if (!File.Exists(path)) return Empty;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            var names = new Dictionary<uint, string>();
            foreach (var (key, value) in raw ?? new Dictionary<string, string>())
            {
                if (uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && !string.IsNullOrWhiteSpace(value))
                {
                    names[id] = value.Trim();
                }
            }

            return new CommunitySkillNames(names, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new CommunitySkillNames(new Dictionary<uint, string>(), $"Nicht lesbar: {ex.Message}");
        }
    }

    /// <summary>Für Tests: Liste aus Einträgen.</summary>
    public static CommunitySkillNames From(IReadOnlyDictionary<uint, string> names) => new(names.ToDictionary(n => n.Key, n => n.Value), null);

    /// <summary>Name zur Nummer, sonst zur Grundnummer, sonst null.</summary>
    public string? NameOf(uint skill)
    {
        if (_names.TryGetValue(skill, out var name)) return name;
        var baseId = skill - (skill % 10_000);
        return skill >= 1_000_000 && baseId != skill && _names.TryGetValue(baseId, out var baseName) ? baseName : null;
    }
}
