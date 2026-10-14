using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Gruppenhinweis nur im/kurz nach dem Kampf; gemerkte Gruppe verfällt nach einem Solo-Kampf.</summary>
public class PartyHintTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HintOnlyWhileFightingOrShortlyAfter()
    {
        Assert.True(OverlayFormat.ShowPartyHint(true, 5, 2, fighting: true, null));
        Assert.True(OverlayFormat.ShowPartyHint(true, 5, 2, fighting: false, TimeSpan.FromSeconds(30)));
        Assert.False(OverlayFormat.ShowPartyHint(true, 5, 2, fighting: false, TimeSpan.FromSeconds(61))); // Leerlauf
        Assert.False(OverlayFormat.ShowPartyHint(true, 5, 2, fighting: false, null));                     // noch kein Kampf
        Assert.False(OverlayFormat.ShowPartyHint(true, 5, 5, fighting: true, null));                      // alle erkannt
        Assert.False(OverlayFormat.ShowPartyHint(false, 5, 2, fighting: true, null));                     // ausgeschaltet
        Assert.False(OverlayFormat.ShowPartyHint(true, null, 1, fighting: true, null));                   // keine Gruppe
        Assert.True(new OverlaySettings().Normalized().ShowPartyHint);
    }

    private static FightRecord Fight(double seconds, params FightPlayerRecord[] players) =>
        new(Guid.NewGuid(), "K", T0, T0.AddSeconds(seconds), "Ich", players);

    private static readonly FightPlayerRecord Me = new(1, "Ich", 14, 50_000, 1_000, true, []);
    private static readonly FightPlayerRecord Mate = new(2, "Heiler", 17, 10_000, 200, false, []);
    private static readonly FightPlayerRecord Stranger = new(7, "Fremd", 16, 10_000, 200, false, []);

    [Fact]
    public void RememberedPartyIsDroppedAfterLongSoloFight()
    {
        var meter = new LiveCombatMeter();
        meter.RememberParty(5, ["Ich", "Heiler"]);

        Assert.False(meter.ForgetRememberedPartyAfterSoloFight(Fight(10, Me)));          // zu kurz
        Assert.False(meter.ForgetRememberedPartyAfterSoloFight(Fight(60, Me, Mate)));    // Mitglied dabei
        Assert.Equal(5, meter.PartySize);
        Assert.True(meter.ForgetRememberedPartyAfterSoloFight(Fight(60, Me, Stranger))); // nur ich (+ Fremde)
        Assert.Null(meter.PartySize);
    }

    [Fact]
    public void RealRosterOfThisSessionIsKept()
    {
        var meter = new LiveCombatMeter();
        var body = new byte[] { 1, 2, 3, 4, 0, 5 }; // Kopf: Schlüssel, leerer Gruppenname, Größe 5
        meter.ReportParty(Aion2DpsMeter.Core.Analysis.PartyRosterHypothesis.Parse(body)!);
        Assert.False(meter.ForgetRememberedPartyAfterSoloFight(Fight(60, Me)));
        Assert.Equal(5, meter.PartySize);
        Assert.Equal(TimeSpan.FromHours(2), PartyStore.MaxAge);
    }
}
