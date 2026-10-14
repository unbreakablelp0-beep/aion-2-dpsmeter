using System.Buffers.Binary;
using System.Net;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Ein Paket mit Rohdaten aus einer Aufzeichnungsdatei.
/// </summary>
/// <param name="Timestamp">Zeitpunkt.</param>
/// <param name="LinkType">libpcap-Linktyp (1 = Ethernet, 0 = BSD-Loopback, 101 = Raw-IP, 108 = OpenBSD-Loopback).</param>
/// <param name="Data">Aufgezeichnete Bytes.</param>
/// <param name="OriginalLength">Ursprüngliche Länge auf der Leitung.</param>
public sealed record RawPacket(DateTimeOffset Timestamp, int LinkType, ReadOnlyMemory<byte> Data, int OriginalLength);

/// <summary>
/// Ein Netzwerkendpunkt (IP-Adresse und Port).
/// </summary>
public sealed record Endpoint(IPAddress Address, int Port)
{
    public override string ToString() =>
        Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{Address}]:{Port}" : $"{Address}:{Port}";
}

/// <summary>
/// Ergebnis der Kopfzeilen-Auswertung: nur Standardprotokolle (Ethernet, IPv4/IPv6, TCP/UDP),
/// kein Spielprotokoll.
/// </summary>
public sealed record ParsedPacket(
    DateTimeOffset Timestamp,
    TransportProtocol Protocol,
    Endpoint Source,
    Endpoint Destination,
    int WireLength,
    ReadOnlyMemory<byte> Payload,
    TcpHeader? Tcp = null);

/// <summary>TCP-Flags nach RFC 9293.</summary>
[Flags]
public enum TcpFlags : byte
{
    None = 0,
    Fin = 0x01,
    Syn = 0x02,
    Rst = 0x04,
    Psh = 0x08,
    Ack = 0x10,
    Urg = 0x20,
}

/// <summary>Für die Stromzusammensetzung nötige TCP-Felder.</summary>
public sealed record TcpHeader(uint SequenceNumber, uint AcknowledgmentNumber, TcpFlags Flags);

/// <summary>
/// Liest Ethernet-/Loopback-, IPv4-/IPv6- und TCP-/UDP-Kopfzeilen nach den öffentlichen Standards.
/// IPv4-Folgefragmente und IPv6-Erweiterungsheader werden nicht ausgewertet.
/// </summary>
public static class PacketHeaderParser
{
    public const int LinkTypeNull = 0;
    public const int LinkTypeEthernet = 1;
    public const int LinkTypeRaw = 101;
    public const int LinkTypeLoop = 108;

    private const ushort EtherTypeIPv4 = 0x0800;
    private const ushort EtherTypeIPv6 = 0x86DD;
    private const ushort EtherTypeVlan = 0x8100;
    private const byte IpProtocolTcp = 6;
    private const byte IpProtocolUdp = 17;

    /// <summary>Versucht, das Paket zu lesen. false bei unbekanntem/unvollständigem Aufbau.</summary>
    public static bool TryParse(RawPacket packet, out ParsedPacket? result)
    {
        ArgumentNullException.ThrowIfNull(packet);
        result = null;
        var data = packet.Data;
        var span = data.Span;

        int ipOffset;
        switch (packet.LinkType)
        {
            case LinkTypeEthernet:
                if (span.Length < 14) return false;
                ipOffset = 14;
                var etherType = BinaryPrimitives.ReadUInt16BigEndian(span[12..]);
                if (etherType == EtherTypeVlan)
                {
                    if (span.Length < 18) return false;
                    etherType = BinaryPrimitives.ReadUInt16BigEndian(span[16..]);
                    ipOffset = 18;
                }

                if (etherType is not (EtherTypeIPv4 or EtherTypeIPv6)) return false;
                break;
            case LinkTypeNull:
            case LinkTypeLoop:
                ipOffset = 4;
                break;
            case LinkTypeRaw:
                ipOffset = 0;
                break;
            default:
                return false;
        }

        if (span.Length <= ipOffset) return false;
        var version = span[ipOffset] >> 4;

        byte protocol;
        IPAddress source, destination;
        int l4Offset, l4Length;
        if (version == 4)
        {
            if (span.Length < ipOffset + 20) return false;
            var ip = span[ipOffset..];
            var headerLength = (ip[0] & 0x0F) * 4;
            var totalLength = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
            var fragmentOffset = BinaryPrimitives.ReadUInt16BigEndian(ip[6..]) & 0x1FFF;
            if (headerLength < 20 || totalLength < headerLength || fragmentOffset != 0) return false;

            protocol = ip[9];
            source = new IPAddress(ip.Slice(12, 4));
            destination = new IPAddress(ip.Slice(16, 4));
            l4Offset = ipOffset + headerLength;
            l4Length = totalLength - headerLength;
        }
        else if (version == 6)
        {
            if (span.Length < ipOffset + 40) return false;
            var ip = span[ipOffset..];
            protocol = ip[6];
            source = new IPAddress(ip.Slice(8, 16));
            destination = new IPAddress(ip.Slice(24, 16));
            l4Offset = ipOffset + 40;
            l4Length = BinaryPrimitives.ReadUInt16BigEndian(ip[4..]);
        }
        else
        {
            return false;
        }

        // Nur so viel auswerten, wie tatsächlich aufgezeichnet wurde.
        l4Length = Math.Min(l4Length, span.Length - l4Offset);
        if (l4Length < 0) return false;
        var l4 = span.Slice(l4Offset, l4Length);

        TransportProtocol transport;
        TcpHeader? tcpHeader = null;
        int sourcePort, destinationPort, payloadOffset, payloadLength;
        switch (protocol)
        {
            case IpProtocolTcp:
                if (l4.Length < 20) return false;
                var dataOffset = (l4[12] >> 4) * 4;
                if (dataOffset < 20 || dataOffset > l4.Length) return false;
                transport = TransportProtocol.Tcp;
                sourcePort = BinaryPrimitives.ReadUInt16BigEndian(l4);
                destinationPort = BinaryPrimitives.ReadUInt16BigEndian(l4[2..]);
                payloadOffset = dataOffset;
                payloadLength = l4.Length - dataOffset;
                tcpHeader = new TcpHeader(
                    BinaryPrimitives.ReadUInt32BigEndian(l4[4..]),
                    BinaryPrimitives.ReadUInt32BigEndian(l4[8..]),
                    (TcpFlags)(l4[13] & 0x3F));
                break;
            case IpProtocolUdp:
                if (l4.Length < 8) return false;
                var udpLength = BinaryPrimitives.ReadUInt16BigEndian(l4[4..]);
                if (udpLength < 8) return false;
                transport = TransportProtocol.Udp;
                sourcePort = BinaryPrimitives.ReadUInt16BigEndian(l4);
                destinationPort = BinaryPrimitives.ReadUInt16BigEndian(l4[2..]);
                payloadOffset = 8;
                payloadLength = Math.Min(udpLength, l4.Length) - 8;
                break;
            default:
                return false;
        }

        result = new ParsedPacket(
            packet.Timestamp,
            transport,
            new Endpoint(source, sourcePort),
            new Endpoint(destination, destinationPort),
            packet.OriginalLength,
            data.Slice(l4Offset + payloadOffset, payloadLength),
            tcpHeader);
        return true;
    }
}
