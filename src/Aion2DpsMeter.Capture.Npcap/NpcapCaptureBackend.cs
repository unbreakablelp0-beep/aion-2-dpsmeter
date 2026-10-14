using System.Net.Sockets;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using Microsoft.Win32;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Aion2DpsMeter.Capture.Npcap;

/// <summary>
/// Anbindung an Npcap über SharpPcap. Rein passiv: Adapter werden nur zum Lesen
/// geöffnet (kein Promiscuous-Modus), es werden keine Pakete gesendet oder verändert.
/// Weitergegeben werden Zeit und Länge; der Paketinhalt nur, wenn <see cref="CaptureOptions.OnRawPacket"/>
/// gesetzt ist (Live-Auswertung im Speicher) oder gespeichert werden soll.
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
                "Npcap ist mit „Nur Administratoren“ installiert, das Programm läuft ohne Administratorrechte.");
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
    public Task CaptureAsync(string adapterId, CaptureOptions options, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapterId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onPacket);

        return Task.Factory.StartNew(
            () => CaptureLoop(adapterId, options, onPacket, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void CaptureLoop(string adapterId, CaptureOptions options, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
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

        CaptureFileWriterDevice? writer = null;
        try
        {
            if (options.Filter is { } filter)
            {
                try
                {
                    device.Filter = filter;
                }
                catch (PcapException ex)
                {
                    throw new CaptureException(CaptureErrorKind.InvalidFilter, $"{filter}: {ex.Message}", ex);
                }
            }

            if (options.SaveFilePath is { } savePath)
            {
                writer = OpenWriter(savePath, device);
            }

            var selector = options.Process is { } process
                ? new ProcessPacketSelector<RawCapture>(process.ProcessId, process.Connections)
                : null;
            var linkType = (int)device.LinkType;
            var onRaw = options.OnRawPacket;

            void Emit(RawCapture raw)
            {
                writer?.Write(raw);
                var time = new DateTimeOffset(raw.Timeval.Date, TimeSpan.Zero);
                onPacket(new CapturedPacket(time, raw.Data.Length));
                onRaw?.Invoke(new RawPacket(time, linkType, raw.Data, raw.PacketLength));
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = device.GetNextPacket(out PacketCapture packet);
                    switch (result)
                    {
                        case GetPacketStatus.PacketRead:
                            var raw = packet.GetPacket();
                            if (selector is null)
                            {
                                Emit(raw);
                            }
                            else
                            {
                                var rawPacket = new RawPacket(
                                    new DateTimeOffset(raw.Timeval.Date, TimeSpan.Zero), linkType, raw.Data, raw.PacketLength);
                                foreach (var selected in selector.Add(rawPacket, raw)) Emit(selected);
                            }

                            break;
                        case GetPacketStatus.ReadTimeout:
                            if (selector is not null)
                            {
                                foreach (var selected in selector.Poll()) Emit(selected);
                            }

                            break;
                        case GetPacketStatus.NoRemainingPackets:
                            return;
                        default:
                            throw new CaptureException(CaptureErrorKind.CaptureFailed, device.LastError);
                    }
                }
            }
            finally
            {
                // Zurückgehaltene Pakete am Ende noch entscheiden und speichern.
                if (selector is not null)
                {
                    foreach (var selected in selector.Flush()) Emit(selected);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            writer?.Close();
            device.Close();
        }
    }

    /// <summary>
    /// Öffnet eine .pcap-Datei zum Schreiben. Schreibt nur auf die lokale Festplatte – es wird
    /// nichts ins Netzwerk gesendet.
    /// </summary>
    private static CaptureFileWriterDevice OpenWriter(string path, LibPcapLiveDevice device)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // libpcap überschreibt vorhandene Dateien ohne Rückfrage (FileMode wird nicht beachtet),
            // deshalb selbst prüfen.
            if (File.Exists(path))
            {
                throw new CaptureException(CaptureErrorKind.SaveFileFailed, $"{path}: Datei existiert bereits.");
            }

            var writer = new CaptureFileWriterDevice(path);
            writer.Open(device);
            return writer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PcapException or ArgumentException)
        {
            throw new CaptureException(CaptureErrorKind.SaveFileFailed, $"{path}: {ex.Message}", ex);
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
