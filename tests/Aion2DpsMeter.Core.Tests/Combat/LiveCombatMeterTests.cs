using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Live-Zähler mit simulierten Treffern.</summary>
public class LiveCombatMeterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CombatHit Hit(double s, uint actor, long amount, uint target = 900, bool crit = false, bool overTime = false) =>
        new(T0.AddSeconds(s), actor, target, 1, amount, crit, overTime);

    /// <summary>Kommt der Name (45 36) erst nach den Treffern, gilt er rückwirkend für den ganzen Kampf.</summary>
    [Fact]
    public void LateNameIsAppliedRetroactively()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 48819, 1000));
        Assert.Null(meter.Snapshot(T0.AddSeconds(1)).Actors.Single().Name);

        meter.ReportPlayer(48819, "Spät", isSelf: false);
        meter.Add(Hit(2, 48819, 500));
        var actor = meter.Snapshot(T0.AddSeconds(3)).Actors.Single();
        Assert.Equal("Spät", actor.Name);
        Assert.Equal(1500, actor.Damage);
    }

    [Fact]
    public void SumsPerActorWithShareAndDpsFromOwnFirstHit()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 1, 1000));
        meter.Add(Hit(2, 2, 500, crit: true));
        meter.Add(Hit(4, 1, 1000));
        meter.Add(Hit(10, 2, 1500, overTime: true));

        var s = meter.Snapshot(T0.AddSeconds(10));

        Assert.Equal(1, s.FightNumber);
        Assert.True(s.Active);
        Assert.Equal(4000, s.TotalDamage);
        Assert.Equal(400, s.Dps); // 4000 / 10 s
        var a1 = s.Actors.Single(a => a.Actor == 1);
        var a2 = s.Actors.Single(a => a.Actor == 2);
        Assert.Equal(2000, a1.Damage);
        Assert.Equal(200, a1.Dps); // ab 0 s bis zum letzten Treffer im Kampf (10 s)
        Assert.Equal(2000, a2.Damage);
        Assert.Equal(250, a2.Dps); // ab 2 s → 8 s
        Assert.Equal(0.5, a1.Share);
        Assert.Equal(1, a2.Hits); // Schaden über Zeit zählt nicht als Treffer
        Assert.Equal(1, a2.CriticalHits);
        Assert.Equal(1500, a2.OverTimeDamage);
    }

    [Fact]
    public void DpsFallsWhileFightRunsWithoutHitsThenFreezesAtLastHit()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.Add(Hit(0, 1, 1000));
        meter.Add(Hit(10, 1, 1000)); // 2000 in 10 s

        Assert.Equal(200, meter.Snapshot(T0.AddSeconds(10)).Dps);
        var idle = meter.Snapshot(T0.AddSeconds(20)); // 10 s nichts getroffen, Kampf läuft noch
        Assert.True(idle.Active);
        Assert.Equal(100, idle.Dps);
        Assert.Equal(100, idle.Actors[0].Dps);
        Assert.Equal(TimeSpan.FromSeconds(20), idle.Duration);

        var ended = meter.Snapshot(T0.AddSeconds(21)); // Kampf beendet → eingefroren auf letzten Treffer
        Assert.False(ended.Active);
        Assert.Equal(200, ended.Dps);
        Assert.Equal(TimeSpan.FromSeconds(10), ended.Duration);

        Assert.Equal(100, meter.Detail(1, now: T0.AddSeconds(20))!.Dps);
        Assert.Equal(200, meter.Detail(1)!.Dps);
    }

    [Fact]
    public void LagIsSubtractedFromNow()
    {
        var meter = new LiveCombatMeter { Lag = TimeSpan.FromSeconds(2) };
        meter.Add(Hit(0, 1, 1000));
        meter.Add(Hit(10, 1, 1000));

        // „jetzt“ = 12 s, Treffer kommen 2 s verzögert: gerechnet wird bis 10 s.
        Assert.Equal(200, meter.Snapshot(T0.AddSeconds(12)).Dps);
        Assert.True(meter.Snapshot(T0.AddSeconds(21)).Active); // 21 − 2 − 10 = 9 s ≤ 10 s
    }

    [Fact]
    public void SelfHitsAreIgnoredButCounted()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 7, 1064, target: 7));
        meter.Add(Hit(1, 1, 100));

        var s = meter.Snapshot(T0.AddSeconds(1));

        Assert.Equal(100, s.TotalDamage);
        Assert.Equal(1, s.IgnoredSelfHits);
        Assert.DoesNotContain(s.Actors, a => a.Actor == 7);
    }

    [Fact]
    public void SingleHitUsesAtLeastOneSecond()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 1, 500));

        var s = meter.Snapshot(T0);

        Assert.Equal(500, s.Dps);
        Assert.Equal(500, s.Actors[0].Dps);
    }

    [Fact]
    public void IdleStartsNewFightOnlyWithNextHit()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.Add(Hit(0, 1, 1000));
        meter.Add(Hit(5, 1, 1000));

        var ended = meter.Snapshot(T0.AddSeconds(20));
        Assert.False(ended.Active);
        Assert.Equal(2000, ended.TotalDamage); // bleibt sichtbar

        meter.Add(Hit(30, 2, 300));
        var next = meter.Snapshot(T0.AddSeconds(30));

        Assert.Equal(2, next.FightNumber);
        Assert.Equal(300, next.TotalDamage);
        Assert.Single(next.Actors);
    }

    [Fact]
    public void ResetClearsButKeepsOwnActor()
    {
        var meter = new LiveCombatMeter { OwnActor = 1 };
        meter.Add(Hit(0, 1, 1000));
        Assert.True(meter.Snapshot(T0).Actors[0].IsOwn);

        meter.Reset();
        var s = meter.Snapshot(T0);

        Assert.Equal(0, s.FightNumber);
        Assert.Null(s.Start);
        Assert.Empty(s.Actors);
        Assert.Equal(1u, meter.OwnActor);
    }

    [Fact]
    public void NamesAndDetectedOwnActorAppearAndManualChoiceWins()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(10398, "Held", isSelf: true);
        meter.ReportPlayer(8575, "Andere", isSelf: false);
        meter.Add(Hit(0, 10398, 1000));
        meter.Add(Hit(1, 8575, 500));
        meter.Add(Hit(1, 4343, 200));

        var s = meter.Snapshot(T0.AddSeconds(1));
        Assert.Equal(10398u, meter.DetectedOwnActor);
        Assert.False(meter.OwnActorIsManual);
        Assert.Equal("Held", s.Actors.Single(a => a.Actor == 10398).Name);
        Assert.True(s.Actors.Single(a => a.Actor == 10398).IsOwn);
        Assert.Null(s.Actors.Single(a => a.Actor == 4343).Name);

        meter.OwnActor = 8575;
        var manual = meter.Snapshot(T0.AddSeconds(1));
        Assert.True(manual.Actors.Single(a => a.Actor == 8575).IsOwn);
        Assert.False(manual.Actors.Single(a => a.Actor == 10398).IsOwn);

        meter.OwnActor = null; // zurück auf automatisch
        meter.Reset();
        Assert.Equal(10398u, meter.OwnActor);
        Assert.Equal("Held", meter.NameOf(10398));
    }

    [Fact]
    public void ClassFromMostCommonSkillPrefix()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 100, false, false));
        meter.Add(new CombatHit(T0, 1, 900, 14_050_000, 100, false, true)); // Schaden über Zeit, gleiche Klasse
        meter.Add(new CombatHit(T0, 1, 900, 30_000_171, 100, false, false)); // Gegenstand: zählt nicht
        meter.Add(new CombatHit(T0, 2, 900, 12_040_020, 100, false, false));
        meter.Add(new CombatHit(T0, 3, 900, 1_010_000, 100, false, false)); // keine Klassen-Fertigkeit

        var s = meter.Snapshot(T0);

        Assert.Equal("Bogenschütze", s.Actors.Single(a => a.Actor == 1).ClassName);
        Assert.Equal("Templer", s.Actors.Single(a => a.Actor == 2).ClassName);
        Assert.Null(s.Actors.Single(a => a.Actor == 3).ClassName);
    }

    [Theory]
    [InlineData(14_020_340u, 14)]
    [InlineData(10_000_000u, 10)]
    [InlineData(29_999_999u, 29)]
    [InlineData(30_000_171u, null)]
    [InlineData(1_010_000u, null)]
    public void SkillPrefix(uint skill, int? expected)
    {
        Assert.Equal(expected, SkillClassMap.Prefix(skill));
        Assert.True(SkillClassMap.IsKnown(14));
        Assert.True(SkillClassMap.IsKnown(12));
        Assert.Equal("Gladiator", SkillClassMap.Describe(18));
        Assert.False(SkillClassMap.IsKnown(19));
        Assert.Equal("Klasse 19", SkillClassMap.Describe(19));
    }

    [Fact]
    public void PetDamageCountsForOwnerUnlessClassDisagrees()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 13569, 900, 16_100_004, 1000, false, false)); // Beschwörer
        meter.ReportSummon(49865, 13569);
        meter.Add(new CombatHit(T0.AddSeconds(1), 49865, 900, 100_044, 300, false, false)); // Pet-Fertigkeit
        meter.Add(new CombatHit(T0.AddSeconds(2), 49865, 900, 16_130_004, 200, false, false)); // Klasse passt
        meter.ReportSummon(777, 13569);
        meter.Add(new CombatHit(T0.AddSeconds(3), 777, 900, 17_150_232, 50, false, false)); // Klasse 17 ≠ 16

        var s = meter.Snapshot(T0.AddSeconds(3));
        var owner = s.Actors.Single(a => a.Actor == 13569);

        Assert.Equal(1500, owner.Damage);
        Assert.Equal(500, owner.PetDamage);
        Assert.DoesNotContain(s.Actors, a => a.Actor == 49865);
        Assert.Contains(s.Actors, a => a.Actor == 777); // widersprechende Klasse: eigene Zeile
        Assert.Equal(1, meter.PetConflicts);
    }

    [Fact]
    public void PetHitsBeforeOwnerMessageAreMovedLater()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 13569, 900, 16_100_004, 1000, false, false));
        meter.Add(new CombatHit(T0, 49865, 900, 16_130_004, 400, true, false)); // Pet trifft vor der Besitzer-Nachricht
        meter.Add(new CombatHit(T0, 777, 900, 17_150_232, 50, false, false));

        meter.ReportSummon(49865, 13569);
        meter.ReportSummon(777, 13569); // Klasse 17 ≠ 16: bleibt eigene Zeile

        var s = meter.Snapshot(T0);
        var owner = s.Actors.Single(a => a.Actor == 13569);
        Assert.Equal(1400, owner.Damage);
        Assert.Equal(400, owner.PetDamage);
        Assert.Equal(2, owner.Hits);
        Assert.Equal(1, owner.CriticalHits);
        Assert.DoesNotContain(s.Actors, a => a.Actor == 49865);
        Assert.Contains(s.Actors, a => a.Actor == 777);
        Assert.Equal(1, meter.PetConflicts);
    }

    [Fact]
    public void RememberedOwnIsUsedUntilContradictedOrReplaced()
    {
        var meter = new LiveCombatMeter();
        meter.Remember(new RememberedOwnCharacter(10275, "Ich", T0));
        meter.Add(Hit(0, 10275, 100));

        var s = meter.Snapshot(T0);
        Assert.False(s.Actors[0].IsOwn); // gemerkt ist unsicher: nicht als „ich“ werten
        Assert.True(s.Actors[0].IsRememberedOwn);
        Assert.Null(meter.OwnActor);
        Assert.Equal("Ich", s.Actors[0].Name);

        meter.OwnActor = 10275; // von Hand bestätigt
        Assert.True(meter.Snapshot(T0).Actors[0].IsOwn);
        Assert.False(meter.Snapshot(T0).Actors[0].IsRememberedOwn);
        meter.OwnActor = null;

        meter.ReportPlayer(10275, "Jemand", isSelf: false); // Kennung gehört inzwischen jemand anderem
        Assert.Null(meter.RememberedOwn);
        Assert.Null(meter.OwnActor);

        meter.Remember(new RememberedOwnCharacter(10275, "Ich", T0));
        meter.ReportPlayer(11194, "Ich", isSelf: true); // frisch erkannt
        Assert.Null(meter.RememberedOwn);
        Assert.Equal(11194u, meter.OwnActor);
    }

    [Fact]
    public void OwnCharacterStoreKeepsOnlyOwnFilesAndRespectsAge()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aion2dps-own-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(dir, "eigener-charakter.json");
            var store = new OwnCharacterStore(path);
            Assert.Null(store.Load(T0));
            Assert.True(store.Save(new RememberedOwnCharacter(10275, "Ich", T0)));
            Assert.True(store.Save(new RememberedOwnCharacter(10276, "Ich", T0))); // eigene Datei: ersetzen erlaubt

            var loaded = new OwnCharacterStore(path).Load(T0.AddHours(1));
            Assert.Equal(10276u, loaded?.Id);
            Assert.Null(new OwnCharacterStore(path).Load(T0.AddHours(13))); // zu alt

            File.WriteAllText(path, "fremd");
            var foreign = new OwnCharacterStore(path);
            Assert.Null(foreign.Load(T0));
            Assert.False(foreign.Save(new RememberedOwnCharacter(1, "X", T0)));
            Assert.Equal("fremd", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ToleratesHitsFromSeveralThreads()
    {
        var meter = new LiveCombatMeter();
        Parallel.For(0, 1000, i => meter.Add(Hit(i % 5, (uint)(i % 3 + 1), 10)));

        Assert.Equal(10_000, meter.Snapshot(T0.AddSeconds(5)).TotalDamage);
    }
}
