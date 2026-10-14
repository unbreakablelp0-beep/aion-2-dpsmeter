using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

/// <summary>
/// Simulierte Aufnahme-Anbindung für Tests. Es wird kein echtes Netzwerk verwendet.
/// </summary>
internal sealed class FakeCaptureBackend : IPacketCaptureBackend
{
    public static readonly NetworkAdapterInfo Ethernet =
        new(@"\Device\NPF_{FAKE-ETH}", "Simulierter Ethernet-Adapter", false, ["192.0.2.10"]);

    public static readonly NetworkAdapterInfo Loopback =
        new(@"\Device\NPF_Loopback", "Simulierter Loopback-Adapter", true, ["127.0.0.1"]);

    public CaptureBackendInfo Info { get; set; } = new(true, "Fake 1.0");

    public List<NetworkAdapterInfo> AdapterList { get; } = [Ethernet, Loopback];

    public Exception? GetAdaptersException { get; set; }

    /// <summary>Verhalten der Aufnahme. Standard: wartet, bis abgebrochen wird.</summary>
    public Func<string, Action<CapturedPacket>, CancellationToken, Task> CaptureBehavior { get; set; } =
        (_, _, token) => Task.Delay(Timeout.Infinite, token);

    public int GetAdaptersCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public CaptureBackendInfo GetInfo() => Info;

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        GetAdaptersCalls++;
        if (GetAdaptersException is not null) throw GetAdaptersException;
        return AdapterList.ToList();
    }

    public CaptureOptions? LastOptions { get; private set; }

    public Task CaptureAsync(string adapterId, CaptureOptions options, Action<CapturedPacket> onPacket, CancellationToken cancellationToken)
    {
        CaptureCalls++;
        LastOptions = options;
        return CaptureBehavior(adapterId, onPacket, cancellationToken);
    }
}
