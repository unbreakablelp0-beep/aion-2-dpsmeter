using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Bestenliste: nur eigene Werte eines Bosskampfs, Name nur auf Wunsch, nie andere Spieler.</summary>
public class StatsShareTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static FightRecord Fight(string? boss, double seconds) => new(Guid.NewGuid(), "K", T0, T0.AddSeconds(seconds), "Lyra",
        [new FightPlayerRecord(1, "Lyra", 14, 1_200_000, 20_000, true, []), new FightPlayerRecord(2, "Korvin", 16, 2_000_000, 33_000, false, [])],
        null, boss);

    [Fact]
    public void SharesOnlyOwnValuesOfBossFights()
    {
        var shared = SharedFight.From(Fight("Feuerwächter", 60), "0.0.7", withName: false)!;
        Assert.Equal("Feuerwächter", shared.Boss);
        Assert.Equal(60, shared.DurationSeconds);
        Assert.Equal(1_200_000, shared.Damage);
        Assert.Equal(20_000, shared.Dps);
        Assert.Equal("Bogenschütze", shared.Class);
        Assert.Null(shared.Player);
        Assert.DoesNotContain("Korvin", shared.ToJson()); // andere Spieler nie

        Assert.Equal("Lyra", SharedFight.From(Fight("Feuerwächter", 60), "0.0.7", withName: true)!.Player);

        // Eigener GS und eigene KS (Einwilligung ab Stand 2); unplausible Werte werden weggelassen.
        var withPower = SharedFight.From(Fight("Feuerwächter", 60), "0.0.8", false, 2261, 98_631)!;
        Assert.Equal(2261, withPower.GearScore);
        Assert.Equal(98_631, withPower.CombatPower);
        Assert.Contains("\"gs\":2261", withPower.ToJson());
        Assert.Null(SharedFight.From(Fight("Feuerwächter", 60), "0.0.8", false, 0, 5)!.CombatPower);
        Assert.Null(SharedFight.From(Fight(null, 60), "0.0.7", false));          // kein Boss
        Assert.Null(SharedFight.From(Fight("Feuerwächter", 5), "0.0.7", false)); // zu kurz
    }

    [Fact]
    public void JsonUsesDatabaseColumnsAndSharingIsOffByDefault()
    {
        var json = SharedFight.From(Fight("Feuerwächter", 60), "0.0.7", false)!.ToJson();
        foreach (var column in new[] { "\"boss\"", "\"duration_s\"", "\"damage\"", "\"dps\"", "\"class\"", "\"version\"", "\"player\"" }) Assert.Contains(column, json);
        var settings = new OverlaySettings().Normalized();
        Assert.Equal(ShareModes.Off, settings.ShareMode);
        Assert.False(settings.ShareAsked);
        Assert.Equal(ShareModes.Off, new OverlaySettings(ShareMode: "Alles").Normalized().ShareMode);
    }
}
