using System.Buffers.Binary;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Combat;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Live-Decoder mit synthetischem TCP-Strom nach den beschriebenen Hypothesen – keine echten Spieldaten.</summary>
public class LiveCombatDecoderTests
{
    private const uint Isn = 1000;
    private static readonly byte[] Block11 = [0x11, 0x00, 0x22, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void ReportsDirectAndOverTimeHitsIncludingBundles()
    {
        var stream = new List<byte>();
        for (var i = 0; i < 3; i++) stream.AddRange(Heartbeat());
        stream.AddRange(Frame([0x04, 0x38, .. Damage(target: 900, actor: 10, candidate: 1580, damageType: 3), 0x01, 0x00]));
        stream.AddRange(Frame([0x04, 0x38, .. Varint(900), 0x00])); // Ankündigung: kein Treffer
        stream.AddRange(Frame([0x05, 0x38, .. Dot(target: 900, effect: 0x0A, actor: 10, amount: 312)]));
        stream.AddRange(Frame([0x05, 0x38, .. Dot(target: 900, effect: 0x08, actor: 10, amount: 36)])); // kein Schadenseffekt
        stream.AddRange(Bundle(Frame([0x04, 0x38, .. Damage(900, 11, 2350, 2), 0x01, 0x00])));
        stream.AddRange(Frame([0x04, 0x38, .. Damage(target: 10, actor: 10, candidate: 1064, damageType: 2)])); // Selbsttreffer

        var hits = new List<CombatHit>();
        var decoder = new LiveCombatDecoder(hits.Add);
        decoder.Add(Tcp(0, Server, 13000, Local, 50001, [], Isn, TcpFlags.Syn | TcpFlags.Ack));
        var seq = Isn + 1;
        foreach (var part in stream.Chunk(37))
        {
            decoder.Add(Tcp(0.1, Server, 13000, Local, 50001, part, seq));
            seq += (uint)part.Length;
        }

        Assert.Equal(4, hits.Count);
        Assert.Equal(new long[] { 1580, 312, 2350, 1064 }, hits.Select(h => h.Amount));
        Assert.True(hits[0].IsCritical);
        Assert.True(hits[1].OverTime);
        Assert.Equal(11u, hits[2].Actor);

        var meter = new LiveCombatMeter();
        foreach (var h in hits) meter.Add(h);
        var s = meter.Snapshot(T0);
        Assert.Equal(1580 + 312 + 2350, s.TotalDamage);
        Assert.Equal(1, s.IgnoredSelfHits);
    }

    [Fact]
    public void FramesBeforeThreeHeartbeatsAreHeldBackAndReleasedInOrder()
    {
        byte[][] frames =
        [
            Heartbeat(),
            Heartbeat(),
            Frame([0x04, 0x38, .. Damage(900, 10, 1580, 2), 0x01, 0x00]),
            Heartbeat(),
            Frame([0x04, 0x38, .. Damage(900, 10, 2000, 2), 0x01, 0x00]),
        ];

        var hits = new List<CombatHit>();
        var decoder = new LiveCombatDecoder(hits.Add);
        decoder.Add(Tcp(0, Server, 13000, Local, 50001, [], Isn, TcpFlags.Syn | TcpFlags.Ack));
        var seq = Isn + 1;
        for (var i = 0; i < frames.Length; i++) // je Rahmen ein Segment
        {
            decoder.Add(Tcp(0.1, Server, 13000, Local, 50001, frames[i], seq));
            seq += (uint)frames[i].Length;
            if (i == 2) Assert.Empty(hits); // Treffer vor dem 3. Heartbeat: noch zurückgehalten
            if (i == 3) Assert.Single(hits); // mit dem 3. Heartbeat freigegeben
        }

        Assert.Equal(new long[] { 1580, 2000 }, hits.Select(h => h.Amount));
    }

    [Fact]
    public void PlayerInfoIsReportedIncludingSelfAtLoginBeforeHeartbeats()
    {
        byte[][] frames =
        [
            Frame([0x33, 0x36, .. PlayerBody(11194, "Testheld", server: 2309)]), // vor jedem Heartbeat (Login)
            Heartbeat(),
            Heartbeat(),
            Heartbeat(), // erst hier geht die Sperre auf – auch ohne weiteren Spieler- oder Trefferrahmen
            Frame([0x45, 0x36, .. PlayerBody(8575, "Andere", server: null)]),
        ];

        var players = new List<PlayerInfoCandidate>();
        var decoder = new LiveCombatDecoder(_ => { }, players.Add);
        decoder.Add(Tcp(0, Server, 13000, Local, 50001, [], Isn, TcpFlags.Syn | TcpFlags.Ack));
        var seq = Isn + 1;
        var afterThirdHeartbeat = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            decoder.Add(Tcp(0.1, Server, 13000, Local, 50001, frames[i], seq));
            seq += (uint)frames[i].Length;
            if (i == 3) afterThirdHeartbeat = players.Count;
        }

        Assert.Equal(1, afterThirdHeartbeat);
        Assert.Equal(2, players.Count);
        Assert.True(players[0].IsSelf);
        Assert.Equal(11194u, players[0].EntityId);
        Assert.Equal("Testheld", players[0].Name);
        Assert.Equal((ushort)2309, players[0].ServerId);
        Assert.False(players[1].IsSelf);
        Assert.Equal("Andere", players[1].Name);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })] // Kennung 0
    [InlineData(new byte[] { 0x05, 1, 2, 3, 4, 0x00, 0x03, 0x41, 0x42, 0x43 })] // Maskenbit 0 fehlt
    [InlineData(new byte[] { 0x05, 1, 2, 3, 4, 0x01, 0x05, 0x41, 0x42 })] // Name länger als der Rest
    [InlineData(new byte[] { 0x05, 1, 2, 3, 4, 0x01, 0x02, 0xC3, 0x28 })] // ungültiges UTF-8
    [InlineData(new byte[] { 0x05, 1, 2, 3, 4, 0x01, 0x02, 0x41, 0x0A })] // Steuerzeichen
    [InlineData(new byte[] { 0x05, 1, 2, 3, 4, 0x01, 0x25 })] // Länge 37
    public void PlayerInfoRejectsImplausibleBodies(byte[] body)
    {
        Assert.Null(PlayerInfoHypothesis.Parse(PlayerInfoHypothesis.PlayerType, body));
    }

    [Fact]
    public void PlayerInfoReadsMultiByteNames()
    {
        var c = PlayerInfoHypothesis.Parse(PlayerInfoHypothesis.OldPlayerType, PlayerBody(42, "ミカ", server: null));

        Assert.Equal("ミカ", c?.Name);
        Assert.Null(PlayerInfoHypothesis.Parse(0x0438, PlayerBody(42, "X", null))); // falscher Typ
    }

    [Fact]
    public void RandomTrafficGivesNoHits()
    {
        var hits = new List<CombatHit>();
        var decoder = new LiveCombatDecoder(hits.Add);
        for (var i = 0; i < 50; i++)
        {
            decoder.Add(Tcp(i * 0.01, Server, 443, Local, 50002, RandomBytes(1000, seed: i), (uint)(5000 + i * 1000)));
        }

        Assert.Empty(hits);
    }

    // --- Hilfen -----------------------------------------------------------------------------------

    private static byte[] Heartbeat() => Frame([0x00, 0x36, 1, 2, 3, 4, 5, 6, 7, 8]);

    private static byte[] PlayerBody(uint id, string name, ushort? server)
    {
        var b = new List<byte>();
        b.AddRange(Varint(id));
        b.AddRange([9, 9, 9, 9]); // Maske 1
        b.Add(0x01); // Maske 2, Bit 0
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        b.Add((byte)bytes.Length);
        b.AddRange(bytes);
        if (server is { } s) b.AddRange([(byte)s, (byte)(s >> 8), 0x18, 0, 0, 0]);
        return b.ToArray();
    }

    private static byte[] Damage(uint target, uint actor, uint candidate, uint damageType)
    {
        var b = new List<byte>();
        b.AddRange(Varint(target));
        b.Add(0x06); // Layout 6
        b.AddRange(Varint(0));
        b.AddRange(Varint(actor));
        var skill = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(skill, 17_000_100);
        b.AddRange(skill);
        b.Add(0x01);
        b.AddRange(Varint(damageType));
        b.AddRange(Block11);
        b.AddRange(Varint(11_910));
        b.AddRange(Varint(candidate));
        return b.ToArray();
    }

    private static byte[] Dot(uint target, byte effect, uint actor, uint amount)
    {
        var b = new List<byte>();
        b.AddRange(Varint(target));
        b.Add(effect);
        b.AddRange(Varint(actor));
        b.AddRange(Varint(55_117));
        var skill = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(skill, 1_405_000_000);
        b.AddRange(skill);
        b.AddRange(Varint(amount));
        return b.ToArray();
    }

    private static byte[] Bundle(byte[] content)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)content.Length);
        return Frame([0xFF, 0xFF, .. size, .. Lz4BlockDecoderTests.Lz4Literals(content)]);
    }

    private static byte[] Varint(uint value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static byte[] Frame(byte[] body) => [.. Varint((uint)body.Length + 4), .. body];
}
