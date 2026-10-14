using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aion2DpsMeter.Core.Events;

/// <summary>Ein Weltboss mit selbst eingetragener Respawn-Zeit (Zeiten sind je Server verschieden).</summary>
/// <param name="Id">Kennung (für Erinnerungen).</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="RespawnAt">Wann er (wieder) erscheint, oder null (nicht eingetragen).</param>
/// <param name="Remind">Erinnern an/aus.</param>
/// <param name="LeadMinutes">Vorlauf der Erinnerung in Minuten.</param>
/// <param name="Interval">Respawn-Abstand nach einem Kill (z. B. Gartua 12 h) oder null (unbekannt).</param>
/// <param name="FieldMap">Karte der Feldboss-Liste (01 91), in der der Boss steht, oder null.</param>
/// <param name="FieldPlace">Platz in dieser Liste (1 …), oder null.</param>
/// <param name="FromGame">Zeit stammt aus der Feldboss-Liste des Spiels (statt selbst eingetragen).</param>
public sealed record WorldBoss(string Id, string Name, DateTimeOffset? RespawnAt, bool Remind = true, int LeadMinutes = 10, TimeSpan? Interval = null,
    int? FieldMap = null, int? FieldPlace = null, bool FromGame = false)
{
    /// <summary>Nächster Spawn nach einem Kill zur Zeit <paramref name="killedAt"/>; null ohne bekannten Abstand.</summary>
    public DateTimeOffset? SpawnAfterKill(DateTimeOffset killedAt) => Interval is { } i ? killedAt + i : null;
}

/// <summary>Hilfen für Weltboss-Zeiten.</summary>
public static partial class WorldBossTimes
{
    /// <summary>Standardliste: Gartua als erster Eintrag (vom Besitzer genannt), weitere selbst hinzufügen.</summary>
    /// <remarks>
    /// Gartua = Karte 1110, Platz 21: In der Community-Gegnerliste ist „Unsterblicher Gartua“ Nummer 2400800, also Block
    /// 2400 der Karte 1110. Unter den 24 Bossen dieses Blocks (ohne Trainingspuppen) steht er an 21. Stelle, und die Liste
    /// hat 24 Plätze. Platz 21 meldete in der Aufnahme 093400 „zurück 10:21:44“; plus 12 h passt das zur Restzeit im
    /// Screenshot des Besitzers (~22:23). Abgleich mit Screenshot und Aufnahme gleichzeitig steht noch aus.
    /// </remarks>
    public static IReadOnlyList<WorldBoss> Default { get; } =
        [new("gartua", "Unsterblicher Gartua", null, Interval: TimeSpan.FromHours(12), FieldMap: 1110, FieldPlace: 21)];

    /// <summary>
    /// Zeiten aus der Feldboss-Liste des Spiels übernehmen: tot → Rückkehrzeit, lebt → „seit“ (dann ist er da). Liefert
    /// die geänderte Liste oder null, wenn kein Boss betroffen ist bzw. sich nichts geändert hat.
    /// </summary>
    public static IReadOnlyList<WorldBoss>? Apply(IReadOnlyList<WorldBoss> bosses, Analysis.FieldBossList list)
    {
        ArgumentNullException.ThrowIfNull(bosses);
        ArgumentNullException.ThrowIfNull(list);
        var changed = false;
        var result = bosses.Select(b =>
        {
            if (b.FieldMap != (int)list.Map || b.FieldPlace is not { } place) return b;
            if (list.Slots.FirstOrDefault(s => s.Place == place) is not { Time: { } time }) return b;
            if (b.RespawnAt == time && b.FromGame) return b;
            changed = true;
            return b with { RespawnAt = time, FromGame = true };
        }).ToList();
        return changed ? result : null;
    }

    /// <summary>Feste Angaben (Karte/Platz/Abstand) bekannter Bosse in ältere gespeicherte Einträge übernehmen.</summary>
    public static WorldBoss WithKnownDefaults(WorldBoss boss)
    {
        var known = Default.FirstOrDefault(d => d.Id == boss.Id);
        return known is null ? boss : boss with
        {
            FieldMap = boss.FieldMap ?? known.FieldMap,
            FieldPlace = boss.FieldPlace ?? known.FieldPlace,
            Interval = boss.Interval ?? known.Interval,
        };
    }

    /// <summary>
    /// Uhrzeit eines Kills lesen („17:42“ oder „17:42:10“, Ortszeit): heute, oder gestern, falls die Zeit noch in der
    /// Zukunft läge. Null, wenn nichts passt.
    /// </summary>
    public static DateTimeOffset? ParseKillTime(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = ClockPattern().Match(text.Trim());
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var s = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (h > 23 || min > 59 || s > 59) return null;
        var local = now.ToLocalTime();
        var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, h, min, s, local.Offset);
        return candidate > now ? candidate.AddDays(-1) : candidate;
    }

    /// <summary>
    /// Restzeit aus dem Spiel lesen: „4:16:50“, „16:50“, „4 Std. 16 Min. 50 Sek.“, „4h 16m“ … Null, wenn nichts passt.
    /// </summary>
    public static TimeSpan? ParseCountdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();

        var clock = ClockPattern().Match(t);
        if (clock.Success)
        {
            var a = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
            if (clock.Groups[3].Success)
            {
                var c = int.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture);
                return b < 60 && c < 60 ? new TimeSpan(a, b, c) : null;
            }

            return b < 60 ? new TimeSpan(0, a, b) : null; // „mm:ss“
        }

        int hours = Unit(t, HoursPattern()), minutes = Unit(t, MinutesPattern()), seconds = Unit(t, SecondsPattern());
        int days = Unit(t, DaysPattern());
        if (days + hours + minutes + seconds == 0) return null;
        return new TimeSpan(days, hours, minutes, seconds);
    }

    /// <summary>Nächste fällige Erinnerung je Boss genau einmal (Vorlauf erreicht, Termin noch nicht vorbei).</summary>
    public static IEnumerable<WorldBoss> Due(IEnumerable<WorldBoss> bosses, DateTimeOffset now, ISet<(string, DateTimeOffset)> fired)
    {
        ArgumentNullException.ThrowIfNull(bosses);
        ArgumentNullException.ThrowIfNull(fired);
        foreach (var b in bosses)
        {
            if (!b.Remind || b.RespawnAt is not { } at || at < now) continue;
            if (at - now <= TimeSpan.FromMinutes(b.LeadMinutes) && fired.Add((b.Id, at))) yield return b;
        }
    }

    private static int Unit(string text, Regex pattern)
    {
        var m = pattern.Match(text);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    [GeneratedRegex(@"^(\d{1,3}):(\d{1,2})(?::(\d{1,2}))?$")]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"(\d+)\s*(?:T\b|Tag|Tage|d\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DaysPattern();

    [GeneratedRegex(@"(\d+)\s*(?:Std|Stunde|Stunden|h\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HoursPattern();

    [GeneratedRegex(@"(\d+)\s*(?:Min|Minute|Minuten|m\b)", RegexOptions.IgnoreCase)]
    private static partial Regex MinutesPattern();

    [GeneratedRegex(@"(\d+)\s*(?:Sek|Sekunde|Sekunden|s\b)", RegexOptions.IgnoreCase)]
    private static partial Regex SecondsPattern();
}

/// <summary>
/// Speichert die Weltboss-Liste lokal (Dokumente\Aion2DpsMeter\weltbosse.json, Kennung im Feld „Kind“). Eine
/// vorhandene fremde oder kaputte Datei wird nie überschrieben; dann gilt die Standardliste nur im Speicher.
/// </summary>
public sealed class WorldBossStore(string path)
{
    public const string Marker = "Aion2DpsMeter-Weltbosse";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private bool _foreign;

    public string Path { get; } = path;

    /// <summary>Hinweis, falls die Datei nicht verwendet werden konnte.</summary>
    public string? Problem { get; private set; }

    public IReadOnlyList<WorldBoss> Load()
    {
        Problem = null;
        _foreign = false;
        if (!File.Exists(Path)) return WorldBossTimes.Default;
        try
        {
            var file = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json);
            if (file?.Kind != Marker || file.Bosses is null)
            {
                _foreign = true;
                Problem = "Die Datei stammt nicht von diesem Programm – sie wird nicht verändert.";
                return WorldBossTimes.Default;
            }

            return file.Bosses.Where(b => !string.IsNullOrWhiteSpace(b.Id) && !string.IsNullOrWhiteSpace(b.Name))
                .Select(b => WorldBossTimes.WithKnownDefaults(b with { LeadMinutes = Math.Clamp(b.LeadMinutes, 1, 60) }))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _foreign = true;
            Problem = $"Datei nicht lesbar ({ex.Message}) – sie wird nicht verändert.";
            return WorldBossTimes.Default;
        }
    }

    /// <summary>Speichert; false, wenn die vorhandene Datei fremd ist (dann wird nichts geschrieben).</summary>
    public bool Save(IReadOnlyList<WorldBoss> bosses)
    {
        ArgumentNullException.ThrowIfNull(bosses);
        if (_foreign) return false;
        if (File.Exists(Path))
        {
            try
            {
                if (JsonSerializer.Deserialize<FileContent>(File.ReadAllText(Path), Json)?.Kind != Marker) return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(new FileContent(Marker, bosses), Json));
        return true;
    }

    private sealed record FileContent(string Kind, IReadOnlyList<WorldBoss>? Bosses);
}
