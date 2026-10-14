using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Aion2DpsMeter.Capture.Npcap;

/// <summary>
/// Liest .pcap- und .pcapng-Dateien über SharpPcap/Npcap (libpcap). Nur lesend;
/// vom Paket werden nur Zeit und Länge weitergegeben.
/// </summary>
public sealed class PcapFileSource : IPacketFileSource, IRawPacketFileSource
{
    /// <inheritdoc />
    public Task ReadAsync(string path, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onPacket);

        return Task.Run(
            () => Read(path, (time, _, data, _) => onPacket(new CapturedPacket(time, data.Length)), cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ReadRawAsync(string path, Action<RawPacket> onPacket, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onPacket);

        return Task.Run(
            () => Read(path, (time, linkType, data, originalLength) =>
                onPacket(new RawPacket(time, linkType, data.ToArray(), originalLength)), cancellationToken),
            cancellationToken);
    }

    private delegate void PacketHandler(DateTimeOffset time, int linkType, ReadOnlySpan<byte> data, int originalLength);

    private static void Read(string path, PacketHandler onPacket, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new CaptureException(CaptureErrorKind.FileNotFound, path);
        }

        var device = new CaptureFileReaderDevice(path);
        try
        {
            device.Open();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new CaptureException(CaptureErrorKind.NpcapNotInstalled, ex.Message, ex);
        }
        catch (PcapException ex)
        {
            throw new CaptureException(CaptureErrorKind.InvalidFileFormat, ex.Message, ex);
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = device.GetNextPacket(out PacketCapture packet);
                switch (result)
                {
                    case GetPacketStatus.PacketRead:
                        var raw = packet.GetPacket();
                        onPacket(
                            new DateTimeOffset(packet.Header.Timeval.Date, TimeSpan.Zero),
                            (int)device.LinkType,
                            packet.Data,
                            raw.PacketLength);
                        break;
                    case GetPacketStatus.NoRemainingPackets:
                        return;
                    case GetPacketStatus.ReadTimeout:
                        break;
                    default:
                        throw new CaptureException(CaptureErrorKind.InvalidFileFormat, device.LastError);
                }
            }
        }
        finally
        {
            device.Close();
        }
    }
}
