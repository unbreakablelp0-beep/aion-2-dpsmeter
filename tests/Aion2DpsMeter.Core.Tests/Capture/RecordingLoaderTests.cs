using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

/// <summary>
/// Tests des RecordingLoader mit simulierter Dateiquelle (keine echten Dateien, kein Npcap).
/// </summary>
public class RecordingLoaderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeFileSource(Func<string, Action<CapturedPacket>, CancellationToken, Task> behavior)
        : IPacketFileSource
    {
        public Task ReadAsync(string path, Action<CapturedPacket> onPacket, CancellationToken cancellationToken) =>
            behavior(path, onPacket, cancellationToken);
    }

    [Fact]
    public async Task Load_Success_ReturnsStatisticsAndGermanMessage()
    {
        var loader = new RecordingLoader(new FakeFileSource((_, onPacket, _) =>
        {
            onPacket(new CapturedPacket(T0, 100));
            onPacket(new CapturedPacket(T0.AddSeconds(3), 50));
            return Task.CompletedTask;
        }));

        var result = await loader.LoadAsync("test.pcap");

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(new PacketStatistics(2, 150, T0, T0.AddSeconds(3)), result.Statistics);
        Assert.Equal("Aufzeichnung geladen: 2 Pakete, 150 Bytes", result.Message);
    }

    [Fact]
    public async Task Load_EmptyRecording_IsSuccessWithZeroPackets()
    {
        var loader = new RecordingLoader(new FakeFileSource((_, _, _) => Task.CompletedTask));

        var result = await loader.LoadAsync("leer.pcap");

        Assert.True(result.Success);
        Assert.Equal(PacketStatistics.Empty, result.Statistics);
    }

    [Theory]
    [InlineData(CaptureErrorKind.FileNotFound)]
    [InlineData(CaptureErrorKind.InvalidFileFormat)]
    [InlineData(CaptureErrorKind.NpcapNotInstalled)]
    public async Task Load_SourceError_IsReportedNotThrown(CaptureErrorKind kind)
    {
        var loader = new RecordingLoader(new FakeFileSource((_, _, _) =>
            Task.FromException(new CaptureException(kind, "Detail"))));

        var result = await loader.LoadAsync("x.pcap");

        Assert.False(result.Success);
        Assert.Equal(kind, result.Error);
        Assert.Equal(CaptureErrorMessages.Describe(kind), result.Message);
        Assert.Equal("Detail", result.Detail);
    }

    [Fact]
    public async Task Load_ErrorAfterSomePackets_KeepsPartialStatistics()
    {
        var loader = new RecordingLoader(new FakeFileSource((_, onPacket, _) =>
        {
            onPacket(new CapturedPacket(T0, 10));
            throw new CaptureException(CaptureErrorKind.InvalidFileFormat, "abgeschnitten");
        }));

        var result = await loader.LoadAsync("kaputt.pcap");

        Assert.Equal(CaptureErrorKind.InvalidFileFormat, result.Error);
        Assert.Equal(1, result.Statistics.PacketCount);
    }

    [Fact]
    public async Task Load_Cancelled_Throws()
    {
        var loader = new RecordingLoader(new FakeFileSource((_, _, token) => Task.FromCanceled(token)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.LoadAsync("x.pcap", cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Load_EmptyPath_IsRejected(string path)
    {
        var loader = new RecordingLoader(new FakeFileSource((_, _, _) => Task.CompletedTask));

        await Assert.ThrowsAsync<ArgumentException>(() => loader.LoadAsync(path));
    }
}
