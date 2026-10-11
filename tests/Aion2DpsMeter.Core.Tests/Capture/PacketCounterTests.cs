using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

public class PacketCounterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewCounter_IsEmpty()
    {
        Assert.Equal(PacketStatistics.Empty, new PacketCounter().Snapshot());
    }

    [Fact]
    public void Add_CountsPacketsBytesAndTimeRange_EvenOutOfOrder()
    {
        var counter = new PacketCounter();
        counter.Add(new CapturedPacket(T0.AddSeconds(5), 100));
        counter.Add(new CapturedPacket(T0, 60));
        counter.Add(new CapturedPacket(T0.AddSeconds(2), 0));

        Assert.Equal(new PacketStatistics(3, 160, T0, T0.AddSeconds(5)), counter.Snapshot());
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var counter = new PacketCounter();
        counter.Add(new CapturedPacket(T0, 100));

        counter.Reset();

        Assert.Equal(PacketStatistics.Empty, counter.Snapshot());
    }

    [Fact]
    public void Add_NegativeLength_IsRejected()
    {
        var counter = new PacketCounter();

        Assert.Throws<ArgumentOutOfRangeException>(() => counter.Add(new CapturedPacket(T0, -1)));
        Assert.Equal(0, counter.Snapshot().PacketCount);
    }

    [Fact]
    public void Add_FromManyThreads_LosesNothing()
    {
        var counter = new PacketCounter();

        Parallel.For(0, 10_000, i => counter.Add(new CapturedPacket(T0.AddMilliseconds(i), 10)));

        var stats = counter.Snapshot();
        Assert.Equal(10_000, stats.PacketCount);
        Assert.Equal(100_000, stats.ByteCount);
        Assert.Equal(T0, stats.FirstPacketAt);
        Assert.Equal(T0.AddMilliseconds(9_999), stats.LastPacketAt);
    }
}
