using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>DPS frieren beim Boss-Tod ein; Berserk-Prognose (simulierte Zeiten).</summary>
public class BossKillTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DpsFreezeWhenTheBossDies()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10)) { Lag = TimeSpan.Zero };
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 10_000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(10), 1, 900, 14_020_340, 10_000, false, false));
        meter.EndFight(T0.AddSeconds(10));

        // 5 s später: Kampf ist sofort beendet, die DPS bleiben bei 20.000 / 10 s statt weiter zu sinken.
        var later = meter.Snapshot(T0.AddSeconds(15));
        Assert.False(later.Active);
        Assert.Equal(2_000, later.Actors.Single().Dps);
        Assert.Equal(TimeSpan.FromSeconds(10), later.Duration);

        // Der Kampf ist sofort für den Verlauf fertig, mit Ende beim Tod.
        var record = Assert.Single(meter.DrainFinishedFights(T0.AddSeconds(11)));
        Assert.Equal(T0.AddSeconds(10), record.End);

        // Der nächste Treffer beginnt einen neuen Kampf.
        meter.Add(new CombatHit(T0.AddSeconds(20), 1, 901, 14_020_340, 5_000, false, false));
        var next = meter.Snapshot(T0.AddSeconds(20));
        Assert.True(next.Active);
        Assert.Equal(5_000, next.TotalDamage);
    }

    [Fact]
    public void BerserkForecastGoodTightAndTooSlow()
    {
        BossStatus Boss(double ttkSeconds, double deadlineInSeconds) => new(1, 1_000_000, 2_000_000, TimeSpan.FromSeconds(ttkSeconds),
            FightStart: T0, Deadline: T0.AddSeconds(30 + deadlineInSeconds));
        var now = T0.AddSeconds(30);
        var min = TimeSpan.FromSeconds(15);

        Assert.Equal(0, BossRules.BerserkForecast(Boss(60, 210), now, min)!.Value.Level);  // 2:30 Puffer
        Assert.Contains("2:30 Puffer", BossRules.BerserkForecast(Boss(60, 210), now, min)!.Value.Text);
        Assert.Equal(1, BossRules.BerserkForecast(Boss(60, 80), now, min)!.Value.Level);   // 0:20 Puffer
        var bad = BossRules.BerserkForecast(Boss(120, 80), now, min)!.Value;               // 40 s zu langsam
        Assert.Equal(2, bad.Level);
        Assert.Contains("0:40 zu langsam", bad.Text);
        Assert.Contains("50 % mehr", bad.Text);

        Assert.Null(BossRules.BerserkForecast(Boss(60, 210), T0.AddSeconds(10), min));   // erst nach 15 s Kampf
        Assert.Null(BossRules.BerserkForecast(Boss(60, 210) with { Deadline = null }, now, min)); // kein Zeitlimit
    }
}
