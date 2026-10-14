using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Capture.Npcap.Tests;

/// <summary>
/// Liest selbst erzeugte, synthetische Aufzeichnungen über das installierte Npcap (libpcap).
/// </summary>
[Trait("Category", "RequiresNpcap")]
public class PcapFileSourceTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files) File.Delete(f);
    }

    private string Temp(byte[] content, string extension)
    {
        var path = TestRecordings.WriteTempFile(content, extension);
        _files.Add(path);
        return path;
    }

    private static readonly PacketStatistics Expected = new(
        3, 60 + 1514 + 42, TestRecordings.T0, TestRecordings.T0.AddSeconds(2));

    [Fact]
    public async Task Load_Pcap_CountsAllPacketsWithCorrectTimes()
    {
        var result = await new RecordingLoader(new PcapFileSource()).LoadAsync(Temp(TestRecordings.CreatePcap(), ".pcap"));

        Assert.True(result.Success, result.Detail);
        Assert.Equal(Expected, result.Statistics);
        Assert.Equal("Aufzeichnung geladen: 3 Pakete, 1616 Bytes", result.Message);
    }

    [Fact]
    public async Task Load_Pcapng_CountsAllPacketsWithCorrectTimes()
    {
        var result = await new RecordingLoader(new PcapFileSource()).LoadAsync(Temp(TestRecordings.CreatePcapng(), ".pcapng"));

        Assert.True(result.Success, result.Detail);
        Assert.Equal(Expected, result.Statistics);
    }

    [Fact]
    public async Task Load_Pcap_DeliversPacketsInFileOrder()
    {
        var packets = new List<CapturedPacket>();

        await new PcapFileSource().ReadAsync(Temp(TestRecordings.CreatePcap(), ".pcap"), packets.Add, CancellationToken.None);

        Assert.Equal(
            TestRecordings.Packets.Select(p => new CapturedPacket(TestRecordings.T0 + p.Offset, p.Length)),
            packets);
    }

    [Fact]
    public async Task Analyze_PcapWithUdpFrames_GivesConversationPerDirection()
    {
        byte[] local = [192, 0, 2, 10], server = [198, 51, 100, 7];
        var path = Temp(TestRecordings.CreatePcap(
        [
            (TimeSpan.Zero, TestRecordings.UdpFrame(local, 50000, server, 7777, 10)),
            (TimeSpan.FromSeconds(1), TestRecordings.UdpFrame(server, 7777, local, 50000, 200)),
            (TimeSpan.FromSeconds(2), TestRecordings.UdpFrame(local, 50000, server, 7777, 30)),
        ]), ".pcap");

        var analysis = await Aion2DpsMeter.Core.Analysis.RecordingReport.AnalyzeAsync(new PcapFileSource(), path, ["192.0.2.10"]);

        Assert.Equal(3, analysis.TotalPackets);
        Assert.Equal(0, analysis.UnparsedPackets);
        var c = Assert.Single(analysis.Conversations);
        Assert.Equal("192.0.2.10:50000", c.A.ToString());
        Assert.Equal("198.51.100.7:7777", c.B.ToString());
        Assert.Equal(2, c.AToB.Packets);
        Assert.Equal(40, c.AToB.PayloadBytes);
        Assert.Equal(1, c.BToA.Packets);
        Assert.Equal(200, c.BToA.PayloadBytes);
        Assert.Equal(TimeSpan.FromSeconds(2), c.Duration);
    }

    [Fact]
    public async Task Load_MissingFile_IsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aion2dps-gibt-es-nicht-{Guid.NewGuid():N}.pcap");

        var result = await new RecordingLoader(new PcapFileSource()).LoadAsync(path);

        Assert.False(result.Success);
        Assert.Equal(CaptureErrorKind.FileNotFound, result.Error);
    }

    [Fact]
    public async Task Load_TextFile_IsInvalidFormat()
    {
        var path = Temp("Das ist keine Paketaufzeichnung."u8.ToArray(), ".pcap");

        var result = await new RecordingLoader(new PcapFileSource()).LoadAsync(path);

        Assert.False(result.Success);
        Assert.Equal(CaptureErrorKind.InvalidFileFormat, result.Error);
        Assert.Equal(0, result.Statistics.PacketCount);
    }

    [Fact]
    public async Task Load_AlreadyCancelled_Throws()
    {
        var path = Temp(TestRecordings.CreatePcap(), ".pcap");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RecordingLoader(new PcapFileSource()).LoadAsync(path, cts.Token));
    }
}
