using System.Buffers.Binary;
using System.Net;
using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>
/// Synthetische Ströme, die nach den beschriebenen Community-Angaben gebaut sind – keine echten Spieldaten.
/// Damit wird nur geprüft, dass die Prüfung die beschriebenen Regeln richtig zählt.
/// </summary>
public class StreamHypothesisCheckTests
{
    private static readonly TcpStreamKey Key = new(
        new Endpoint(IPAddress.Parse(Server), 7777), new Endpoint(IPAddress.Parse(Local), 50001));

    [Fact]
    public void ConfirmsAllHypothesesOnStreamBuiltByTheRules()
    {
        var stream = ValidStream(heartbeats: 50, plainFrames: 30, bundles: 5, out var frames, out var innerFrames);

        var result = Feed(new StreamHypothesisCheck(), Split(stream, seed: 1));

        Assert.Equal(stream.Length, result.StreamBytes);
        Assert.Equal(50, result.HeartbeatHits);
        Assert.Equal(50, result.HeartbeatsAtFrameStart);
        Assert.Equal(frames, result.Frames);
        Assert.Equal(stream.Length, result.FramedBytes);
        Assert.Equal(0, result.Resyncs);
        Assert.Equal(5, result.BundleCandidates);
        Assert.Equal(5, result.BundlesDecoded);
        Assert.Equal(5, result.BundlesComplete);
        Assert.Equal(innerFrames, result.InnerFrames);
        Assert.Equal(HypothesisVerdict.Confirmed, result.Heartbeat);
        Assert.Equal(HypothesisVerdict.Confirmed, result.Framing);
        Assert.Equal(HypothesisVerdict.Confirmed, result.Lz4);
    }

    [Fact]
    public void RandomBytesConfirmNothing()
    {
        var result = Feed(new StreamHypothesisCheck(), Split(RandomBytes(64 * 1024, seed: 3), seed: 2));

        Assert.Equal(0, result.AllHeartbeatHits);
        Assert.Equal(HypothesisVerdict.NotConfirmed, result.Heartbeat);
        // Zufallsdaten gehen hier zu über 50 % scheinbar in Rahmen auf – deshalb braucht „bestätigt“ die Heartbeats.
        Assert.True(result.FramedShare > 0.5, $"Abdeckung {result.FramedShare}");
        Assert.Equal(HypothesisVerdict.Unclear, result.Framing);
        Assert.NotEqual(HypothesisVerdict.Confirmed, result.Lz4);
    }

    [Fact]
    public void MidStreamWithoutHeartbeatIsUnclearNotRejected()
    {
        var result = Feed(new StreamHypothesisCheck(startsAligned: false), Split(RandomBytes(32 * 1024, seed: 11), seed: 12));

        Assert.False(result.StartedAligned);
        Assert.False(result.HadSyncPoint);
        Assert.Equal(0, result.Frames);
        Assert.Equal(HypothesisVerdict.Unclear, result.Framing);
        Assert.Equal(HypothesisVerdict.NotConfirmed, result.Heartbeat);
    }

    [Fact]
    public void HighCoverageWithoutHeartbeatsIsOnlyUnclear()
    {
        var parts = new List<byte>();
        for (var i = 0; i < 99; i++) parts.AddRange(Frame([0x01, 0x23, 1, 2, 3, 4, 5, 6, 7, 8, 9]));

        var result = Feed(new StreamHypothesisCheck(), [parts.ToArray()]);

        Assert.Equal(1.0, result.FramedShare);
        Assert.Equal(HypothesisVerdict.Unclear, result.Framing); // nur 99 Rahmen
    }

    [Fact]
    public void UnbrokenChainFromConnectionStartIsConfirmedWithoutHeartbeats()
    {
        var parts = new List<byte>();
        for (var i = 0; i < 100; i++) parts.AddRange(Frame([0x01, 0x23, 1, 2, 3, 4, 5, 6, 7, 8, 9]));
        var stream = parts.ToArray();

        var aligned = Feed(new StreamHypothesisCheck(startsAligned: true), Split(stream, seed: 31));
        var broken = Feed(new StreamHypothesisCheck(startsAligned: true), [[.. stream, 0x02, .. stream]]); // ein ungültiger Kopf

        Assert.Equal(0, aligned.AllHeartbeatHits);
        Assert.Equal(HypothesisVerdict.Confirmed, aligned.Framing);
        Assert.Equal(1, broken.Resyncs);
        Assert.NotEqual(HypothesisVerdict.Confirmed, broken.Framing);
    }

    [Fact]
    public void RandomBytesFromConnectionStartNeverReachLongUnbrokenChain()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var result = Feed(new StreamHypothesisCheck(startsAligned: true), [RandomBytes(16 * 1024, seed)]);
            Assert.NotEqual(HypothesisVerdict.Confirmed, result.Framing);
        }
    }

    [Fact]
    public void HeartbeatsOffFrameStartsAreNotConfirmed()
    {
        var parts = new List<byte>();
        for (var i = 0; i < 150; i++) parts.AddRange(Frame([0x01, 0x23, 0x0E, 0x00, 0x36, 1, 2, 3])); // Muster im Rahmeninneren

        var result = Feed(new StreamHypothesisCheck(), [parts.ToArray()]);

        Assert.Equal(150, result.HeartbeatHits);
        Assert.Equal(0, result.HeartbeatsAtFrameStart);
        Assert.Equal(HypothesisVerdict.NotConfirmed, result.Framing);
    }

    [Fact]
    public void MidStreamStartSearchesFirstHeartbeat()
    {
        var valid = ValidStream(heartbeats: 40, plainFrames: 20, bundles: 0, out var frames, out _);
        var stream = new byte[] { 0x99, 0x42, 0x13 }.Concat(valid).ToArray();

        var result = Feed(new StreamHypothesisCheck(startsAligned: false), Split(stream, seed: 4));

        Assert.Equal(frames, result.Frames);
        Assert.Equal(valid.Length, result.FramedBytes);
        Assert.Equal(40, result.HeartbeatsAtFrameStart);
        Assert.Equal(HypothesisVerdict.Confirmed, result.Framing);
    }

    [Fact]
    public void ResyncsAtHeartbeatAfterGap()
    {
        var first = ValidStream(heartbeats: 20, plainFrames: 10, bundles: 0, out var firstFrames, out _);
        var second = ValidStream(heartbeats: 20, plainFrames: 10, bundles: 0, out var secondFrames, out _);
        var check = new StreamHypothesisCheck();

        Feed(check, Split(first, seed: 5));
        check.Add(new TcpStreamGap(Key, T0, first.Length, 7, TcpGapReason.Timeout));
        // Nach der Lücke beginnt der Strom mitten in einem Rahmen (5 Bytes eines normalen Rahmens).
        var afterGap = new byte[] { 0x07, 0x01, 0x02, 0x03, 0x04 }.Concat(second).ToArray();
        var result = Feed(check, Split(afterGap, seed: 6), offset: first.Length + 7);

        Assert.Equal(1, result.Gaps);
        Assert.Equal(firstFrames + secondFrames, result.Frames);
        Assert.Equal(first.Length + second.Length, result.FramedBytes);
        Assert.Equal(40, result.HeartbeatsAtFrameStart);
    }

    [Fact]
    public void InvalidHeaderTriggersResync()
    {
        var valid = ValidStream(heartbeats: 30, plainFrames: 10, bundles: 0, out var frames, out _);
        var stream = Frame([0x00, 0x10, 1, 2]).Concat(new byte[] { 0x02 }).Concat(valid).ToArray(); // 0x02 < 6: ungültig

        var result = Feed(new StreamHypothesisCheck(), [stream]);

        Assert.Equal(1, result.Resyncs);
        Assert.Equal(frames + 1, result.Frames);
    }

    [Fact]
    public void CountsHeartbeatSplitAcrossSegments()
    {
        var check = new StreamHypothesisCheck();
        var heartbeat = HeartbeatFrame();

        var result = Feed(check, [heartbeat[..1], heartbeat[1..2], heartbeat[2..]]);

        Assert.Equal(1, result.HeartbeatHits);
        Assert.Equal(1, result.HeartbeatsAtFrameStart);
        Assert.Equal(1, result.Frames);
    }

    [Fact]
    public void CountsShortHeartbeatForm()
    {
        var result = Feed(new StreamHypothesisCheck(), [[0x06, 0x00, 0x36, 0x06, 0x00, 0x36]]);

        Assert.Equal(2, result.ShortHeartbeatHits);
        Assert.Equal(2, result.HeartbeatsAtFrameStart);
        Assert.Equal(2, result.Frames);
    }

    [Fact]
    public void SkipsOptionalMarkerByteAndFindsBundleBehindIt()
    {
        var inner = Frame([0x01, 0x23, 9, 9, 9]);
        var bundle = BundleFrame(inner, marker: 0xF5);
        var result = Feed(new StreamHypothesisCheck(), [bundle]);

        Assert.Equal(1, result.Frames);
        Assert.Equal(1, result.BundleCandidates);
        Assert.Equal(1, result.BundlesComplete);
        Assert.Equal(1, result.InnerFrames);
    }

    [Fact]
    public void BrokenLz4BundlesAreNotConfirmed()
    {
        var parts = new List<byte>();
        for (var i = 0; i < 4; i++)
        {
            var bundle = BundleFrame(Frame([0x01, 0x23, 1, 2, 3, 4]));
            bundle[^8] = 0x0F; // LZ4-Token verfälscht: keine Literale, Rückverweis vor den Anfang
            parts.AddRange(bundle);
            parts.AddRange(HeartbeatFrame());
        }

        parts.AddRange(ValidStream(heartbeats: 60, plainFrames: 20, bundles: 0, out _, out _));
        var result = Feed(new StreamHypothesisCheck(), [parts.ToArray()]);

        Assert.Equal(4, result.BundleCandidates);
        Assert.Equal(0, result.BundlesComplete);
        Assert.Equal(HypothesisVerdict.NotConfirmed, result.Lz4);
    }

    [Fact]
    public void BundleWithWrongDeclaredSizeIsNotDecoded()
    {
        var bundle = BundleFrame(Frame([0x01, 0x23, 1, 2, 3]));
        BinaryPrimitives.WriteUInt32LittleEndian(bundle.AsSpan(3), 99); // nach Varint (1 Byte) und FF FF

        var result = Feed(new StreamHypothesisCheck(), [bundle]);

        Assert.Equal(1, result.BundleCandidates);
        Assert.Equal(0, result.BundlesDecoded);
    }

    [Fact]
    public void NestedBundleIsWalked()
    {
        var nested = BundleFrame(Frame([0x01, 0x23, 7, 7]));
        var outer = BundleFrame([.. Frame([0x02, 0x34, 1]), 0x00, 0x00, .. nested]);

        var result = Feed(new StreamHypothesisCheck(), [outer]);

        Assert.Equal(1, result.BundlesComplete);
        Assert.Equal(3, result.InnerFrames); // äußerer Rahmen, verschachteltes Bündel, dessen Inhalt
    }

    [Fact]
    public void CountsMessageTypesInPlainFramesAndBundles()
    {
        byte[] Typed(byte hi, byte lo, int extra) => Frame([hi, lo, .. new byte[extra]]);
        var bundleContent = Typed(0x04, 0x38, 6).Concat(Typed(0x04, 0x38, 6)).Concat(HeartbeatFrame()).ToArray();
        var stream = Typed(0x04, 0x38, 8) // 11 B
            .Concat(Typed(0x04, 0x38, 10)) // 13 B
            .Concat(Typed(0x01, 0x23, 3))
            .Concat(Typed(0x04, 0x38, 8)) // 11 B
            .Concat(Typed(0x01, 0x23, 3))
            .Concat(BundleFrame(bundleContent)) // 2 × 9 B + Heartbeat
            .ToArray();

        var result = Feed(new StreamHypothesisCheck(), Split(stream, seed: 21));

        var damage = Assert.IsType<MessageTypeStatistics>(result.FindMessageType(0x0438));
        Assert.Equal("04 38", damage.Hex);
        Assert.Equal(5, damage.Count);
        Assert.Equal(2, damage.InBundles);
        Assert.Equal(9, damage.MinSize);
        Assert.Equal(13, damage.MaxSize);
        Assert.Equal(3, damage.DistinctSizes);
        Assert.Equal(9, damage.MostCommonSize); // 9 und 11 je zweimal → kleinere
        Assert.Equal((11 + 13 + 11 + 9 + 9) / 5.0, damage.AverageSize);
        Assert.Equal(2, result.FindMessageType(0x0123)!.Count);
        Assert.Equal(1, result.FindMessageType(0x0036)!.InBundles);
        Assert.Null(result.FindMessageType(0xFFFF)); // Bündel-Rahmen selbst zählen nicht
        Assert.Equal(0x0438, result.MessageTypes![0].Type); // absteigend nach Anzahl
    }

    [Fact]
    public void ReadsMessageTypeBehindMarkerByte()
    {
        var result = Feed(new StreamHypothesisCheck(), [Frame([0xF5, 0x04, 0x38, 1, 2])]);

        Assert.Equal(1, result.FindMessageType(0x0438)?.Count);
    }

    [Fact]
    public void BrokenBundleContentIsNotCounted()
    {
        var bundle = BundleFrame(Frame([0x04, 0x38, 1, 2, 3, 4]));
        bundle[^8] = 0x0F; // LZ4-Token verfälscht

        var result = Feed(new StreamHypothesisCheck(), [bundle]);

        Assert.Empty(result.MessageTypes!);
    }

    [Fact]
    public void TooLittleData()
    {
        var result = Feed(new StreamHypothesisCheck(), [HeartbeatFrame()]);

        Assert.Equal(HypothesisVerdict.TooLittleData, result.Heartbeat);
        Assert.Equal(HypothesisVerdict.TooLittleData, result.Framing);
        Assert.Equal(HypothesisVerdict.TooLittleData, result.Lz4);
    }

    [Fact]
    public void IncompleteLastFrameIsNotCounted()
    {
        var frame = Frame(new byte[20]);
        var result = Feed(new StreamHypothesisCheck(), [[.. HeartbeatFrame(), .. frame[..10]]]);

        Assert.Equal(1, result.Frames);
        Assert.Equal(11, result.FramedBytes);
    }

    [Fact]
    public void AnalyzerReportsHypothesesPerTcpDirection()
    {
        var stream = ValidStream(heartbeats: 50, plainFrames: 30, bundles: 3, out _, out _);
        var analyzer = new RecordingAnalyzer([Local]);
        const uint serverIsn = 1000;
        analyzer.Add(Tcp(0, Server, 7777, Local, 50001, [], serverIsn, TcpFlags.Syn | TcpFlags.Ack));
        var seq = serverIsn + 1;
        var t = 0.1;
        foreach (var segment in Split(stream, seed: 9))
        {
            analyzer.Add(Tcp(t, Server, 7777, Local, 50001, segment, seq));
            seq += (uint)segment.Length;
            t += 0.05;
        }

        analyzer.Add(Tcp(t, Local, 50001, Server, 7777, RandomBytes(2000)));
        analyzer.Add(Udp(t, Local, 60000, Server, 53, new byte[5]));
        var result = analyzer.GetResult();

        var tcp = result.Conversations.Single(c => c.Protocol == TransportProtocol.Tcp);
        Assert.NotNull(tcp.BToAHypothesis);
        Assert.Equal(HypothesisVerdict.Confirmed, tcp.BToAHypothesis.Framing);
        Assert.Equal(HypothesisVerdict.Confirmed, tcp.BToAHypothesis.Lz4);
        Assert.Equal(2000, tcp.AToBHypothesis!.StreamBytes);
        Assert.Null(result.Conversations.Single(c => c.Protocol == TransportProtocol.Udp).AToBHypothesis);

        var text = RecordingReport.Format(result, "test.pcap");
        Assert.Contains("Hypothesen-Prüfung Gegenstelle → lokal", text);
        Assert.Contains("Heartbeat 0E 00 36: 50", text);
        Assert.Contains("Varint-Rahmen: 100,0 %", text);
        Assert.Contains("Hypothese bestätigt", text);
        Assert.Contains("Nachrichtentypen (2 Bytes nach der Länge; nur gezählt, nicht gedeutet)", text);
        Assert.Contains("00 36: 50, ", text);
        Assert.Contains("Typ 04 38 (Community-Hypothese „Schaden“, nicht gesichert, nicht gedeutet): kommt nicht vor", text);
        // Client-Richtung: Zufallsdaten ohne bestätigte Rahmen → keine Typenliste
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Nachrichtentypen"));
        Assert.DoesNotContain("nicht wie ein Typfeld", text);
    }

    [Fact]
    public void ReportWarnsWhenTypeBytesAreMostlyDistinct()
    {
        var analyzer = new RecordingAnalyzer([Local]);
        const uint isn = 500;
        analyzer.Add(Tcp(0, Local, 50001, Server, 7777, [], isn, TcpFlags.Syn));
        var seq = isn + 1;
        for (var i = 0; i < 150; i++)
        {
            var frame = Frame([(byte)(i >> 8), (byte)i, 1, 2, 3, 4, 5, 6, 7, 8]); // jedes Mal ein anderer Wert
            analyzer.Add(Tcp(0.01 * (i + 1), Local, 50001, Server, 7777, frame, seq));
            seq += (uint)frame.Length;
        }

        var text = RecordingReport.Format(analyzer.GetResult(), "test.pcap");

        Assert.Contains("150 verschiedene, 150 Rahmen", text);
        Assert.Contains("nicht wie ein Typfeld", text);
    }

    // --- Hilfen -----------------------------------------------------------------------------------

    private static StreamHypothesisResult Feed(StreamHypothesisCheck check, IEnumerable<byte[]> segments, long offset = 0)
    {
        foreach (var segment in segments)
        {
            check.Add(new TcpStreamData(Key, T0.AddSeconds(offset / 1000.0), offset, segment));
            offset += segment.Length;
        }

        return check.GetResult();
    }

    /// <summary>Rahmen nach der beschriebenen Regel: Varint-Wert = Länge nach dem Varint + 4.</summary>
    private static byte[] Frame(byte[] body)
    {
        var bytes = new List<byte>();
        var value = (uint)body.Length + 4;
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        bytes.AddRange(body);
        return bytes.ToArray();
    }

    /// <summary>11-Byte-Heartbeat: 0E 00 36 + 8 Bytes.</summary>
    private static byte[] HeartbeatFrame() => Frame([0x00, 0x36, 1, 2, 3, 4, 5, 6, 7, 8]);

    private static byte[] BundleFrame(byte[] content, byte? marker = null)
    {
        var body = new List<byte>();
        if (marker is { } m) body.Add(m);
        body.AddRange([0xFF, 0xFF]);
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)content.Length);
        body.AddRange(size);
        body.AddRange(Lz4BlockDecoderTests.Lz4Literals(content));
        return Frame(body.ToArray());
    }

    private static byte[] ValidStream(int heartbeats, int plainFrames, int bundles, out long frames, out long innerFrames)
    {
        var random = new Random(heartbeats * 31 + plainFrames * 7 + bundles);
        var stream = new List<byte>();
        frames = 0;
        innerFrames = 0;

        var total = heartbeats + plainFrames + bundles;
        for (var i = 0; i < total; i++)
        {
            if (i < heartbeats)
            {
                stream.AddRange(HeartbeatFrame());
            }
            else if (i < heartbeats + plainFrames)
            {
                var body = new byte[2 + random.Next(0, i % 3 == 0 ? 300 : 40)]; // auch 2-Byte-Varints
                random.NextBytes(body);
                body[0] = 0x01; // kein FF FF, kein Kennbyte
                stream.AddRange(Frame(body));
            }
            else
            {
                var content = new List<byte>();
                for (var k = 0; k < 4; k++)
                {
                    var body = new byte[2 + random.Next(0, 60)];
                    random.NextBytes(body);
                    body[0] = 0x02;
                    content.AddRange(Frame(body));
                    innerFrames++;
                }

                content.Add(0x00); // Füllbyte
                stream.AddRange(BundleFrame(content.ToArray()));
            }

            frames++;
        }

        return stream.ToArray();
    }

    /// <summary>Teilt Daten in zufällig große Segmente (1–200 Bytes), auch mitten im Varint.</summary>
    private static List<byte[]> Split(byte[] data, int seed)
    {
        var random = new Random(seed);
        var parts = new List<byte[]>();
        for (var pos = 0; pos < data.Length;)
        {
            var length = Math.Min(data.Length - pos, random.Next(1, 200));
            parts.Add(data[pos..(pos + length)]);
            pos += length;
        }

        return parts;
    }
}
