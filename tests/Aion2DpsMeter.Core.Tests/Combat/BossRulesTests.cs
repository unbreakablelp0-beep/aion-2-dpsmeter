using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Echte Bosse für Boss-Leiste und Bestenliste: keine Totems, Puppen, Objekte, keine unbekannten Gegner.</summary>
public class BossRulesTests
{
    [Fact]
    public void ObjectsAndDummiesAreNeverBosses()
    {
        Assert.False(BossRules.IsRealBoss(new NpcInfo("Strahlendes Totem (Mau)", IsBoss: true, IsDummy: false), hasDeadline: false));
        Assert.False(BossRules.IsRealBoss(new NpcInfo("Strahlendes Totem (Mau)", IsBoss: true, IsDummy: false), hasDeadline: true));
        Assert.False(BossRules.IsRealBoss(new NpcInfo("Trainingspuppe", IsBoss: true, IsDummy: false), false));
        Assert.False(BossRules.IsRealBoss(new NpcInfo("Sandsack", IsBoss: true, IsDummy: false), false));
        Assert.False(BossRules.IsRealBoss(new NpcInfo("Irgendwas", IsBoss: true, IsDummy: true), false));
    }

    [Fact]
    public void RealBossesNeedTheListOrTheGamesTimeLimit()
    {
        Assert.True(BossRules.IsRealBoss(new NpcInfo("Nekromantin Duanka", IsBoss: true, IsDummy: false), false));
        Assert.True(BossRules.IsRealBoss(new NpcInfo("Kromedes Gier", IsBoss: true, IsDummy: false), false));
        Assert.False(BossRules.IsRealBoss(new NpcInfo("Lavaklumpen", IsBoss: false, IsDummy: false), false));
        Assert.False(BossRules.IsRealBoss(null, false));  // unbekannt – auch mit vielen HP nicht für die Bestenliste
        Assert.True(BossRules.IsRealBoss(null, true));    // Zeitlimit vom Spiel
    }

    [Fact]
    public void BossBarHidesTotems()
    {
        var totem = new BossStatus(1, 5_000_000, 10_000_000, null, 2300907);
        Assert.False(OverlayFormat.IsBoss(totem, new NpcInfo("Strahlendes Totem (Mau)", IsBoss: true, IsDummy: false)));
    }
}
