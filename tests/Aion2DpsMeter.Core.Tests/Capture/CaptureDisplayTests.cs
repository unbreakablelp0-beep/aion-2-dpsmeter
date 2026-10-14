using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

public class CaptureDisplayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static NetworkAdapterInfo Adapter(string name, bool loopback, params string[] addresses) =>
        new($@"\Device\NPF_{{{name}}}", name, loopback, addresses);

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(2_359_296, "2,25 MB")]
    [InlineData(1_073_741_824, "1 GB")]
    public void FormatBytes_UsesGermanFormatAndBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, CaptureDisplay.FormatBytes(bytes));
    }

    [Fact]
    public void FormatBytes_Negative_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureDisplay.FormatBytes(-1));
    }

    [Fact]
    public void FormatCount_UsesGermanThousandsSeparator()
    {
        Assert.Equal("1.234.567", CaptureDisplay.FormatCount(1_234_567));
    }

    [Fact]
    public void FormatDuration_AndRate_FromFirstToLastPacket()
    {
        var stats = new PacketStatistics(150, 9000, T0, T0.AddSeconds(75));

        Assert.Equal("01:15", CaptureDisplay.FormatDuration(stats));
        Assert.Equal("2 Pakete/s", CaptureDisplay.FormatPacketRate(stats));
    }

    [Fact]
    public void FormatDuration_OverOneHour_ShowsHours()
    {
        var stats = new PacketStatistics(1, 1, T0, T0.AddSeconds(3725));

        Assert.Equal("1:02:05", CaptureDisplay.FormatDuration(stats));
    }

    [Fact]
    public void Empty_HasZeroDurationAndNoRate()
    {
        Assert.Equal("00:00", CaptureDisplay.FormatDuration(PacketStatistics.Empty));
        Assert.Equal("–", CaptureDisplay.FormatPacketRate(PacketStatistics.Empty));
    }

    [Fact]
    public void FormatPacketRate_UsesGermanDecimalComma()
    {
        var stats = new PacketStatistics(5, 500, T0, T0.AddSeconds(2));

        Assert.Equal("2,5 Pakete/s", CaptureDisplay.FormatPacketRate(stats));
    }

    [Fact]
    public void DescribeAdapter_ShowsIPv4AndMarksLoopback()
    {
        Assert.Equal("Ethernet – 192.0.2.5",
            CaptureDisplay.DescribeAdapter(Adapter("Ethernet", false, "fe80::1", "192.0.2.5")));
        Assert.Equal("Lo (Loopback) – 127.0.0.1",
            CaptureDisplay.DescribeAdapter(Adapter("Lo", true, "::1", "127.0.0.1")));
        Assert.Equal("WAN Miniport", CaptureDisplay.DescribeAdapter(Adapter("WAN Miniport", false)));
    }

    [Fact]
    public void SuggestAdapter_PrefersNonLoopbackWithRoutableIPv4()
    {
        var wan = Adapter("WAN", false);
        var linkLocal = Adapter("WLAN getrennt", false, "169.254.1.2");
        var loopback = Adapter("Loopback", true, "127.0.0.1");
        var ethernet = Adapter("Ethernet", false, "192.168.0.20");

        Assert.Same(ethernet, CaptureDisplay.SuggestAdapter([wan, linkLocal, loopback, ethernet]));
    }

    [Fact]
    public void SuggestAdapter_FallsBackToNonLoopbackThenFirst()
    {
        var wan = Adapter("WAN", false);
        var loopback = Adapter("Loopback", true, "127.0.0.1");

        Assert.Same(wan, CaptureDisplay.SuggestAdapter([loopback, wan]));
        Assert.Same(loopback, CaptureDisplay.SuggestAdapter([loopback]));
        Assert.Null(CaptureDisplay.SuggestAdapter([]));
    }
}
