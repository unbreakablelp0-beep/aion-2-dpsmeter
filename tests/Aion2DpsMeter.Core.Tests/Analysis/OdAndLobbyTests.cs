using System.Text;
using Aion2DpsMeter.Core.Analysis;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Od-Zusatzwert (Bytefolgen wie in den Aufnahmen) und Mitgliederliste für die Lobby (erfundene Namen).</summary>
public class OdAndLobbyTests
{
    [Fact]
    public void ReadsOdBonusFromLoginListAndUpdate()
    {
        // Einlösen: 55 → 95 bzw. 200 → 240, jeweils +40.
        Assert.Equal(95, OdEnergyHypothesis.ParseBonus(OdEnergyHypothesis.UpdateType, Convert.FromHexString("010C018793030F5F0128000000")));
        Assert.Equal(240, OdEnergyHypothesis.ParseBonus(OdEnergyHypothesis.UpdateType, Convert.FromHexString("010C018793030FF0010128000000")));

        // Login-Liste: Eintrag mitten in einem längeren Rahmen.
        var list = Convert.FromHexString("008B969800008C9698000C018793030F3704028793030E04038793030E0404");
        Assert.Equal(55, OdEnergyHypothesis.ParseBonus(OdEnergyHypothesis.ListType, list));

        // Andere Typen / andere 0C-61-Inhalte: nichts.
        Assert.Null(OdEnergyHypothesis.ParseBonus(0x0C61, Convert.FromHexString("00040A0000000E02")));
        Assert.Null(OdEnergyHypothesis.ParseBonus(0x0297, Convert.FromHexString("010C018793030F5F0128000000")));
    }

    private static byte[] Member(int slot, string name, int level, int gear, long power)
    {
        var n = Encoding.UTF8.GetBytes(name);
        var bytes = new List<byte> { 0x0C, (byte)slot, 9, 9, 9, 9, 9, 9, 0x05, 0x09, (byte)n.Length };
        bytes.AddRange(n);
        bytes.AddRange(BitConverter.GetBytes(16));    // Klasse
        bytes.AddRange(BitConverter.GetBytes(level));
        bytes.AddRange(BitConverter.GetBytes(gear));
        bytes.AddRange(new byte[] { 0x05, 0x09, 0x05, 0x09, 0x04 });
        bytes.AddRange(BitConverter.GetBytes(power));
        bytes.AddRange(new byte[] { 0, 0, 1, 1 });
        return bytes.ToArray();
    }

    [Fact]
    public void ListsLobbyMembersWithLevelGearAndPower()
    {
        var body = new byte[] { 1, 2, 3, 4, 0, 3 }.Concat(new byte[20])
            .Concat(Member(1, "Lyra", 45, 2261, 98_631))
            .Concat(Member(2, "Korvin", 30, 394, 20_922))
            .Concat(Member(3, "Mira", 24, 490, 20_453))
            .ToArray();
        var members = PartyRosterHypothesis.Members(body);
        Assert.Equal(["Lyra", "Korvin", "Mira"], members.Select(m => m.Name));
        Assert.Equal([45, 30, 24], members.Select(m => m.Level));
        Assert.Equal(2261, members[0].GearScore);
        Assert.Equal(98_631, members[0].CombatPower);

        var roster = PartyRosterHypothesis.Parse(body)!;
        Assert.Equal(3, roster.Members.Count);
        Assert.Empty(new PartyRoster(3, ["Lyra"]).Members); // gemerkte Gruppe: keine Werte
    }
}
