using System.Globalization;
using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>Angaben zu einer Gegner-Nummer aus der Community-Liste.</summary>
public sealed record NpcInfo(string Name, bool IsBoss, bool IsDummy);

/// <summary>
/// Optionale, nur lokal vorhandene Gegnerliste aus der Community (Format
/// <c>{ "2400800": { "name": "…", "isBoss": true, "isDummy": false }, … }</c>), z. B. aus cyberbadger6969/aion2-dps-meter
/// <c>data/npcs/de.json</c> (dort laut NOTICE aus Spieldaten © NCSOFT gewonnen). Wird nicht mitgeliefert und nie
/// geschrieben. Trainingspuppen/Sandsäcke gelten als Puppe.
/// </summary>
public sealed class NpcNames
{
    private readonly Dictionary<int, NpcInfo> _npcs;

    private NpcNames(Dictionary<int, NpcInfo> npcs, string? problem)
    {
        _npcs = npcs;
        Problem = problem;
    }

    public static NpcNames Empty { get; } = new(new Dictionary<int, NpcInfo>(), null);

    public int Count => _npcs.Count;

    public string? Problem { get; }

    public static NpcNames Load(string path)
    {
        if (!File.Exists(path)) return Empty;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var npcs = new Dictionary<int, NpcInfo>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!int.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var code) || p.Value.ValueKind != JsonValueKind.Object) continue;
                if (!p.Value.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } name) continue;
                var boss = p.Value.TryGetProperty("isBoss", out var b) && b.ValueKind == JsonValueKind.True;
                var dummy = p.Value.TryGetProperty("isDummy", out var d) && d.ValueKind == JsonValueKind.True;
                npcs[code] = new NpcInfo(name, boss, dummy || IsDummyName(name));
            }

            return new NpcNames(npcs, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new NpcNames(new Dictionary<int, NpcInfo>(), $"Nicht lesbar: {ex.Message}");
        }
    }

    /// <summary>Für Tests.</summary>
    public static NpcNames From(IReadOnlyDictionary<int, NpcInfo> npcs) => new(npcs.ToDictionary(n => n.Key, n => n.Value), null);

    public NpcInfo? Lookup(int? code) => code is { } c && _npcs.TryGetValue(c, out var info) ? info : null;

    private static bool IsDummyName(string name) =>
        name.Contains("Trainingspuppe", StringComparison.OrdinalIgnoreCase) || name.Contains("Sandsack", StringComparison.OrdinalIgnoreCase);
}
