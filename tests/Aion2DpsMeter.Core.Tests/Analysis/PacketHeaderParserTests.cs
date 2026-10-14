using System.Net;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Analysis;

public class PacketHeaderParserTests
{
    [Fact]
    public void EthernetIPv4Tcp_IsParsed()
    {
        var packet = Tcp(1, Local, 50001, Server, 7777, [1, 2, 3, 4, 5]);

        Assert.True(PacketHeaderParser.TryParse(packet, out var parsed));
        Assert.Equal(TransportProtocol.Tcp, parsed!.Protocol);
        Assert.Equal(new Endpoint(IPAddress.Parse(Local), 50001), parsed.Source);
        Assert.Equal(new Endpoint(IPAddress.Parse(Server), 7777), parsed.Destination);
        Assert.Equal([1, 2, 3, 4, 5], parsed.Payload.ToArray());
        Assert.Equal(14 + 20 + 20 + 5, parsed.WireLength);
        Assert.Equal(T0.AddSeconds(1), parsed.Timestamp);
    }

    [Fact]
    public void EthernetIPv4Udp_IsParsed()
    {
        Assert.True(PacketHeaderParser.TryParse(Udp(0, Server, 9000, Local, 60000, [9, 9]), out var parsed));
        Assert.Equal(TransportProtocol.Udp, parsed!.Protocol);
        Assert.Equal(9000, parsed.Source.Port);
        Assert.Equal(60000, parsed.Destination.Port);
        Assert.Equal([9, 9], parsed.Payload.ToArray());
    }

    [Fact]
    public void EthernetIPv6Udp_IsParsed()
    {
        Assert.True(PacketHeaderParser.TryParse(UdpV6(0, "2001:db8::1", 1000, "2001:db8::2", 2000, [7]), out var parsed));
        Assert.Equal("[2001:db8::1]:1000", parsed!.Source.ToString());
        Assert.Equal([7], parsed.Payload.ToArray());
    }

    [Fact]
    public void LoopbackIPv4Udp_IsParsed()
    {
        Assert.True(PacketHeaderParser.TryParse(Loopback(0, "127.0.0.1", 1, "127.0.0.1", 2, [5, 6]), out var parsed));
        Assert.Equal([5, 6], parsed!.Payload.ToArray());
    }

    [Fact]
    public void EmptyPayload_IsParsedWithZeroLength()
    {
        Assert.True(PacketHeaderParser.TryParse(Tcp(0, Local, 1, Server, 2, []), out var parsed));
        Assert.Equal(0, parsed!.Payload.Length);
    }

    [Fact]
    public void NonIpEthernet_IsRejected()
    {
        Assert.False(PacketHeaderParser.TryParse(Ethernet(0, 0x0806, new byte[28]), out _)); // ARP
    }

    [Fact]
    public void Icmp_IsRejected()
    {
        Assert.False(PacketHeaderParser.TryParse(Ethernet(0, 0x0800, IPv4(Local, Server, 1, new byte[8])), out _));
    }

    [Fact]
    public void IPv4FollowUpFragment_IsRejected()
    {
        var ip = IPv4(Local, Server, 17, UdpDatagram(1, 2, [1]), fragmentField: 0x00B9); // Offset ≠ 0
        Assert.False(PacketHeaderParser.TryParse(Ethernet(0, 0x0800, ip), out _));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    public void TruncatedPackets_AreRejectedWithoutException(int keepBytes)
    {
        var full = Tcp(0, Local, 1, Server, 2, [1, 2, 3]);
        var cut = new RawPacket(full.Timestamp, full.LinkType, full.Data[..keepBytes], full.OriginalLength);

        Assert.False(PacketHeaderParser.TryParse(cut, out _));
    }

    [Fact]
    public void PayloadCutBySnapLength_UsesAvailableBytes()
    {
        var full = Tcp(0, Local, 1, Server, 2, [1, 2, 3, 4, 5, 6]);
        var cut = new RawPacket(full.Timestamp, full.LinkType, full.Data[..^2], full.OriginalLength);

        Assert.True(PacketHeaderParser.TryParse(cut, out var parsed));
        Assert.Equal([1, 2, 3, 4], parsed!.Payload.ToArray());
        Assert.Equal(full.OriginalLength, parsed.WireLength);
    }

    [Fact]
    public void UnknownLinkType_IsRejected()
    {
        Assert.False(PacketHeaderParser.TryParse(new RawPacket(T0, 147, new byte[60], 60), out _));
    }
}
