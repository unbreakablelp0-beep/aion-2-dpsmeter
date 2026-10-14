using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aion2DpsMeter.Core.Events;

/// <summary>Art eines Events (nur für die Anzeige).</summary>
public enum GameEventCategory
{
    Rift,
    Event,
    Abyss,
}

/// <summary>
/// Ein wiederkehrendes Spiel-Event mit festem Zeitplan in der Server-Zeitzone (EU: Europe/Berlin). Die Zeiten stammen
/// aus einer vom Nutzer gezeigten Event-Übersicht (13.10.2026) – kein Internetabruf. Ändert das Spiel die Zeiten, passt
/// man die lokale Datei events.json an.
/// </summary>
/// <param name="Id">Kurze Kennung (für Einstellungen).</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="Category">Art.</param>
/// <param name="Description">Zeitplan in Worten.</param>
/// <param name="Days">Wochentage (null = jeden Tag).</param>
/// <param name="Times">Uhrzeiten am Tag (Server-Zeitzone).</param>
public sealed record GameEvent(string Id, string Name, GameEventCategory Category, string Description, IReadOnlyList<DayOfWeek>? Days, IReadOnlyList<TimeOnly> Times)
{
    /// <summary>
    /// Nächster Termin echt nach <paramref name="now"/>. Rechnet in <paramref name="zone"/> (Sommerzeit inklusive):
    /// Uhrzeiten, die es wegen der Zeitumstellung nicht gibt, entfallen; doppelte zählen einmal (erste).
    /// </summary>
    public DateTimeOffset NextAfter(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var zoneNow = TimeZoneInfo.ConvertTime(now, zone);
        for (var day = -1; day <= 8; day++)
        {
            var date = DateOnly.FromDateTime(zoneNow.DateTime).AddDays(day);
            if (Days is not null && !Days.Contains(date.DayOfWeek)) continue;
            foreach (var time in Times.Order())
            {
                var local = date.ToDateTime(time);
                if (zone.IsInvalidTime(local)) continue;
                var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
                var at = new DateTimeOffset(local, offset);
                if (at > now) return at;
            }
        }

        throw new InvalidOperationException($"Kein Termin für {Name} gefunden.");
    }

    /// <summary>Stündlich zur Minute <paramref name="minute"/>.</summary>
    public static IReadOnlyList<TimeOnly> Hourly(int minute) =>
        Enumerable.Range(0, 24).Select(h => new TimeOnly(h, minute)).ToList();

    /// <summary>Alle <paramref name="hours"/> Stunden ab <paramref name="startHour"/>.</summary>
    public static IReadOnlyList<TimeOnly> Every(int hours, int startHour) =>
        Enumerable.Range(0, 24).Where(h => (h - startHour + 24) % hours == 0).Select(h => new TimeOnly(h, 0)).ToList();
}

/// <summary>Zeitplan: Zeitzone und Events.</summary>
public sealed record GameEventSchedule(string TimeZoneId, IReadOnlyList<GameEvent> Events)
{
    /// <summary>EU-Server (Europe/Berlin); auf Windows heißt die Zone „W. Europe Standard Time“.</summary>
    public const string DefaultTimeZone = "W. Europe Standard Time";

    /// <summary>Eingebauter Standard (EU, vom Nutzer am 13.10.2026 geliefert).</summary>
    public static GameEventSchedule Default { get; } = new(DefaultTimeZone,
    [
        new("rift", "Raumzeit-Riss", GameEventCategory.Rift, "alle 3 h ab 02:00", null, GameEvent.Every(3, 2)),
        new("invasion", "Dimensionale Invasion", GameEventCategory.Event, "stündlich um :30", null, GameEvent.Hourly(30)),
        new("shugo", "Shugo-Fest", GameEventCategory.Event, "stündlich um :00", null, GameEvent.Hourly(0)),
        new("arena", "Arena der Taktik", GameEventCategory.Event, "täglich 11:00 und 19:00", null, [new TimeOnly(11, 0), new TimeOnly(19, 0)]),
        new("daily", "Tages-Reset", GameEventCategory.Event, "täglich 09:00", null, [new TimeOnly(9, 0)]),
        new("weekly", "Wochen-Reset", GameEventCategory.Event, "mittwochs 09:00", [DayOfWeek.Wednesday], [new TimeOnly(9, 0)]),
        new("siege", "Artefakt-Belagerung", GameEventCategory.Abyss, "Mo, Do, Sa 21:00", [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday], [new TimeOnly(21, 0)]),
    ]);

    /// <summary>Zeitzone; unbekannte Kennung → Europe/Berlin, notfalls UTC.</summary>
    public TimeZoneInfo Zone
    {
        get
        {
            foreach (var id in new[] { TimeZoneId, DefaultTimeZone, "Europe/Berlin" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                {
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Das als Nächstes anstehende Event.</summary>
    public (GameEvent Event, DateTimeOffset At) Next(DateTimeOffset now)
    {
        var zone = Zone;
        return Events.Select(e => (e, e.NextAfter(now, zone))).MinBy(x => x.Item2);
    }

    /// <summary>„45m 36s“, „2h 05m“, „2d 10h 45m“.</summary>
    public static string Countdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes:00}m";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{span.Minutes}m {span.Seconds:00}s";
    }
}

/// <summary>
/// Liest den Zeitplan aus einer lokalen JSON-Datei (vom Nutzer änderbar). Fehlt sie, wird der Standard verwendet und
/// – nur dann – als Vorlage angelegt; vorhandene oder fremde Dateien werden nie überschrieben.
/// </summary>
public sealed class GameEventScheduleStore(string path)
{
    public const string Marker = "Aion2DpsMeter-Events";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    /// <summary>Hinweis, falls die Datei nicht verwendet werden konnte (sonst null).</summary>
    public string? Problem { get; private set; }

    public GameEventSchedule Load()
    {
        Problem = null;
        if (!File.Exists(Path))
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using var stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(stream, new FileContent(Marker, GameEventSchedule.Default.TimeZoneId, GameEventSchedule.Default.Events), Json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Problem = $"Vorlage nicht angelegt: {ex.Message}";
            }

            return GameEventSchedule.Default;
        }

        try
        {
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json);
            if (file?.Kind != Marker || file.Events is not { Count: > 0 } events)
            {
                Problem = "Die Datei stammt nicht von diesem Programm oder ist leer – Standard-Zeitplan wird verwendet.";
                return GameEventSchedule.Default;
            }

            var valid = events
                .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Name) && e.Times is { Count: > 0 })
                .ToList();
            if (valid.Count != events.Count) Problem = "Einige Einträge waren unvollständig und wurden übersprungen.";
            return valid.Count == 0 ? GameEventSchedule.Default : new GameEventSchedule(file.TimeZone ?? GameEventSchedule.DefaultTimeZone, valid);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Problem = $"Datei nicht lesbar ({ex.Message}) – Standard-Zeitplan wird verwendet.";
            return GameEventSchedule.Default;
        }
    }

    private sealed record FileContent(string Kind, string? TimeZone, IReadOnlyList<GameEvent>? Events);
}

/// <summary>
/// Erinnerungen: meldet jedes Event-Vorkommen genau einmal, sobald es höchstens die eingestellte Zahl Minuten entfernt
/// ist. Nicht threadsicher (nur vom Oberflächen-Zeitgeber aufrufen).
/// </summary>
public sealed class EventAlarmClock
{
    private readonly HashSet<(string, DateTimeOffset)> _fired = new();

    /// <summary>Fällige Erinnerungen. <paramref name="minutesBefore"/>: Event-Kennung → Minuten vorher (fehlt = aus).</summary>
    public IReadOnlyList<(GameEvent Event, DateTimeOffset At)> Due(DateTimeOffset now, GameEventSchedule schedule, IReadOnlyDictionary<string, int> minutesBefore)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(minutesBefore);
        var zone = schedule.Zone;
        var due = new List<(GameEvent, DateTimeOffset)>();
        foreach (var e in schedule.Events)
        {
            if (!minutesBefore.TryGetValue(e.Id, out var before) || before < 0) continue;
            var at = e.NextAfter(now, zone);
            if (at - now <= TimeSpan.FromMinutes(before) && _fired.Add((e.Id, at))) due.Add((e, at));
        }

        _fired.RemoveWhere(f => f.Item2 < now.AddHours(-1)); // alte Einträge aufräumen
        return due;
    }
}
