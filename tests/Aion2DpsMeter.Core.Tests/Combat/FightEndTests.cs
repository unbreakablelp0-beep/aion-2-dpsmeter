using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Kampfende-Karte und Bestwert aus gespeicherten Kämpfen (simulierte Treffer).</summary>
public class FightEndTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static LiveCombatSnapshot Fight()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.ReportPlayer(2, "Heiler", isSelf: false);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 10_000, false, false));
        meter.Add(new CombatHit(T0, 2, 900, 17_010_000, 30_000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(8), 1, 900, 14_020_340, 10_000, false, false)); // unter der Leerlaufzeit
        return meter.Snapshot(T0.AddSeconds(60)); // beendet
    }

    [Fact]
    public void EndTextShowsDurationDpsSharePlaceAndBest()
    {
        var snapshot = Fight();
        Assert.Equal("0:08 · 2,50K DPS · 40,0 % · Platz 2 von 2", OverlayFormat.FightEndText(snapshot, null));
        Assert.Equal("0:08 · 2,50K DPS · 40,0 % · Platz 2 von 2 · Bestwert 3,13K (−20 %)", OverlayFormat.FightEndText(snapshot, 3125));
        Assert.Equal("0:08 · 2,50K DPS · 40,0 % · Platz 2 von 2 · neuer Bestwert!", OverlayFormat.FightEndText(snapshot, 2000));
    }

    [Fact]
    public void EndTextComparesOnlyAmongShownPlayers()
    {
        var snapshot = Fight();
        Assert.Equal("0:08 · 2,50K DPS · 100,0 % · Platz 1 von 1", OverlayFormat.FightEndText(snapshot, null, new HashSet<uint> { 1 }));
        Assert.Equal("0:08 · 2,50K DPS · 40,0 % · Platz 2 von 2", OverlayFormat.FightEndText(snapshot, null, new HashSet<uint> { 1, 2 }));
    }

    [Fact]
    public void PartyShareIsWithinTheParty()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.ReportPlayer(2, "Heiler", isSelf: false);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1_000, false, false));
        meter.Add(new CombatHit(T0, 2, 900, 17_010_000, 3_000, false, false));
        meter.Add(new CombatHit(T0, 7, 901, 16_100_004, 96_000, false, false)); // Fremder

        var rows = OverlayFormat.Build(meter.Snapshot(T0), 8, ownOnly: true, party: new HashSet<uint> { 2 }).Rows;
        Assert.Equal(["75,0 %", "25,0 %"], rows.Select(r => r.Share));
        Assert.Equal("1,0 %", OverlayFormat.Build(meter.Snapshot(T0), 8).Rows.Single(r => r.Actor == 1).Share); // „Alle“
    }

    [Fact]
    public void EndTextNeedsOwnCharacter()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 2, 900, 17_010_000, 30_000, false, false));
        Assert.Null(OverlayFormat.FightEndText(meter.Snapshot(T0), null));
    }

    [Fact]
    public void BestOwnDpsSkipsShortFightsAndTheCurrentOne()
    {
        FightRecord Record(DateTimeOffset start, double seconds, double ownDps) => new(Guid.NewGuid(), "K", start, start.AddSeconds(seconds), "Ich",
            [new FightPlayerRecord(1, "Ich", 14, (long)(ownDps * seconds), ownDps, true, []), new FightPlayerRecord(2, "X", 17, 1, 99_999, false, [])]);

        var fights = new[]
        {
            Record(T0, 30, 1200),
            Record(T0.AddMinutes(5), 5, 9000),    // zu kurz
            Record(T0.AddMinutes(10), 60, 1500),  // der gerade beendete
        };
        Assert.Equal(1200, FightStatistics.BestOwnDps(fights, T0.AddMinutes(10)));
        Assert.Equal(1500, FightStatistics.BestOwnDps(fights, null));
        Assert.Null(FightStatistics.BestOwnDps([], null));
    }

    /// <summary>Fremde schlagen weiter die Puppe: mein Kampf ist trotzdem beendet, meine DPS bleiben stehen.</summary>
    [Fact]
    public void StrangersDoNotKeepOwnFightRunning()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 5_000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(5), 1, 900, 14_020_340, 5_000, false, false));
        for (var s = 0; s <= 40; s += 5) meter.Add(new CombatHit(T0.AddSeconds(s), 7, 901, 16_100_004, 1_000, false, false)); // Fremder

        var snapshot = meter.Snapshot(T0.AddSeconds(40));
        Assert.True(snapshot.Active); // im ganzen Gebiet läuft noch etwas …
        var own = OverlayFormat.Build(snapshot, 8, ownOnly: true, idle: TimeSpan.FromSeconds(10));
        Assert.False(own.Active);     // … aber mein Kampf ist vorbei
        Assert.Equal("Kampf beendet", own.Title);
        Assert.Equal("0:05", own.Elapsed);
        Assert.Equal(2000, Assert.Single(own.Rows).DpsValue); // 10.000 in 5 s, eingefroren
        Assert.True(OverlayFormat.Build(snapshot, 8).Active); // Modus „Alle“: wie bisher
    }

    [Fact]
    public void NoFightWithoutOwnCharacterInOwnMode()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 7, 19_974, 16_100_004, 1_000, false, false)); // Fremder an der Puppe, ich unbekannt
        var content = OverlayFormat.Build(meter.Snapshot(T0), 8, ownOnly: true);
        Assert.Equal("Inaktiv", content.Title);
        Assert.False(content.Active);
        Assert.Equal(string.Empty, content.Elapsed);
        Assert.True(content.OwnUnknown);
    }

    [Fact]
    public void OnlyFightsWithOwnOrPartyHitsAreSaved()
    {
        FightRecord Fight(params FightPlayerRecord[] players) => new(Guid.NewGuid(), "K", T0, T0.AddSeconds(20), null, players);
        var stranger = new FightPlayerRecord(7, null, 16, 1000, 50, false, []);
        var mate = new FightPlayerRecord(2, "Heiler", 17, 1000, 50, false, []);
        var me = new FightPlayerRecord(1, "Ich", 14, 1000, 50, true, []);

        Assert.False(FightStatistics.IsRelevant(Fight(stranger), new HashSet<uint>()));
        Assert.True(FightStatistics.IsRelevant(Fight(stranger, me), new HashSet<uint>()));
        Assert.True(FightStatistics.IsRelevant(Fight(stranger, mate), new HashSet<uint> { 2 }));
    }

    [Fact]
    public void MainTargetCountsPartyWhenAsked()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1_000, false, false));
        meter.Add(new CombatHit(T0, 2, 901, 17_010_000, 9_000, false, false)); // Gruppenmitglied
        meter.Add(new CombatHit(T0, 7, 902, 16_100_004, 99_000, false, false)); // Fremder

        Assert.Equal(900u, meter.MainTarget(T0, TimeSpan.FromSeconds(10)));
        Assert.Equal(901u, meter.MainTarget(T0, TimeSpan.FromSeconds(10), new HashSet<uint> { 1, 2 }));
        Assert.Equal(902u, meter.MainTarget(T0, TimeSpan.FromSeconds(10), new HashSet<uint> { 1, 2, 7 }));
    }

    [Fact]
    public void DpsTimelineAndWeeklyStatistics()
    {
        var perSecond = DpsTimeline.PerSecond([(T0, 100L), (T0.AddSeconds(0.5), 50L), (T0.AddSeconds(3), 300L)], T0);
        Assert.Equal([150L, 0, 0, 300], perSecond);
        Assert.Equal([150, 75, 50, 112.5], DpsTimeline.Smooth(perSecond, 4));

        FightRecord Fight(DateTimeOffset start, double dps, string? boss) => new(Guid.NewGuid(), "K", start, start.AddSeconds(30), "Ich",
            [new FightPlayerRecord(1, "Ich", 14, (long)(dps * 30), dps, true, [])], null, boss);
        var now = new DateTimeOffset(2026, 10, 14, 18, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 14, 18, 0, 0)));
        var fights = new[]
        {
            Fight(now.AddHours(-2), 20_000, "Kromedes Gier"),
            Fight(now.AddHours(-1), 30_000, "Kromedes Gier"),
            Fight(now.AddDays(-1), 10_000, null),
            Fight(now.AddDays(-1).AddHours(1), 12_000, "Geisterfürst Agro"),
        };
        var days = FightStatistics.LastSevenDays(fights, now);
        Assert.Equal(7, days.Count);
        Assert.Equal(2, days[^1].Fights);
        Assert.Equal(2, days[^1].BossFights);
        Assert.Equal(25_000, days[^1].AverageOwnDps);
        Assert.Equal(1, days[^2].BossFights);
        Assert.Null(days[0].AverageOwnDps);

        var best = FightStatistics.BestPerBoss(fights);
        Assert.Equal(["Kromedes Gier", "Geisterfürst Agro"], best.Select(b => b.Boss));
        Assert.Equal(30_000, best[0].Dps);
    }

    [Fact]
    public void FightEndCardOffAndThemeDefault()
    {
        var settings = new OverlaySettings().Normalized();
        Assert.False(settings.ShowFightEndCard);
        Assert.Equal("Violett", settings.Theme);
        Assert.Equal("Violett", new OverlaySettings(Theme: "Pink").Normalized().Theme);
        Assert.Equal("Gold", new OverlaySettings(Theme: "Gold").Normalized().Theme);
    }

    [Fact]
    public void AutoHideOffByDefaultAndChoosable()
    {
        var settings = new OverlaySettings().Normalized();
        Assert.Equal(0, settings.AutoHideSeconds);
        Assert.False(settings.ShouldAutoHide(TimeSpan.FromHours(1))); // aus = nie ausblenden

        var thirty = new OverlaySettings(AutoHideSeconds: 30).Normalized();
        Assert.False(thirty.ShouldAutoHide(TimeSpan.FromSeconds(29)));
        Assert.True(thirty.ShouldAutoHide(TimeSpan.FromSeconds(31)));
        Assert.Equal(0, new OverlaySettings(AutoHideSeconds: 45).Normalized().AutoHideSeconds); // nicht wählbar → aus
    }

    [Fact]
    public void EffectsAreOnByDefault()
    {
        Assert.True(new OverlaySettings().Normalized().Effects);
    }
}
