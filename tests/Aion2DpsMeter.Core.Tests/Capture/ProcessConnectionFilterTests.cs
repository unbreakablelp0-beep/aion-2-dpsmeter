using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

/// <summary>Tests mit ausgedachten Verbindungen (keine echten Prozesse).</summary>
public class ProcessConnectionFilterTests
{
    private static ProcessConnection Tcp(int pid, int localPort, string remote = "198.51.100.7", int remotePort = 443) =>
        new(pid, TransportProtocol.Tcp, "192.0.2.10", localPort, remote, remotePort);

    private static ProcessConnection Udp(int pid, int localPort) =>
        new(pid, TransportProtocol.Udp, "0.0.0.0", localPort);

    [Fact]
    public void BuildFilter_CombinesTcpAndUdpPortsSortedAndDistinct()
    {
        var filter = ProcessConnectionFilter.BuildFilter(
            [Tcp(10, 50002), Udp(10, 60000), Tcp(10, 50001), Tcp(10, 50001)]);

        Assert.Equal("(tcp port 50001) or (tcp port 50002) or (udp port 60000)", filter);
    }

    [Fact]
    public void BuildFilter_SameNumberDifferentProtocol_KeepsBoth()
    {
        Assert.Equal("(tcp port 7000) or (udp port 7000)",
            ProcessConnectionFilter.BuildFilter([Udp(1, 7000), Tcp(1, 7000)]));
    }

    [Fact]
    public void BuildFilter_NoUsablePorts_ReturnsNull()
    {
        Assert.Null(ProcessConnectionFilter.BuildFilter([]));
        Assert.Null(ProcessConnectionFilter.BuildFilter([Udp(1, 0)]));
    }

    [Fact]
    public void GroupByProcess_GroupsSortsAndSkipsPidZero()
    {
        var names = new Dictionary<int, string> { [20] = "zeta", [10] = "Alpha" };

        var groups = ProcessConnectionFilter.GroupByProcess(
            [Tcp(20, 1), Tcp(10, 2), Udp(10, 3), Tcp(0, 4), Tcp(30, 5)],
            pid => names.GetValueOrDefault(pid));

        Assert.Equal([10, 30, 20], groups.Select(g => g.ProcessId));
        Assert.Equal("Alpha (PID 10) – 2 Verbindungen", groups[0].DisplayName);
        Assert.Equal("Unbekannt (PID 30) – 1 Verbindung", groups[1].DisplayName);
    }

    [Fact]
    public void CreateRecordingPath_UsesTimestampAndPcapExtension()
    {
        var path = ProcessConnectionFilter.CreateRecordingPath(@"C:\Aufnahmen",
            new DateTimeOffset(2026, 10, 12, 1, 15, 0, TimeSpan.FromHours(2)));

        Assert.Equal(Path.Combine(@"C:\Aufnahmen", "aufnahme-20261012-011500.pcap"), path);
    }
}
