using Aion2DpsMeter.Core.Combat;
using Aion2DpsMeter.Core.Events;

namespace Aion2DpsMeter.Core.Tests.Events;

/// <summary>Event-Zeitplan, Sommerzeit, Erinnerungen und lokale Datei (simulierte Uhrzeiten).</summary>
public class GameEventsTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = GameEventSchedule.Default.Zone;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-events-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static GameEvent Event(string id) => GameEventSchedule.Default.Events.Single(e => e.Id == id);

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void ZoneIsBerlin()
    {
        // Sommer: +2, Winter: +1
        Assert.Equal(TimeSpan.FromHours(2), Berlin.GetUtcOffset(Utc(7, 1, 12)));
        Assert.Equal(TimeSpan.FromHours(1), Berlin.GetUtcOffset(Utc(12, 1, 12)));
    }

    [Fact]
    public void HourlyEventAtHalfPast()
    {
        var next = Event("invasion").NextAfter(Utc(10, 13, 8, 18), Berlin); // 10:18 MESZ
        Assert.Equal(Utc(10, 13, 8, 30), next);
    }

    [Fact]
    public void StartTimeItselfIsNotNext()
    {
        var next = Event("shugo").NextAfter(Utc(10, 13, 8), Berlin);
        Assert.Equal(Utc(10, 13, 9), next);
    }

    [Fact]
    public void RiftEveryThreeHoursFromTwo()
    {
        Assert.Equal([2, 5, 8, 11, 14, 17, 20, 23], GameEvent.Every(3, 2).Select(t => t.Hour));
        var next = Event("rift").NextAfter(Utc(10, 13, 9), Berlin); // 11:00 MESZ → 14:00 MESZ
        Assert.Equal(Utc(10, 13, 12), next);
    }

    [Fact]
    public void WeeklyResetOnWednesdayInWinterTime()
    {
        // Do 12.11.2026 → Mi 18.11.2026 09:00 MEZ = 08:00 UTC
        var next = Event("weekly").NextAfter(Utc(11, 12, 10), Berlin);
        Assert.Equal(Utc(11, 18, 8), next);
        Assert.Equal(DayOfWeek.Wednesday, TimeZoneInfo.ConvertTime(next, Berlin).DayOfWeek);
    }

    [Fact]
    public void SiegeAcrossWeekend()
    {
        // Sa 17.10. 22:00 MESZ → Mo 19.10. 21:00 MESZ
        var next = Event("siege").NextAfter(Utc(10, 17, 20), Berlin);
        Assert.Equal(Utc(10, 19, 19), next);
    }

    [Fact]
    public void DailyResetKeepsLocalTimeOverDstEnd()
    {
        // 25.10.2026: Umstellung auf Winterzeit. Vorher 09:00 = 07:00 UTC, danach 08:00 UTC.
        Assert.Equal(Utc(10, 24, 7), Event("daily").NextAfter(Utc(10, 24, 0), Berlin));
        Assert.Equal(Utc(10, 25, 8), Event("daily").NextAfter(Utc(10, 24, 12), Berlin));
    }

    [Fact]
    public void AmbiguousTimeCountsOnce()
    {
        // 25.10.2026 02:00 gibt es zweimal; gezählt wird die erste (MESZ, 00:00 UTC), danach 05:00 MEZ.
        var rift = Event("rift");
        var first = rift.NextAfter(Utc(10, 24, 22), Berlin);
        Assert.Equal(Utc(10, 25, 0), first);
        Assert.Equal(Utc(10, 25, 4), rift.NextAfter(first, Berlin));
    }

    [Fact]
    public void InvalidTimeIsSkippedAtDstStart()
    {
        // 29.03.2026: 02:00 gibt es nicht → nächster Riss 05:00 MESZ = 03:00 UTC.
        var next = Event("rift").NextAfter(Utc(3, 28, 23), Berlin);
        Assert.Equal(Utc(3, 29, 3), next);
    }

    [Fact]
    public void ScheduleNextPicksEarliest()
    {
        var (e, at) = GameEventSchedule.Default.Next(Utc(10, 13, 8, 18));
        Assert.Equal("invasion", e.Id);
        Assert.Equal(Utc(10, 13, 8, 30), at);
    }

    [Fact]
    public void CountdownFormats()
    {
        Assert.Equal("45m 36s", GameEventSchedule.Countdown(new TimeSpan(0, 45, 36)));
        Assert.Equal("2h 05m", GameEventSchedule.Countdown(new TimeSpan(2, 5, 0)));
        Assert.Equal("2d 10h 45m", GameEventSchedule.Countdown(new TimeSpan(2, 10, 45, 0)));
        Assert.Equal("0m 00s", GameEventSchedule.Countdown(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void AlarmFiresOnceWithinLeadTime()
    {
        var clock = new EventAlarmClock();
        var alarms = new Dictionary<string, int> { ["invasion"] = 5 };
        var schedule = GameEventSchedule.Default;

        Assert.Empty(clock.Due(Utc(10, 13, 8, 20), schedule, alarms)); // 10 Min. vorher: noch nicht
        var due = clock.Due(Utc(10, 13, 8, 25), schedule, alarms);
        Assert.Equal("invasion", Assert.Single(due).Event.Id);
        Assert.Empty(clock.Due(Utc(10, 13, 8, 27), schedule, alarms)); // nicht doppelt
        Assert.Single(clock.Due(Utc(10, 13, 9, 26), schedule, alarms)); // nächste Stunde wieder
    }

    [Fact]
    public void DisabledEventsNeverFire()
    {
        var clock = new EventAlarmClock();
        Assert.Empty(clock.Due(Utc(10, 13, 8, 29), GameEventSchedule.Default, new Dictionary<string, int>()));
    }

    [Fact]
    public void StoreCreatesTemplateOnlyWhenMissing()
    {
        var path = Path.Combine(_directory, "events.json");
        var store = new GameEventScheduleStore(path);

        var schedule = store.Load();
        Assert.True(File.Exists(path));
        Assert.Null(store.Problem);
        Assert.Equal(GameEventSchedule.Default.Events.Count, schedule.Events.Count);

        // Selbst geändert: Datei wird gelesen und nicht überschrieben.
        var text = File.ReadAllText(path).Replace("Shugo-Fest", "Mein Fest");
        File.WriteAllText(path, text);
        var reloaded = store.Load();
        Assert.Contains(reloaded.Events, e => e.Name == "Mein Fest");
        Assert.Equal(text, File.ReadAllText(path));
        Assert.Equal(Utc(10, 13, 9), reloaded.Events.Single(e => e.Id == "shugo").NextAfter(Utc(10, 13, 8), reloaded.Zone));
    }

    [Fact]
    public void ForeignFileIsKeptAndDefaultUsed()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "events.json");
        File.WriteAllText(path, "{\"Kind\":\"anderes\"}");
        var store = new GameEventScheduleStore(path);

        var schedule = store.Load();
        Assert.Same(GameEventSchedule.Default, schedule);
        Assert.NotNull(store.Problem);
        Assert.Equal("{\"Kind\":\"anderes\"}", File.ReadAllText(path));
    }

    [Fact]
    public void BrokenFileIsKeptAndDefaultUsed()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "events.json");
        File.WriteAllText(path, "kein json");
        var store = new GameEventScheduleStore(path);

        Assert.Same(GameEventSchedule.Default, store.Load());
        Assert.NotNull(store.Problem);
        Assert.Equal("kein json", File.ReadAllText(path));
    }

    [Fact]
    public void AlarmSettingsAreNormalized()
    {
        var settings = new OverlaySettings(
            AlarmVolume: 3,
            AlarmTone: "Trompete",
            EventAlarms: new Dictionary<string, int> { ["rift"] = 5, ["shugo"] = 7 }).Normalized();

        Assert.Equal(1, settings.AlarmVolume);
        Assert.Equal("Glocke", settings.AlarmTone);
        Assert.Equal(5, Assert.Single(settings.EventAlarms!).Value);
        Assert.Equal(0.6, new OverlaySettings(AlarmVolume: double.NaN).Normalized().AlarmVolume);
    }

    [Fact]
    public void NextEventLineOnlyShortlyBefore()
    {
        var settings = new OverlaySettings().Normalized();
        Assert.Equal(10, settings.NextEventMinutes);
        Assert.False(settings.ShowNextEventNow(new TimeSpan(0, 22, 6)));
        Assert.True(settings.ShowNextEventNow(new TimeSpan(0, 9, 59)));
        Assert.True((settings with { NextEventMinutes = 0 }).ShowNextEventNow(TimeSpan.FromHours(3))); // immer
        Assert.Equal(10, new OverlaySettings(NextEventMinutes: 7).Normalized().NextEventMinutes);  // nicht wählbar → Standard
        Assert.Equal(30, new OverlaySettings(NextEventMinutes: 30).Normalized().NextEventMinutes);
    }

    [Fact]
    public void AlarmSettingsRoundTrip()
    {
        var path = Path.Combine(_directory, "overlay.json");
        var store = new OverlaySettingsStore(path);
        store.Load();
        Assert.True(store.Save(new OverlaySettings(EventAlarms: new Dictionary<string, int> { ["siege"] = 15 }, AlarmTone: "Gong", ShowNextEvent: false)));

        var loaded = new OverlaySettingsStore(path).Load();
        Assert.Equal(15, loaded.EventAlarms!["siege"]);
        Assert.Equal("Gong", loaded.AlarmTone);
        Assert.False(loaded.ShowNextEvent);
    }

    [Fact]
    public void ClickThroughIsNeverRestoredAfterStart()
    {
        var path = Path.Combine(_directory, "overlay.json");
        var store = new OverlaySettingsStore(path);
        store.Load();
        Assert.True(store.Save(new OverlaySettings(ClickThrough: true, FontSize: 15)));
        Assert.DoesNotContain("\"ClickThrough\": true", File.ReadAllText(path));

        // Auch eine von Hand auf true gesetzte Datei startet mit „aus“.
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"ClickThrough\": false", "\"ClickThrough\": true"));
        var loaded = new OverlaySettingsStore(path).Load();
        Assert.False(loaded.ClickThrough);
        Assert.Equal(15, loaded.FontSize);
    }
}
