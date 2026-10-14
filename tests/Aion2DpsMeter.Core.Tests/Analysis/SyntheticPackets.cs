using System.Buffers.Binary;
using System.Net;
using Aion2DpsMeter.Core.Analysis;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>
/// Baut synthetische Pakete nach den öffentlichen Standards (Ethernet, IPv4/IPv6, TCP/UDP).
/// Adressen aus den Dokumentationsbereichen (192.0.2.0/24, 198.51.100.0/24, 2001:db8::/32).
/// </summary>
internal static class SyntheticPackets
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public const string Local = "192.0.2.10";
    public const string Server = "198.51.100.7";

    public static RawPacket Tcp(double atSeconds, string src, int srcPort, string dst, int dstPort, byte[] payload,
        uint seq = 0, TcpFlags flags = TcpFlags.Ack) =>
        Ethernet(atSeconds, 0x0800, IPv4(src, dst, 6, TcpSegment(srcPort, dstPort, payload, seq, flags)));

    public static RawPacket Udp(double atSeconds, string src, int srcPort, string dst, int dstPort, byte[] payload) =>
        Ethernet(atSeconds, 0x0800, IPv4(src, dst, 17, UdpDatagram(srcPort, dstPort, payload)));

    public static RawPacket UdpV6(double atSeconds, string src, int srcPort, string dst, int dstPort, byte[] payload) =>
        Ethernet(atSeconds, 0x86DD, IPv6(src, dst, 17, UdpDatagram(srcPort, dstPort, payload)));

    public static RawPacket Loopback(double atSeconds, string src, int srcPort, string dst, int dstPort, byte[] payload)
    {
        var ip = IPv4(src, dst, 17, UdpDatagram(srcPort, dstPort, payload));
        var frame = new byte[4 + ip.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 2); // AF_INET
        ip.CopyTo(frame, 4);
        return new RawPacket(T0.AddSeconds(atSeconds), PacketHeaderParser.LinkTypeNull, frame, frame.Length);
    }

    public static RawPacket Ethernet(double atSeconds, ushort etherType, byte[] payload)
    {
        var frame = new byte[14 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), etherType);
        payload.CopyTo(frame, 14);
        return new RawPacket(T0.AddSeconds(atSeconds), PacketHeaderParser.LinkTypeEthernet, frame, frame.Length);
    }

    public static byte[] IPv4(string src, string dst, byte protocol, byte[] payload, ushort fragmentField = 0)
    {
        var ip = new byte[20 + payload.Length];
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(2), (ushort)ip.Length);
        BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(6), fragmentField);
        ip[8] = 64;
        ip[9] = protocol;
        IPAddress.Parse(src).GetAddressBytes().CopyTo(ip, 12);
        IPAddress.Parse(dst).GetAddressBytes().CopyTo(ip, 16);
        payload.CopyTo(ip, 20);
        return ip;
    }

    public static byte[] IPv6(string src, string dst, byte nextHeader, byte[] payload)
    {
        var ip = new byte[40 + payload.Length];
        ip[0] = 0x60;
        BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(4), (ushort)payload.Length);
        ip[6] = nextHeader;
        ip[7] = 64;
        IPAddress.Parse(src).GetAddressBytes().CopyTo(ip, 8);
        IPAddress.Parse(dst).GetAddressBytes().CopyTo(ip, 24);
        payload.CopyTo(ip, 40);
        return ip;
    }

    public static byte[] TcpSegment(int srcPort, int dstPort, byte[] payload, uint seq = 0, TcpFlags flags = TcpFlags.Ack)
    {
        var tcp = new byte[20 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(tcp, (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.AsSpan(4), seq);
        tcp[12] = 5 << 4; // Kopflänge 20 Bytes
        tcp[13] = (byte)flags;
        payload.CopyTo(tcp, 20);
        return tcp;
    }

    public static byte[] UdpDatagram(int srcPort, int dstPort, byte[] payload)
    {
        var udp = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(udp, (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(4), (ushort)udp.Length);
        payload.CopyTo(udp, 8);
        return udp;
    }

    /// <summary>Pseudozufällige Bytes mit festem Startwert (reproduzierbar).</summary>
    public static byte[] RandomBytes(int length, int seed = 42)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Gut lesbarer, sich wiederholender Text – niedrige Entropie.</summary>
    public static byte[] TextBytes(int length) =>
        System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hallo welt ", length / 11 + 1)))[..length];
}
