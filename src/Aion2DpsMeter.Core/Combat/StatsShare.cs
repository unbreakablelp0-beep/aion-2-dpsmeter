using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Bestenliste (freiwillig): Ziel-Datenbank (Supabase des Besitzers). Ohne öffentlichen Schlüssel ist das Teilen aus.
/// Der „anon“-Schlüssel ist für die Verwendung in Apps gedacht und darf öffentlich sein; er erlaubt laut Datenbank-Regeln
/// nur das Einfügen (siehe tools/supabase-setup.sql).
/// </summary>
public static class StatsShareConfig
{
    public const string Url = "https://yclftixyshwhxeodkbvy.supabase.co";
    public const string AnonKey = "sb_publishable_O2MWhmfbcWbyZgNX2gnE8Q_Ouk6W0H-";

    public static bool Configured => AnonKey.Length > 0;

    public static Uri InsertEndpoint => new($"{Url}/rest/v1/fights");
}

/// <summary>Was geteilt wird – nur eigene Werte eines Bosskampfs, nie Namen anderer Spieler.</summary>
public sealed record SharedFight(
    [property: JsonPropertyName("boss")] string Boss,
    [property: JsonPropertyName("duration_s")] int DurationSeconds,
    [property: JsonPropertyName("damage")] long Damage,
    [property: JsonPropertyName("dps")] int Dps,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("player")] string? Player)
{
    /// <summary>
    /// Aus einem gespeicherten Kampf: nur Bosskämpfe ab 10 s mit eigenem Schaden und bekannter Klasse. Der eigene Name
    /// nur bei <paramref name="withName"/>; sonst anonym.
    /// </summary>
    public static SharedFight? From(FightRecord fight, string version, bool withName)
    {
        ArgumentNullException.ThrowIfNull(fight);
        if (fight.BossName is not { Length: > 0 } boss || fight.Duration < TimeSpan.FromSeconds(10)) return null;
        if (fight.Players.FirstOrDefault(p => p.IsOwn) is not { Damage: > 0 } own) return null;
        if (own.ClassPrefix is not { } prefix || SkillClassMap.Describe(prefix) is not { } className) return null;
        var name = withName && own.Name is { Length: >= 2 and <= 24 } n ? n : null;
        return new SharedFight(boss.Length > 80 ? boss[..80] : boss, (int)Math.Min(7200, fight.Duration.TotalSeconds), own.Damage,
            (int)Math.Clamp(Math.Round(own.Dps), 1, 50_000_000), className, version, name);
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

/// <summary>Teilen-Einstellung.</summary>
public static class ShareModes
{
    public const string Off = "Aus";
    public const string Anonymous = "Anonym";
    public const string WithName = "Mit Name";

    public static readonly string[] All = [Off, Anonymous, WithName];
}
