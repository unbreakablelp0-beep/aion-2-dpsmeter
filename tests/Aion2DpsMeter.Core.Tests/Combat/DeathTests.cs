using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Tode aus Gruppen-HP (1B 92) und 00 8D; Bytefolgen nach dem Muster der Feuertempel-Aufnahme.</summary>
public class DeathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // Kennung 1234 (D2 09), danach wie beobachtet: HP, Max-HP, weitere Felder, letztes Byte lebt/tot.
    private static readonly byte[] Alive = Convert.FromHexString("D209" + "8EC901C7D701060F0000060F0000000000000000000000000000F049020001");
    private static readonly byte[] Dead = Convert.FromHexString("D209" + "00D2C801A6090000060F0000000000000000000000000000F049020000");

    [Fact]
    public void ParsesPartyMemberHpAndAliveFlag()
    {
        var alive = PartyHpHypothesis.Parse(Alive)!;
        Assert.Equal(1234u, alive.Target);
        Assert.Equal(25_742, alive.Hp);
        Assert.Equal(27_591, alive.MaxHp);
        Assert.True(alive.Alive);

        var dead = PartyHpHypothesis.Parse(Dead)!;
        Assert.Equal(0, dead.Hp);
        Assert.False(dead.Alive);

        var odd = (byte[])Alive.Clone();
        odd[^1] = 7; // kein Lebens-Byte
        Assert.Null(PartyHpHypothesis.Parse(odd));
        Assert.Null(PartyHpHypothesis.Parse(Alive.AsSpan(0, 4)));
    }

    [Fact]
    public void CountsDeathsOfNamedPlayersOnceAndPerFight()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromSeconds(10));
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.ReportPlayer(2, "Heiler", isSelf: false);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1_000, false, false));
        meter.Add(new CombatHit(T0, 2, 900, 17_010_000, 1_000, false, false));

        meter.ReportDeath(2, T0.AddSeconds(1));
        meter.ReportDeath(2, T0.AddSeconds(1.1)); // dieselbe Meldung aus der zweiten Quelle
        meter.ReportDeath(2, T0.AddSeconds(7));   // zweiter Tod nach Wiederbelebung
        meter.ReportDeath(900, T0.AddSeconds(2)); // Gegner – zählt nicht
        meter.Add(new CombatHit(T0.AddSeconds(8), 1, 900, 14_020_340, 1_000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(8), 2, 900, 17_010_000, 1_000, false, false));

        var snapshot = meter.Snapshot(T0.AddSeconds(9));
        Assert.Equal(2, snapshot.Actors.Single(a => a.Actor == 2).Deaths);
        Assert.Equal(0, snapshot.Actors.Single(a => a.Actor == 1).Deaths);
        Assert.Equal(2, OverlayFormat.Build(snapshot, 8).Rows.Single(r => r.Actor == 2).Deaths);

        // Neuer Kampf: Tode aus dem alten zählen nicht mehr.
        meter.Add(new CombatHit(T0.AddMinutes(5), 2, 901, 17_010_000, 1_000, false, false));
        Assert.Equal(0, meter.Snapshot(T0.AddMinutes(5)).Actors.Single(a => a.Actor == 2).Deaths);
    }

    [Fact]
    public void OverlayRowsHaveNoPetLabelAnymore()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Ich", isSelf: true);
        meter.ReportSummon(50, 1);
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1_000, false, false));
        meter.Add(new CombatHit(T0, 50, 900, 14_020_340, 1_000, false, false));
        var row = OverlayFormat.Build(meter.Snapshot(T0), 8).Rows.Single();
        Assert.Equal(0, row.Deaths);
        Assert.Equal(2_000, row.DamageValue); // Pet-Schaden zählt weiter beim Besitzer
    }
}
