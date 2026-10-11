using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>
/// Tests mit synthetischen (ausgedachten, nachrechenbaren) Daten – keine echten Spieldaten.
/// </summary>
public class CombatSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const long Target = 900;

    private static readonly Combatant Alice = new(1, "Alice", IsLocalPlayer: true);
    private static readonly Combatant Bob = new(2, "Bob");

    private static DamageEvent Hit(double atSeconds, long sourceId, long amount, bool critical = false,
        DataConfidence confidence = DataConfidence.Confirmed) =>
        new(T0.AddSeconds(atSeconds), sourceId, Target, amount, critical, confidence);

    /// <summary>
    /// Standardkampf, 10 Sekunden:
    /// Alice: 1000 (krit) bei 0 s, 500 bei 2 s, 1500 (krit) bei 10 s = 3000 Schaden, 3 Treffer, 2 krit.
    /// Bob:   2000 bei 5 s = 2000 Schaden, 1 Treffer.
    /// Gesamt 5000 / 10 s = 500 DPS; Alice 300 DPS (60 %), Bob 200 DPS (40 %).
    /// </summary>
    private static CombatSession CreateStandardFight()
    {
        var session = new CombatSession();
        session.RegisterCombatant(Alice);
        session.RegisterCombatant(Bob);
        session.AddDamage(Hit(0, 1, 1000, critical: true));
        session.AddDamage(Hit(2, 1, 500));
        session.AddDamage(Hit(5, 2, 2000));
        session.AddDamage(Hit(10, 1, 1500, critical: true));
        return session;
    }

    [Fact]
    public void EmptySession_HasNoDamageAndNoDps()
    {
        var stats = new CombatSession().GetStatistics();

        Assert.Equal(0, stats.TotalDamage);
        Assert.Null(stats.Dps);
        Assert.Equal(TimeSpan.Zero, stats.Duration);
        Assert.Null(stats.Start);
        Assert.Empty(stats.Combatants);
        Assert.False(stats.HasUncertainData);
    }

    [Fact]
    public void StandardFight_CalculatesTotalsDurationAndDps()
    {
        var stats = CreateStandardFight().GetStatistics();

        Assert.Equal(5000, stats.TotalDamage);
        Assert.Equal(TimeSpan.FromSeconds(10), stats.Duration);
        Assert.Equal(T0, stats.Start);
        Assert.Equal(T0.AddSeconds(10), stats.End);
        Assert.Equal(500, stats.Dps);
    }

    [Fact]
    public void StandardFight_CalculatesPerCombatantValuesSortedByDamage()
    {
        var stats = CreateStandardFight().GetStatistics();

        Assert.Equal(2, stats.Combatants.Count);

        var alice = stats.Combatants[0];
        Assert.Equal(Alice, alice.Combatant);
        Assert.Equal(3000, alice.TotalDamage);
        Assert.Equal(3, alice.Hits);
        Assert.Equal(2, alice.CriticalHits);
        Assert.Equal(300, alice.Dps);
        Assert.Equal(0.6, alice.DamageShare, precision: 10);
        Assert.Equal(2.0 / 3.0, alice.CriticalRate, precision: 10);

        var bob = stats.Combatants[1];
        Assert.Equal("Bob", bob.DisplayName);
        Assert.Equal(2000, bob.TotalDamage);
        Assert.Equal(1, bob.Hits);
        Assert.Equal(0, bob.CriticalHits);
        Assert.Equal(200, bob.Dps);
        Assert.Equal(0.4, bob.DamageShare, precision: 10);
    }

    [Fact]
    public void SingleHit_HasZeroDurationAndDpsIsNotCalculable()
    {
        var session = new CombatSession();
        session.AddDamage(Hit(0, 1, 1234));

        var stats = session.GetStatistics();

        Assert.Equal(1234, stats.TotalDamage);
        Assert.Equal(TimeSpan.Zero, stats.Duration);
        Assert.Null(stats.Dps);
        Assert.Null(stats.Combatants[0].Dps);
    }

    [Fact]
    public void EventsOutOfOrder_UseEarliestAndLatestTimestamp()
    {
        var session = new CombatSession();
        session.AddDamage(Hit(4, 1, 100));
        session.AddDamage(Hit(0, 1, 100));
        session.AddDamage(Hit(2, 1, 200));

        var stats = session.GetStatistics();

        Assert.Equal(TimeSpan.FromSeconds(4), stats.Duration);
        Assert.Equal(100, stats.Dps);
    }

    [Fact]
    public void UnconfirmedEvents_AreCountedButNotCalculated()
    {
        var session = CreateStandardFight();
        session.AddDamage(Hit(20, 1, 99999, confidence: DataConfidence.Unconfirmed));
        session.AddDamage(Hit(30, 2, 88888, critical: true, confidence: DataConfidence.Unconfirmed));

        var stats = session.GetStatistics();

        Assert.Equal(5000, stats.TotalDamage);
        Assert.Equal(TimeSpan.FromSeconds(10), stats.Duration);
        Assert.Equal(3, stats.Combatants[0].Hits);
        Assert.Equal(2, stats.UnconfirmedEventCount);
        Assert.True(stats.HasUncertainData);
    }

    [Fact]
    public void OnlyUnconfirmedEvents_GiveEmptyStatisticsMarkedAsUncertain()
    {
        var session = new CombatSession();
        session.AddDamage(Hit(0, 1, 500, confidence: DataConfidence.Unconfirmed));

        var stats = session.GetStatistics();

        Assert.Equal(0, stats.TotalDamage);
        Assert.Empty(stats.Combatants);
        Assert.True(stats.HasUncertainData);
    }

    [Fact]
    public void UnknownSource_GetsPlaceholderName()
    {
        var session = new CombatSession();
        session.AddDamage(Hit(0, 42, 100));

        var combatant = session.GetStatistics().Combatants[0];

        Assert.Null(combatant.Combatant);
        Assert.Equal("Unbekannt (42)", combatant.DisplayName);
    }

    [Fact]
    public void ZeroDamageHits_CountAsHitsWithZeroShare()
    {
        var session = new CombatSession();
        session.AddDamage(Hit(0, 1, 0));
        session.AddDamage(Hit(1, 1, 0));

        var stats = session.GetStatistics();

        Assert.Equal(0, stats.TotalDamage);
        Assert.Equal(0, stats.Dps);
        Assert.Equal(2, stats.Combatants[0].Hits);
        Assert.Equal(0, stats.Combatants[0].DamageShare);
    }

    [Fact]
    public void NegativeDamage_IsRejected()
    {
        var session = new CombatSession();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.AddDamage(Hit(0, 1, -1)));
        Assert.Equal(0, session.GetStatistics().TotalDamage);
    }

    [Fact]
    public void Reset_ClearsEventsButKeepsRegisteredCombatants()
    {
        var session = CreateStandardFight();
        session.AddDamage(Hit(11, 1, 1, confidence: DataConfidence.Unconfirmed));

        session.Reset();
        Assert.Equal(0, session.GetStatistics().TotalDamage);
        Assert.Equal(0, session.GetStatistics().UnconfirmedEventCount);

        session.AddDamage(Hit(0, 2, 10));
        Assert.Equal("Bob", session.GetStatistics().Combatants[0].DisplayName);
    }

    [Fact]
    public void RegisterCombatant_AgainWithSameId_UpdatesName()
    {
        var session = new CombatSession();
        session.RegisterCombatant(new Combatant(1, "Alt"));
        session.RegisterCombatant(new Combatant(1, "Neu"));
        session.AddDamage(Hit(0, 1, 1));

        Assert.Equal("Neu", session.GetStatistics().Combatants[0].DisplayName);
    }
}
