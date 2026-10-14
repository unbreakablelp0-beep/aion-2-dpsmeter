using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Gegner-HP (00 8D, 41 36), Boss-Leiste, Hauptziel (selbst gebaute Bytefolgen und simulierte Treffer).</summary>
public class EnemyHpTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static IEnumerable<byte> Varint(ulong value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            yield return value == 0 ? b : (byte)(b | 0x80);
        }
        while (value != 0);
    }

    private static byte[] Remaining(ulong target, ulong kind, long hp) =>
        Varint(target).Concat(Varint(kind)).Concat(Varint(0)).Concat(Varint(5)).Concat(BitConverter.GetBytes(hp)).ToArray();

    [Fact]
    public void ParsesRemainingHpOfEnemiesOnly()
    {
        var hp = EnemyHpHypothesis.ParseRemaining(Remaining(19_072, 2, 40_314));
        Assert.Equal(new EnemyHpCandidate(19_072, 40_314, null), hp);
        Assert.Null(EnemyHpHypothesis.ParseRemaining(Remaining(10_275, 1, 500))); // Art 1 = Spieler
        Assert.Null(EnemyHpHypothesis.ParseRemaining(Remaining(19_072, 2, -1)));
        Assert.Null(EnemyHpHypothesis.ParseRemaining(Remaining(19_072, 2, 40_314).AsSpan(0, 10)));
    }

    [Fact]
    public void ParsesFirstCurrentMaxPairOfSpawn()
    {
        // Kennung, etwas davor, dann 01 + aktuell + max (wie an den Aufnahmen beobachtet: 40314/119700).
        var body = Varint(19_072).Concat(new byte[] { 0x07, 0x00, 0x22 }).Concat(new byte[] { 0x01 })
            .Concat(Varint(40_314)).Concat(Varint(119_700)).Concat(new byte[] { 0x01, 0x02, 0x03 }).ToArray();
        Assert.Equal(new EnemyHpCandidate(19_072, 40_314, 119_700), EnemyHpHypothesis.ParseSpawn(body));
        Assert.Null(EnemyHpHypothesis.ParseSpawn(Varint(19_072).Concat(new byte[] { 0x05, 0x06 }).ToArray()));
    }

    [Fact]
    public void TrackerGivesPercentAndTimeToKill()
    {
        var tracker = new EnemyHpTracker();
        tracker.Report(7, 100_000, 100_000, T0);
        tracker.Report(7, 80_000, null, T0.AddSeconds(4)); // 5.000 HP/s

        var boss = tracker.Status(7, T0.AddSeconds(4))!;
        Assert.Equal(0.8, boss.Fraction);
        Assert.Equal(TimeSpan.FromSeconds(16), boss.TimeToKill);
        Assert.Equal("80,0 % · 80,0K / 100,0K · Kill ~0:16", OverlayFormat.BossText(boss));

        Assert.Null(tracker.Status(7, T0.AddSeconds(40))); // veraltet
        Assert.Null(tracker.Status(8, T0));
    }

    [Fact]
    public void BossHeaderTextsLikeOtherMeter()
    {
        var boss = new BossStatus(16_659, 49_528_026, 90_051_000, TimeSpan.FromSeconds(46));
        Assert.Equal("49.528.026 HP", OverlayFormat.BossHpText(boss));
        Assert.Equal("Kampf läuft · Kill ~0:46", OverlayFormat.BossStatusText(boss));
        Assert.Equal("Besiegt", OverlayFormat.BossStatusText(boss with { Hp = 0 }));
        Assert.Equal("Kampf läuft · Max-HP unbekannt", OverlayFormat.BossStatusText(boss with { MaxHp = null, TimeToKill = null }));
    }

    /// <summary>Wie in der Feuertempel-Aufnahme: Kampfbeginn von #16659 mit Deadline +5:00.</summary>
    [Fact]
    public void BerserkDeadlineFromBattleState()
    {
        var deadline = T0.AddMinutes(5);
        var body = Varint(16_659).Concat(Varint(0)).Concat(Varint(1)).Concat(BitConverter.GetBytes(deadline.ToUnixTimeMilliseconds())).ToArray();
        var state = BattleStateHypothesis.Parse(body, T0)!;
        Assert.True(state.Engaged);
        Assert.Equal(16_659u, state.Target);
        Assert.Equal(deadline, state.Deadline);

        Assert.Null(BattleStateHypothesis.Parse(body, T0.AddHours(2))!.Deadline); // Deadline schon vorbei → keine
        var leave = BattleStateHypothesis.Parse(Varint(16_659).Concat(Varint(0)).Concat(Varint(0)).ToArray())!;
        Assert.False(leave.Engaged);
        Assert.Null(BattleStateHypothesis.Parse(Varint(16_659).Concat(Varint(0)).Concat(Varint(5)).ToArray())); // Zustand > 1

        var tracker = new EnemyHpTracker();
        tracker.Report(16_659, 15_000_000, 15_504_000, T0);
        tracker.ReportBattleState(state);
        Assert.Equal(deadline, tracker.Status(16_659, T0)!.Deadline);
        Assert.Equal("Berserk in 4:48", OverlayFormat.BerserkTextUntil(deadline, T0.AddSeconds(12)));
        Assert.Equal("BERSERK!", OverlayFormat.BerserkTextUntil(deadline, T0.AddMinutes(6)));
        tracker.ReportBattleState(leave);
        Assert.Null(tracker.Status(16_659, T0)!.Deadline);
    }

    [Fact]
    public void BossBarOnlyForRealBosses()
    {
        var npcs = NpcNames.From(new Dictionary<int, NpcInfo>
        {
            [2_400_032] = new("Trainingspuppe", IsBoss: true, IsDummy: true),
            [2_300_371] = new("Kromedes Gier", IsBoss: true, IsDummy: false),
            [2_600_368] = new("Geisterfürst Agro", IsBoss: true, IsDummy: false),
            [2_300_302] = new("Lavaklumpen (Feuertempel)", IsBoss: false, IsDummy: false),
        });
        BossStatus Enemy(int code, long max) => new(1, max / 2, max, null, code);

        Assert.False(OverlayFormat.IsBoss(Enemy(2_400_032, 119_700), npcs.Lookup(2_400_032)));      // Puppe
        Assert.True(OverlayFormat.IsBoss(Enemy(2_300_371, 15_504_000), npcs.Lookup(2_300_371)));    // Dungeon-Boss
        Assert.True(OverlayFormat.IsBoss(Enemy(2_600_368, 121_000_000), npcs.Lookup(2_600_368)));   // Weltboss
        Assert.False(OverlayFormat.IsBoss(Enemy(2_300_302, 1_140_000), npcs.Lookup(2_300_302)));    // normaler Gegner
        Assert.True(OverlayFormat.IsBoss(Enemy(9_999_999, 75_002_019), null));                       // unbekannt, aber riesig
        Assert.False(OverlayFormat.IsBoss(Enemy(9_999_999, 570_000), null));                         // unbekannt, klein
        Assert.True(OverlayFormat.IsBoss(Enemy(2_300_302, 1_140_000) with { Deadline = T0 }, npcs.Lookup(2_300_302))); // Zeitlimit
    }

    [Fact]
    public void ManualBerserkCountdown()
    {
        var start = T0;
        Assert.Equal("Berserk in 8:00", OverlayFormat.BerserkText(TimeSpan.FromMinutes(8), start, T0));
        Assert.Equal("Berserk in 3:48", OverlayFormat.BerserkText(TimeSpan.FromMinutes(8), start, T0.AddSeconds(252.5)));
        Assert.Equal("BERSERK!", OverlayFormat.BerserkText(TimeSpan.FromMinutes(8), start, T0.AddMinutes(9)));
        Assert.Null(OverlayFormat.BerserkText(null, start, T0));
        Assert.Null(OverlayFormat.BerserkText(TimeSpan.FromMinutes(8), null, T0));

        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0.AddSeconds(3), 1, 900, 14_020_340, 10, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(1), 2, 900, 14_020_340, 10, false, false));
        Assert.Equal(T0.AddSeconds(3), meter.FirstHitOn(900, new HashSet<uint> { 1 }));
        Assert.Equal(T0.AddSeconds(1), meter.FirstHitOn(900, new HashSet<uint> { 1, 2 }));

        // Manuelle Berserk-Zeiten (v29–v38) werden seit v39 ignoriert – der Timer läuft nur automatisch.
        var settings = new OverlaySettings(BerserkSeconds: new Dictionary<string, int> { ["Kromedes Gier"] = 480 }).Normalized();
        Assert.Null(settings.BerserkSeconds);
    }

    [Fact]
    public void TopHitIsLargestDirectHitOnTargetFromSources()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1_575_191, true, false));
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 9_000_000, false, OverTime: true)); // über Zeit zählt nicht
        meter.Add(new CombatHit(T0, 2, 900, 17_010_000, 2_000_000, false, false));          // nicht in den Quellen
        meter.Add(new CombatHit(T0, 1, 901, 14_020_340, 3_000_000, false, false));          // anderes Ziel
        Assert.Equal(1_575_191, meter.TopHit(900, new HashSet<uint> { 1 }));
        Assert.Equal(2_000_000, meter.TopHit(900, new HashSet<uint> { 1, 2 }));
        Assert.Null(meter.TopHit(902, new HashSet<uint> { 1 }));
    }

    [Fact]
    public void WithoutSpawnOnlyRemainingHpIsShown()
    {
        var tracker = new EnemyHpTracker();
        tracker.Report(9, 46_800_000, null, T0);
        var boss = tracker.Status(9, T0)!;
        Assert.Null(boss.Fraction);
        Assert.Null(boss.TimeToKill);
        Assert.Equal("46,80M HP", OverlayFormat.BossText(boss));
    }

    [Fact]
    public void HpAboveKnownMaxDropsTheMax()
    {
        var tracker = new EnemyHpTracker();
        tracker.Report(5, 50_000, 63_000, T0);
        tracker.Report(5, 119_700, null, T0.AddSeconds(1)); // Kennung offenbar neu vergeben
        Assert.Null(tracker.Status(5, T0.AddSeconds(1))!.MaxHp);
    }

    [Fact]
    public void MainTargetIsWhatOwnCharacterHitsMost()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(1), 1, 901, 14_020_340, 5000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(1), 2, 900, 16_100_004, 90_000, false, false)); // anderer Spieler

        Assert.Equal(901u, meter.MainTarget(T0.AddSeconds(2), TimeSpan.FromSeconds(10)));
        Assert.Null(meter.MainTarget(T0.AddSeconds(30), TimeSpan.FromSeconds(10)));
    }

    /// <summary>Andere schlagen eine Puppe, ich (noch unbekannt) nicht: keine Boss-Leiste.</summary>
    [Fact]
    public void NoMainTargetWithoutOwnCharacterOrOwnHits()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 2, 19_974, 16_100_004, 90_000, false, false));
        Assert.Null(meter.MainTarget(T0.AddSeconds(1), TimeSpan.FromSeconds(10)));

        meter.ReportPlayer(1, "Ich", isSelf: true);
        Assert.Null(meter.MainTarget(T0.AddSeconds(1), TimeSpan.FromSeconds(10))); // ich habe nichts getroffen
    }
}
