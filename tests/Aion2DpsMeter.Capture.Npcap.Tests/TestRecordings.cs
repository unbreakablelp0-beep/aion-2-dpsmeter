using System.Buffers.Binary;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Erzeugt kleine, synthetische Aufzeichnungsdateien für Tests.
/// Inhalt: ausgedachte Bytes, kein echter Netzwerkverkehr.
/// </summary>
internal static class TestRecordings
{
    /// <summary>12:00:00 UTC am 01.01.2026.</summary>
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Drei Pakete: 60 Bytes bei T0, 1514 Bytes bei T0+0,5 s, 42 Bytes bei T0+2 s.</summary>
    public static readonly (TimeSpan Offset, int Length)[] Packets =
    [
        (TimeSpan.Zero, 60),
        (TimeSpan.FromMilliseconds(500), 1514),
        (TimeSpan.FromSeconds(2), 42),
    ];

    private const uint LinkTypeEthernet = 1;
    private const uint SnapLength = 65535;

    /// <summary>Klassisches pcap-Format (Little Endian, Mikrosekunden).</summary>
    public static byte[] CreatePcap() =>
        CreatePcap(Packets.Select(p => (p.Offset, Payload(p.Length))));

    /// <summary>Klassisches pcap-Format mit eigenen Ethernet-Frames.</summary>
    public static byte[] CreatePcap(IEnumerable<(TimeSpan Offset, byte[] Frame)> frames)
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, 0xA1B2C3D4); // Magic
        WriteUInt16(ms, 2);          // Version 2.4
        WriteUInt16(ms, 4);
        WriteUInt32(ms, 0);          // thiszone
        WriteUInt32(ms, 0);          // sigfigs
        WriteUInt32(ms, SnapLength);
        WriteUInt32(ms, LinkTypeEthernet);

        foreach (var (offset, frame) in frames)
        {
            var micros = ToUnixMicroseconds(T0 + offset);
            WriteUInt32(ms, (uint)(micros / 1_000_000));
            WriteUInt32(ms, (uint)(micros % 1_000_000));
            WriteUInt32(ms, (uint)frame.Length);
            WriteUInt32(ms, (uint)frame.Length);
            ms.Write(frame);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Ethernet/IPv4/UDP-Frame nach Standard, Adressen aus Dokumentationsbereichen.
    /// </summary>
    public static byte[] UdpFrame(byte[] source, int sourcePort, byte[] destination, int destinationPort, int payloadLength)
    {
        var frame = new byte[14 + 20 + 8 + payloadLength];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
        var ip = frame.AsSpan(14);
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip[2..], (ushort)(20 + 8 + payloadLength));
        ip[8] = 64;
        ip[9] = 17;
        source.CopyTo(ip[12..]);
        destination.CopyTo(ip[16..]);
        var udp = ip[20..];
        BinaryPrimitives.WriteUInt16BigEndian(udp, (ushort)sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[2..], (ushort)destinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)(8 + payloadLength));
        return frame;
    }

    /// <summary>pcapng-Format: Section Header, Interface Description, Enhanced Packet Blocks.</summary>
    public static byte[] CreatePcapng()
    {
        using var ms = new MemoryStream();

        // Section Header Block (28 Bytes)
        WriteUInt32(ms, 0x0A0D0D0A);
        WriteUInt32(ms, 28);
        WriteUInt32(ms, 0x1A2B3C4D);
        WriteUInt16(ms, 1);
        WriteUInt16(ms, 0);
        WriteUInt64(ms, ulong.MaxValue); // Sektionslänge unbekannt (-1)
        WriteUInt32(ms, 28);

        // Interface Description Block (20 Bytes), Zeitauflösung Standard = Mikrosekunden
        WriteUInt32(ms, 1);
        WriteUInt32(ms, 20);
        WriteUInt16(ms, (ushort)LinkTypeEthernet);
        WriteUInt16(ms, 0);
        WriteUInt32(ms, SnapLength);
        WriteUInt32(ms, 20);

        foreach (var (offset, length) in Packets)
        {
            var padded = (length + 3) & ~3;
            var blockLength = (uint)(32 + padded);
            var micros = (ulong)ToUnixMicroseconds(T0 + offset);

            WriteUInt32(ms, 6); // Enhanced Packet Block
            WriteUInt32(ms, blockLength);
            WriteUInt32(ms, 0); // Interface-Id
            WriteUInt32(ms, (uint)(micros >> 32));
            WriteUInt32(ms, (uint)micros);
            WriteUInt32(ms, (uint)length);
            WriteUInt32(ms, (uint)length);
            ms.Write(Payload(length));
            ms.Write(new byte[padded - length]);
            WriteUInt32(ms, blockLength);
        }

        return ms.ToArray();
    }

    /// <summary>Schreibt die Bytes in eine neue Datei im Temp-Ordner und gibt den Pfad zurück.</summary>
    public static string WriteTempFile(byte[] content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aion2dps-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static long ToUnixMicroseconds(DateTimeOffset time) =>
        (time - DateTimeOffset.UnixEpoch).Ticks / 10;

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)i;
        return bytes;
    }

    private static void WriteUInt16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteUInt32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteUInt64(Stream s, ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        s.Write(b);
    }
}
