using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Tests mit synthetischen Paketen – keine echten Spieldaten.</summary>
public class RecordingAnalyzerTests
{
    /// <summary>
    /// Lokal 192.0.2.10:50001 ↔ Server 198.51.100.7:7777 (TCP):
    /// 3 Pakete lokal → Server (10, 20, 30 B Nutzdaten), 2 Pakete Server → lokal (100, 0 B), über 4 s.
    /// Dazu 1 UDP-Paket und 1 ARP-Paket (nicht auswertbar).
    /// </summary>
    private static RecordingAnalysis StandardRecording(bool withLocalAddress)
    {
        var analyzer = new RecordingAnalyzer(withLocalAddress ? [Local, "fe80::1%5"] : null);
        analyzer.Add(Tcp(0, Local, 50001, Server, 7777, new byte[10]));
        analyzer.Add(Tcp(1, Server, 7777, Local, 50001, new byte[100]));
        analyzer.Add(Tcp(2, Local, 50001, Server, 7777, new byte[20]));
        analyzer.Add(Tcp(3, Server, 7777, Local, 50001, []));
        analyzer.Add(Tcp(4, Local, 50001, Server, 7777, new byte[30]));
        analyzer.Add(Udp(5, Local, 60000, "198.51.100.8", 53, new byte[5]));
        analyzer.Add(Ethernet(6, 0x0806, new byte[28]));
        return analyzer.GetResult();
    }

    [Fact]
    public void CountsTotalsUnparsedAndTimeRange()
    {
        var result = StandardRecording(withLocalAddress: true);

        Assert.Equal(7, result.TotalPackets);
        Assert.Equal(1, result.UnparsedPackets);
        Assert.Equal(T0, result.First);
        Assert.Equal(T0.AddSeconds(6), result.Last);
        Assert.Equal(2, result.Conversations.Count);
    }

    [Fact]
    public void WithLocalAddress_DirectionsAreLocalToServer()
    {
        var tcp = StandardRecording(withLocalAddress: true).Conversations[0];

        Assert.Equal(TransportProtocol.Tcp, tcp.Protocol);
        Assert.Equal($"{Local}:50001", tcp.A.ToString());
        Assert.Equal($"{Server}:7777", tcp.B.ToString());
        Assert.True(tcp.AIsLocal);
        Assert.Equal(3, tcp.AToB.Packets);
        Assert.Equal(60, tcp.AToB.PayloadBytes);
        Assert.Equal(3 * 54 + 60, tcp.AToB.WireBytes); // je 14 + 20 + 20 Byte Kopf
        Assert.Equal(2, tcp.BToA.Packets);
        Assert.Equal(100, tcp.BToA.PayloadBytes);
        Assert.Equal(TimeSpan.FromSeconds(4), tcp.Duration);
        Assert.Equal(5 / 4.0, tcp.PacketsPerSecond);
    }

    [Fact]
    public void WithoutLocalAddress_DirectionIsUnknownButCountsMatch()
    {
        var tcp = StandardRecording(withLocalAddress: false).Conversations[0];

        Assert.Null(tcp.AIsLocal);
        Assert.Equal(5, tcp.TotalPackets);
        Assert.Equal(160, tcp.AToB.PayloadBytes + tcp.BToA.PayloadBytes);
    }

    [Fact]
    public void ConversationsAreSortedByBytesDescending()
    {
        var result = StandardRecording(withLocalAddress: true);

        Assert.True(result.Conversations[0].TotalWireBytes >= result.Conversations[1].TotalWireBytes);
        Assert.Equal(TransportProtocol.Udp, result.Conversations[1].Protocol);
    }

    [Fact]
    public void PayloadSizeBuckets_AreCounted()
    {
        var analyzer = new RecordingAnalyzer();
        foreach (var size in new[] { 0, 1, 63, 64, 255, 256, 511, 512, 1023, 1024, 1499, 1500, 1460 })
        {
            analyzer.Add(Udp(0, Local, 1, Server, 2, new byte[size]));
        }

        var buckets = analyzer.GetResult().Conversations[0].PayloadSizes.ToDictionary(b => b.Label, b => b.Count);

        Assert.Equal(1, buckets["0 B"]);
        Assert.Equal(2, buckets["1–63 B"]);
        Assert.Equal(2, buckets["64–255 B"]);
        Assert.Equal(2, buckets["256–511 B"]);
        Assert.Equal(2, buckets["512–1023 B"]);
        Assert.Equal(3, buckets["1024–1499 B"]);
        Assert.Equal(1, buckets["≥ 1500 B"]);
    }

    [Fact]
    public void Entropy_RandomPayload_LooksEncryptedOrCompressed()
    {
        var analyzer = new RecordingAnalyzer();
        for (var i = 0; i < 8; i++) analyzer.Add(Udp(i, Local, 1, Server, 2, RandomBytes(512, seed: i)));

        var entropy = analyzer.GetResult().Conversations[0].Entropy;

        Assert.Equal(EntropyAssessment.LikelyEncryptedOrCompressed, entropy.Assessment);
        Assert.True(entropy.BitsPerByte > 7.9);
        Assert.Equal(4096, entropy.SampleBytes);
    }

    [Fact]
    public void Entropy_TextPayload_LooksPlain()
    {
        var analyzer = new RecordingAnalyzer();
        analyzer.Add(Tcp(0, Local, 1, Server, 2, TextBytes(2048)));

        var entropy = analyzer.GetResult().Conversations[0].Entropy;

        Assert.Equal(EntropyAssessment.LikelyPlain, entropy.Assessment);
        Assert.True(entropy.BitsPerByte < 4);
    }

    [Fact]
    public void Entropy_TooLittleData_IsMarked()
    {
        var analyzer = new RecordingAnalyzer();
        analyzer.Add(Tcp(0, Local, 1, Server, 2, RandomBytes(1023)));

        var entropy = analyzer.GetResult().Conversations[0].Entropy;

        Assert.Equal(EntropyAssessment.TooLittleData, entropy.Assessment);
        Assert.Null(entropy.BitsPerByte);
    }

    [Fact]
    public void Entropy_FromHistogram_KnownValues()
    {
        var uniform = Enumerable.Repeat(4L, 256).ToArray(); // 1024 Bytes, jede Bytezahl gleich oft
        Assert.Equal(8.0, EntropyEstimate.FromHistogram(uniform).BitsPerByte!.Value, precision: 10);

        var twoValues = new long[256];
        twoValues[0] = 512;
        twoValues[1] = 512;
        var estimate = EntropyEstimate.FromHistogram(twoValues);
        Assert.Equal(1.0, estimate.BitsPerByte!.Value, precision: 10);
        Assert.Equal(EntropyAssessment.LikelyPlain, estimate.Assessment);

        Assert.Throws<ArgumentException>(() => EntropyEstimate.FromHistogram(new long[10]));
    }

    [Fact]
    public void Empty_HasNoConversations()
    {
        var result = new RecordingAnalyzer().GetResult();

        Assert.Equal(0, result.TotalPackets);
        Assert.Null(result.First);
        Assert.Empty(result.Conversations);
    }

    [Fact]
    public void Report_ContainsKeyFactsInGerman()
    {
        var text = RecordingReport.Format(StandardRecording(withLocalAddress: true), "test.pcap");

        Assert.Contains("Auswertung: test.pcap", text);
        Assert.Contains("Pakete gesamt:        7", text);
        Assert.Contains("Nicht auswertbar:     1", text);
        Assert.Contains($"TCP  {Local}:50001 (lokal)  ↔  {Server}:7777 (Gegenstelle)", text);
        Assert.Contains("lokal → Gegenstelle: 3 Pakete", text);
        Assert.Contains("Gegenstelle → lokal: 2 Pakete", text);
        Assert.Contains("Das Spielprotokoll wird nicht gedeutet", text);
        Assert.Contains("zu wenig Nutzdaten", text);
    }

    [Fact]
    public void Report_LimitsListedConversations()
    {
        var analyzer = new RecordingAnalyzer();
        for (var port = 1; port <= 5; port++) analyzer.Add(Udp(0, Local, port, Server, 9, new byte[port]));

        var text = RecordingReport.Format(analyzer.GetResult(), "x", maxConversations: 2);

        Assert.Contains("[2]", text);
        Assert.DoesNotContain("[3]", text);
        Assert.Contains("… 3 weitere", text);
    }

    [Fact]
    public async Task AnalyzeAsync_ReadsFromSourceAndPassesErrorsOn()
    {
        var ok = new FakeRawSource((_, onPacket, _) =>
        {
            onPacket(Tcp(0, Local, 1, Server, 2, [1]));
            return Task.CompletedTask;
        });
        var result = await RecordingReport.AnalyzeAsync(ok, "a.pcap", [Local]);
        Assert.Equal(1, result.TotalPackets);
        Assert.True(result.Conversations[0].AIsLocal);

        var failing = new FakeRawSource((_, _, _) =>
            Task.FromException(new CaptureException(CaptureErrorKind.InvalidFileFormat)));
        var ex = await Assert.ThrowsAsync<CaptureException>(() => RecordingReport.AnalyzeAsync(failing, "b.pcap"));
        Assert.Equal(CaptureErrorKind.InvalidFileFormat, ex.Kind);
    }

    private sealed class FakeRawSource(Func<string, Action<RawPacket>, CancellationToken, Task> behavior) : IRawPacketFileSource
    {
        public Task ReadRawAsync(string path, Action<RawPacket> onPacket, CancellationToken cancellationToken) =>
            behavior(path, onPacket, cancellationToken);
    }
}
