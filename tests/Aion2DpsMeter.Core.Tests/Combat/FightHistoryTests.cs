using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Kampfverlauf: Export aus dem Zähler, Speicher, Details aus gespeicherten Kämpfen (simulierte Treffer).</summary>
public class FightHistoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-fights-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static CombatHit Hit(double s, uint actor, long amount, uint skill = 14_020_340, uint target = 900, bool crit = false) =>
        new(T0.AddSeconds(s), actor, target, skill, amount, crit, false);

    [Fact]
    public void FinishedFightIsExportedExactlyOnce()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.Add(Hit(0, 1, 1000, crit: true));
        meter.Add(Hit(4, 1, 3000));
        meter.Add(Hit(5, 2, 500, skill: 16_100_004));

        Assert.Empty(meter.DrainFinishedFights(T0.AddSeconds(8))); // läuft noch
        var fights = meter.DrainFinishedFights(T0.AddSeconds(20));
        Assert.Empty(meter.DrainFinishedFights(T0.AddSeconds(30))); // nicht doppelt

        var fight = Assert.Single(fights);
        Assert.Equal("Ich", fight.OwnName);
        Assert.Equal(4500, fight.TotalDamage);
        Assert.Equal(TimeSpan.FromSeconds(5), fight.Duration);
        Assert.Equal(2, fight.Players.Count);
        Assert.True(fight.Players[0].IsOwn);
        Assert.Equal("Bogenschütze", fight.Players[0].ClassName);
        Assert.StartsWith("Kampf ", fight.Name);
    }

    [Fact]
    public void FightReplacedByNextOneIsKeptAndResetFightIsNot()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.Add(Hit(0, 1, 1000));
        meter.Add(Hit(30, 1, 2000)); // neuer Kampf, bevor jemand abgeholt hat

        var first = Assert.Single(meter.DrainFinishedFights(T0.AddSeconds(31)));
        Assert.Equal(1000, first.TotalDamage);

        meter.Reset();
        Assert.Null(meter.ExportCurrentFight());
        meter.Add(Hit(40, 1, 700));
        Assert.Equal(700, meter.ExportCurrentFight()!.TotalDamage); // beim Beenden auch laufende Kämpfe
        Assert.Null(meter.ExportCurrentFight());
    }

    [Fact]
    public void StoreSavesWithoutOverwritingRenamesAndDeletesOnlyOwnFiles()
    {
        var store = new FightHistoryStore(_directory);
        var fight = SampleFight();

        var a = store.Save(fight);
        var b = store.Save(fight); // gleiche Startzeit: neuer Name statt Überschreiben
        Assert.NotEqual(a, b);
        Assert.EndsWith("-2.json", b);

        File.WriteAllText(Path.Combine(_directory, "fremd.json"), "{\"Kind\":\"anders\"}");
        File.WriteAllText(Path.Combine(_directory, "kaputt.json"), "kein json");
        Assert.Equal(2, store.List().Count);

        Assert.True(store.Rename(a, "Boss Versuch 3"));
        Assert.Contains(store.List(), e => e.Fight.Name == "Boss Versuch 3");

        Assert.False(store.Delete(Path.Combine(_directory, "fremd.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "fremd.json")));
        Assert.False(store.Rename(Path.Combine(_directory, "kaputt.json"), "x"));
        Assert.Equal("kein json", File.ReadAllText(Path.Combine(_directory, "kaputt.json")));

        Assert.True(store.Delete(b));
        Assert.Single(store.List());
    }

    [Fact]
    public void SavedFightGivesDetailsAndSearch()
    {
        var store = new FightHistoryStore(_directory);
        store.Save(SampleFight());
        var fight = store.List()[0].Fight;

        var detail = fight.Detail(1)!;
        Assert.Equal(4300, detail.Damage);
        Assert.Equal(2, detail.Hits);
        Assert.Equal(1, detail.CriticalHits);
        Assert.Equal(new uint[] { 900, 901 }, detail.Targets.Select(t => t.Target));
        Assert.Equal(300, fight.Detail(1, target: 901)!.Damage);
        Assert.Equal(4000, detail.Skills[0].Damage);
        Assert.Contains(detail.Skills, s => s.Kind == HitKind.OverTime);

        Assert.True(fight.Matches("ich"));
        Assert.True(fight.Matches("kampf"));
        Assert.False(fight.Matches("niemand"));
        Assert.Null(fight.Detail(999));
    }

    private static FightRecord SampleFight()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.Add(Hit(0, 1, 1000, crit: true));
        meter.Add(Hit(2, 1, 3000));
        meter.Add(new CombatHit(T0.AddSeconds(3), 1, 901, 14_050_000, 300, false, true));
        return meter.ExportCurrentFight()!;
    }
}
