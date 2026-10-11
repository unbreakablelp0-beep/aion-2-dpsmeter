using Aion2DpsMeter.Core.Capture;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Aion2DpsMeter.Capture.Npcap;

/// <summary>
/// Liest .pcap- und .pcapng-Dateien über SharpPcap/Npcap (libpcap). Nur lesend;
/// vom Paket werden nur Zeit und Länge weitergegeben.
/// </summary>
public sealed class PcapFileSource : IPacketFileSource
{
    /// <inheritdoc />
    public Task ReadAsync(string path, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onPacket);

        return Task.Run(() => Read(path, onPacket, cancellationToken), cancellationToken);
    }

    private static void Read(string path, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
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
                        onPacket(new CapturedPacket(
                            new DateTimeOffset(packet.Header.Timeval.Date, TimeSpan.Zero),
                            packet.Data.Length));
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
