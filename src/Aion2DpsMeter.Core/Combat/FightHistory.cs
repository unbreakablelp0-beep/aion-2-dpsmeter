using System.Globalization;
using System.Text.Json;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>Zusammengefasste Treffer eines Spielers gegen ein Ziel mit einer Fähigkeit (keine Rohpakete).</summary>
public sealed record FightSkillRecord(uint Target, uint Skill, HitKind Kind, long Damage, int Hits, int CriticalHits, long Max, DateTimeOffset FirstHit);

/// <summary>Ein Spieler in einem gespeicherten Kampf.</summary>
public sealed record FightPlayerRecord(
    uint Actor,
    string? Name,
    int? ClassPrefix,
    long Damage,
    double Dps,
    bool IsOwn,
    IReadOnlyList<FightSkillRecord> Skills,
    IReadOnlyList<long>? DamagePerSecond = null)
{
    // DamagePerSecond: Schaden je Sekunde ab Kampfbeginn (für die DPS-Kurve); in Kämpfen vor v36 nicht vorhanden.
    public string? ClassName => ClassPrefix is { } p ? SkillClassMap.Describe(p) : null;
}

/// <summary>Ein gespeicherter Kampf.</summary>
/// <param name="Id">Eindeutige Kennung.</param>
/// <param name="Name">Bezeichnung (Standard „Kampf &lt;Datum Uhrzeit&gt;“, umbenennbar).</param>
/// <param name="Start">Erster Treffer.</param>
/// <param name="End">Kampfende (letzter Treffer).</param>
/// <param name="OwnName">Eigener Charakter, falls bekannt.</param>
/// <param name="Players">Spieler, absteigend nach Schaden.</param>
/// <param name="TargetNames">Bekannte Namen von Zielen (z. B. Spieler), sonst leer.</param>
public sealed record FightRecord(
    Guid Id,
    string Name,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? OwnName,
    IReadOnlyList<FightPlayerRecord> Players,
    IReadOnlyDictionary<uint, string>? TargetNames = null,
    string? BossName = null)
{
    // BossName: Boss, dessen Leiste in diesem Kampf zu sehen war (ab v36 gespeichert), sonst null.
    public TimeSpan Duration => End - Start;

    public long TotalDamage => Players.Sum(p => p.Damage);

    /// <summary>Standardname „Kampf 13.10.2026 09:58“ (Ortszeit).</summary>
    public static string DefaultName(DateTimeOffset start) =>
        "Kampf " + start.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Aufschlüsselung eines Spielers wie im Live-Detailfenster (ohne Community-Felder), optional nur gegen ein Ziel.</summary>
    public ActorDetail? Detail(uint actor, uint? target = null)
    {
        if (Players.FirstOrDefault(p => p.Actor == actor) is not { } player) return null;

        var targets = player.Skills
            .GroupBy(s => s.Target)
            .Select(g => new TargetDamage(g.Key, TargetNames?.GetValueOrDefault(g.Key), g.Sum(s => s.Damage)))
            .OrderByDescending(t => t.Damage)
            .ToList();
        var records = target is { } t ? player.Skills.Where(s => s.Target == t).ToList() : player.Skills.ToList();
        if (records.Count == 0) return new ActorDetail(actor, player.Name, player.ClassPrefix, TimeSpan.Zero, 0, 0, 0, 0, targets, []);

        var damage = records.Sum(r => r.Damage);
        var duration = End - records.Min(r => r.FirstHit);
        var direct = records.Where(r => r.Kind != HitKind.OverTime).ToList();
        var skills = records
            .GroupBy(r => (r.Skill, r.Kind))
            .Select(g =>
            {
                var hits = g.Sum(r => r.Hits);
                return new SkillStatistics(g.Key.Skill, g.Key.Kind, g.Sum(r => r.Damage),
                    damage == 0 ? 0 : (double)g.Sum(r => r.Damage) / damage,
                    hits, g.Sum(r => r.CriticalHits), g.Max(r => r.Max), hits == 0 ? 0 : (double)g.Sum(r => r.Damage) / hits);
            })
            .OrderByDescending(s => s.Damage)
            .ThenBy(s => s.Skill)
            .ToList();

        return new ActorDetail(actor, player.Name, player.ClassPrefix, duration, damage,
            damage / Math.Max(1.0, duration.TotalSeconds), direct.Sum(r => r.Hits), direct.Sum(r => r.CriticalHits), targets, skills);
    }

    /// <summary>Für die Suche: passt die Bezeichnung oder ein Spielername?</summary>
    public bool Matches(string? query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
        || Players.Any(p => p.Name?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true);
}

/// <summary>Ein Eintrag der Kämpfe-Liste (Datei + Inhalt).</summary>
public sealed record FightHistoryEntry(string Path, FightRecord Fight);

/// <summary>
/// Speichert Kämpfe lokal als eine JSON-Datei je Kampf. Neue Dateien bekommen einen freien Namen (nie überschreiben);
/// ersetzt bzw. gelöscht werden nur Dateien, die nachweislich vom Programm stammen. Löschen fragt die Oberfläche vorher ab.
/// </summary>
public sealed class FightHistoryStore(string directory)
{
    public const string Marker = "Aion2DpsMeter-Fight";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Directory { get; } = directory;

    /// <summary>Speichert einen neuen Kampf und liefert den Dateipfad.</summary>
    public string Save(FightRecord fight)
    {
        ArgumentNullException.ThrowIfNull(fight);
        System.IO.Directory.CreateDirectory(Directory);
        var baseName = "kampf-" + fight.Start.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(Directory, baseName + ".json");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(Directory, $"{baseName}-{i}.json");

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, new FileContent(Marker, fight), Json);
        return path;
    }

    /// <summary>Alle eigenen Kämpfe, neueste zuerst. Fremde oder kaputte Dateien werden übersprungen.</summary>
    public IReadOnlyList<FightHistoryEntry> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var result = new List<FightHistoryEntry>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            if (Read(path) is { } fight) result.Add(new FightHistoryEntry(path, fight));
        }

        return result.OrderByDescending(e => e.Fight.Start).ToList();
    }

    /// <summary>Benennt um (nur eigene Dateien). false, wenn die Datei fehlt oder nicht vom Programm stammt.</summary>
    public bool Rename(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Read(path) is not { } fight) return false;
        var updated = fight with { Name = name.Trim()[..Math.Min(name.Trim().Length, 80)] };
        File.WriteAllText(path, JsonSerializer.Serialize(new FileContent(Marker, updated), Json));
        return true;
    }

    /// <summary>Löscht (nur eigene Dateien, Rückfrage macht die Oberfläche). false, wenn nicht vom Programm.</summary>
    public bool Delete(string path)
    {
        if (Read(path) is null) return false;
        File.Delete(path);
        return true;
    }

    private static FightRecord? Read(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(path), Json);
            return file?.Kind == Marker ? file.Fight : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record FileContent(string Kind, FightRecord? Fight);
}

/// <summary>Ein Tag der Wochenstatistik.</summary>
/// <param name="Day">Datum (Ortszeit).</param>
/// <param name="Fights">Gespeicherte Kämpfe an dem Tag.</param>
/// <param name="BossFights">Davon mit Boss.</param>
/// <param name="AverageOwnDps">Durchschnitt der eigenen DPS (Kämpfe ab 10 s) oder null.</param>
public sealed record DayStatistics(DateOnly Day, int Fights, int BossFights, double? AverageOwnDps);

/// <summary>Auswertungen über gespeicherte Kämpfe.</summary>
public static class FightStatistics
{
    /// <summary>Kürzere Kämpfe zählen nicht für den Bestwert (kurze Bursts verzerren die DPS).</summary>
    public static readonly TimeSpan MinDurationForBest = TimeSpan.FromSeconds(10);

    /// <summary>Eigene DPS in einem Kampf (eigener Spieler) oder null.</summary>
    public static double? OwnDps(FightRecord fight) => fight.Players.FirstOrDefault(p => p.IsOwn)?.Dps;

    /// <summary>
    /// Wochenstatistik der letzten 7 Tage (Ortszeit, inkl. heute): je Tag Anzahl Kämpfe, davon Boss-Kämpfe, und der
    /// Durchschnitt der eigenen DPS (nur Kämpfe ab 10 s mit eigenem Charakter).
    /// </summary>
    public static IReadOnlyList<DayStatistics> LastSevenDays(IEnumerable<FightRecord> fights, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fights);
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var byDay = fights.GroupBy(f => DateOnly.FromDateTime(f.Start.ToLocalTime().DateTime)).ToDictionary(g => g.Key, g => g.ToList());
        return Enumerable.Range(0, 7).Select(i =>
        {
            var day = today.AddDays(i - 6);
            var list = byDay.GetValueOrDefault(day) ?? [];
            var dps = list.Where(f => f.Duration >= MinDurationForBest).Select(OwnDps).OfType<double>().ToList();
            return new DayStatistics(day, list.Count, list.Count(f => f.BossName is not null), dps.Count == 0 ? null : dps.Average());
        }).ToList();
    }

    /// <summary>Beste eigene DPS je Boss (nur Kämpfe mit bekanntem Boss), absteigend.</summary>
    public static IReadOnlyList<(string Boss, FightRecord Fight, double Dps)> BestPerBoss(IEnumerable<FightRecord> fights)
    {
        ArgumentNullException.ThrowIfNull(fights);
        return fights
            .Where(f => f.BossName is not null && OwnDps(f) is not null)
            .GroupBy(f => f.BossName!)
            .Select(g => g.MaxBy(f => OwnDps(f)!.Value)!)
            .Select(f => (f.BossName!, f, OwnDps(f)!.Value))
            .OrderByDescending(x => x.Item3)
            .ToList();
    }

    /// <summary>Lohnt sich das Speichern? Nur, wenn der eigene Charakter oder ein Gruppenmitglied darin Schaden gemacht hat.</summary>
    public static bool IsRelevant(FightRecord fight, IReadOnlySet<uint> party)
    {
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(party);
        return fight.Players.Any(p => p.IsOwn || party.Contains(p.Actor));
    }

    /// <summary>
    /// Beste eigene DPS aus gespeicherten Kämpfen von mindestens <see cref="MinDurationForBest"/>; der Kampf mit Start
    /// <paramref name="exceptStart"/> (der gerade beendete) zählt nicht. Null, wenn es keinen passenden Kampf gibt.
    /// </summary>
    public static double? BestOwnDps(IEnumerable<FightRecord> fights, DateTimeOffset? exceptStart)
    {
        ArgumentNullException.ThrowIfNull(fights);
        var values = fights
            .Where(f => f.Duration >= MinDurationForBest && f.Start != exceptStart)
            .SelectMany(f => f.Players.Where(p => p.IsOwn).Select(p => p.Dps))
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }
}
