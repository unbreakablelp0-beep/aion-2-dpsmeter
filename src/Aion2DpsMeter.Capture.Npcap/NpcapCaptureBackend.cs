using System.Net.Sockets;
using Aion2DpsMeter.Core.Capture;
using Microsoft.Win32;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Aion2DpsMeter.Capture.Npcap;

/// <summary>
/// Anbindung an Npcap Ã¼ber SharpPcap. Rein passiv: Adapter werden nur zum Lesen
/// geÃ¶ffnet (kein Promiscuous-Modus), es werden keine Pakete gesendet oder verÃ¤ndert,
/// und vom Paket werden nur Zeit und LÃ¤nge weitergegeben.
/// </summary>
public sealed class NpcapCaptureBackend : IPacketCaptureBackend
{
    private const int ReadTimeoutMilliseconds = 500;
    private const uint PcapIfLoopback = 0x1;

    private static string NpcapDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap");

    /// <inheritdoc />
    public CaptureBackendInfo GetInfo()
    {
        var wpcap = Path.Combine(NpcapDirectory, "wpcap.dll");
        if (!File.Exists(wpcap))
        {
            return new CaptureBackendInfo(false, null, CaptureErrorKind.NpcapNotInstalled, $"Datei fehlt: {wpcap}");
        }

        string version;
        try
        {
            version = Pcap.Version;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return new CaptureBackendInfo(false, null, CaptureErrorKind.NpcapNotInstalled, ex.Message);
        }

        if (IsRestrictedToAdministrators() && !IsAdministrator())
        {
            return new CaptureBackendInfo(false, version, CaptureErrorKind.AccessDenied,
                "Npcap ist mit â€žNur Administratorenâ€œ installiert, das Programm lÃ¤uft ohne Administratorrechte.");
        }

        return new CaptureBackendInfo(true, version);
    }

    /// <inheritdoc />
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        try
        {
            return LoadDevices().Select(ToAdapterInfo).ToList();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new CaptureException(CaptureErrorKind.NpcapNotInstalled, ex.Message, ex);
        }
        catch (PcapException ex)
        {
            throw new CaptureException(ClassifyPcapError(ex.Message, CaptureErrorKind.AdapterListFailed), ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public Task CaptureAsync(string adapterId, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapterId);
        ArgumentNullException.ThrowIfNull(onPacket);

        return Task.Factory.StartNew(
            () => CaptureLoop(adapterId, onPacket, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void CaptureLoop(string adapterId, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
    {
        LibPcapLiveDevice device;
        try
        {
            device = LoadDevices().FirstOrDefault(d => d.Name == adapterId)
                ?? throw new CaptureException(CaptureErrorKind.AdapterNotFound, adapterId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new CaptureException(CaptureErrorKind.NpcapNotInstalled, ex.Message, ex);
        }
        catch (PcapException ex)
        {
            throw new CaptureException(ClassifyPcapError(ex.Message, CaptureErrorKind.AdapterListFailed), ex.Message, ex);
        }

        try
        {
            device.Open(new DeviceConfiguration
            {
                Mode = DeviceModes.None,
                ReadTimeout = ReadTimeoutMilliseconds,
            });
        }
        catch (PcapException ex)
        {
            throw new CaptureException(ClassifyPcapError(ex.Message, CaptureErrorKind.AdapterOpenFailed), ex.Message, ex);
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = device.GetNextPacket(out PacketCapture packet);
                switch (result)
                {
                    case GetPacketStatus.PacketRead:
                        onPacket(new CapturedPacket(
                            new DateTimeOffset(packet.Header.Timeval.Date, TimeSpan.Zero),
                            packet.Data.Length));
                        break;
                    case GetPacketStatus.ReadTimeout:
                        break;
                    case GetPacketStatus.NoRemainingPackets:
                        return;
                    default:
                        throw new CaptureException(CaptureErrorKind.CaptureFailed, device.LastError);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            device.Close();
        }
    }

    private static IEnumerable<LibPcapLiveDevice> LoadDevices() =>
        LibPcapLiveDeviceList.New();

    private static NetworkAdapterInfo ToAdapterInfo(LibPcapLiveDevice device)
    {
        var addresses = device.Addresses
            .Select(a => a.Addr?.ipAddress)
            .Where(ip => ip is not null && ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Select(ip => ip!.ToString())
            .ToList();

        var isLoopback = ((device.Interface?.Flags ?? 0) & PcapIfLoopback) != 0;
        return new NetworkAdapterInfo(device.Name, device.Description ?? string.Empty, isLoopback, addresses);
    }

    private static CaptureErrorKind ClassifyPcapError(string message, CaptureErrorKind fallback) =>
        message.Contains("access", StringComparison.OrdinalIgnoreCase)
        || message.Contains("denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Zugriff", StringComparison.OrdinalIgnoreCase)
            ? CaptureErrorKind.AccessDenied
            : fallback;

    private static bool IsRestrictedToAdministrators()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npcap\Parameters");
        return key?.GetValue("AdminOnly") is int value && value != 0;
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
