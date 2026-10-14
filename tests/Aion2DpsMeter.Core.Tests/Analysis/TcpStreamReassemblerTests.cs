using System.Net;
using System.Text;
using Aion2DpsMeter.Core.Analysis;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>
/// Allgemeine TCP-Stromzusammensetzung mit synthetischen Segmenten (Text als Nutzdaten,
/// damit die Reihenfolge leicht nachprüfbar ist). Nichts Spielspezifisches.
/// </summary>
public class TcpStreamReassemblerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static ParsedPacket Seg(double at, uint seq, string text = "", TcpFlags flags = TcpFlags.Ack,
        bool reverse = false)
    {
        var raw = reverse
            ? Tcp(at, Server, 7777, Local, 50001, Encoding.ASCII.GetBytes(text), seq, flags)
            : Tcp(at, Local, 50001, Server, 7777, Encoding.ASCII.GetBytes(text), seq, flags);
        Assert.True(PacketHeaderParser.TryParse(raw, out var parsed));
        return parsed!;
    }

    private static readonly TcpStreamKey Forward =
        new(new Endpoint(IPAddress.Parse(Local), 50001), new Endpoint(IPAddress.Parse(Server), 7777));

    private sealed class Collector
    {
        public List<TcpStreamEvent> Events { get; } = [];

        public void Add(IEnumerable<TcpStreamEvent> events) => Events.AddRange(events);

        public string Text(TcpStreamKey? key = null) => string.Concat(Events.OfType<TcpStreamData>()
            .Where(d => key is null || d.Stream == key)
            .Select(d => Encoding.ASCII.GetString(d.Data.Span)));

        public List<TcpStreamGap> Gaps => Events.OfType<TcpStreamGap>().ToList();

        public List<TcpStreamClosed> Closed => Events.OfType<TcpStreamClosed>().ToList();
    }

    private static (TcpStreamReassembler R, Collector C) Create(long maxBuffered = 1024 * 1024) =>
        (new TcpStreamReassembler(Timeout, maxBuffered), new Collector());

    [Fact]
    public void InOrder_AfterSyn_IsDeliveredWithOffsets()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 1000, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 1001, "Hallo")));
        c.Add(r.Add(Seg(0.2, 1006, " Welt")));

        Assert.Equal("Hallo Welt", c.Text());
        Assert.Equal([0L, 5L], c.Events.OfType<TcpStreamData>().Select(d => d.Offset));
        var stats = r.GetStatistics(Forward)!;
        Assert.False(stats.StartedMidStream);
        Assert.Equal(10, stats.DeliveredBytes);
        Assert.Equal(2, stats.Segments);
    }

    [Fact]
    public void OutOfOrder_IsReordered()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 6, " Welt")));
        Assert.Empty(c.Events.OfType<TcpStreamData>());

        c.Add(r.Add(Seg(0.2, 1, "Hallo")));

        Assert.Equal("Hallo Welt", c.Text());
        Assert.Equal(1, r.GetStatistics(Forward)!.OutOfOrderSegments);
    }

    [Fact]
    public void ExactDuplicate_IsDeliveredOnce()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 1, "abc")));
        c.Add(r.Add(Seg(0.2, 1, "abc")));

        Assert.Equal("abc", c.Text());
        Assert.Equal(1, r.GetStatistics(Forward)!.DuplicateSegments);
    }

    [Fact]
    public void DuplicateWhileWaiting_IsCountedOnce()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 4, "def")));
        c.Add(r.Add(Seg(0.2, 4, "def")));
        c.Add(r.Add(Seg(0.3, 1, "abc")));

        Assert.Equal("abcdef", c.Text());
        Assert.Equal(1, r.GetStatistics(Forward)!.DuplicateSegments);
    }

    [Fact]
    public void PartialOverlap_IsTrimmed()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 1, "Hallo")));
        c.Add(r.Add(Seg(0.2, 4, "lo Welt")));

        Assert.Equal("Hallo Welt", c.Text());
        Assert.Equal(2, r.GetStatistics(Forward)!.OverlapBytes);
    }

    [Fact]
    public void WaitingSegmentOverlappingDeliveredData_IsTrimmed()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 5, "efgh")));   // wartet (1–4 fehlen)
        c.Add(r.Add(Seg(0.2, 1, "abcdef"))); // schließt die Lücke und überlappt „ef“

        Assert.Equal("abcdefgh", c.Text());
        Assert.Equal(2, r.GetStatistics(Forward)!.OverlapBytes);
    }

    [Fact]
    public void Gap_IsReportedOnlyAfterTimeout_ThenStreamContinues()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 1, "AAAA")));
        c.Add(r.Add(Seg(1.0, 9, "CCCC")));  // „BBBB“ (Seq 5–8) fehlt
        c.Add(r.Add(Seg(2.9, 13)));         // reine Bestätigung, 1,9 s später: noch keine Lücke
        Assert.Empty(c.Gaps);

        c.Add(r.Add(Seg(3.0, 13)));         // 2,0 s nach „CCCC“

        var gap = Assert.Single(c.Gaps);
        Assert.Equal(4, gap.Offset);
        Assert.Equal(4, gap.MissingBytes);
        Assert.Equal(TcpGapReason.Timeout, gap.Reason);
        Assert.Equal("AAAACCCC", c.Text());
        Assert.Equal(8, c.Events.OfType<TcpStreamData>().Last().Offset);
        Assert.Equal(4, r.GetStatistics(Forward)!.GapBytes);
    }

    [Fact]
    public void CheckTimeouts_ReportsGapWithoutFurtherPackets()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.5, 3, "xy"))); // Seq 1–2 fehlen

        c.Add(r.CheckTimeouts(T0.AddSeconds(1)));
        Assert.Empty(c.Gaps);
        c.Add(r.CheckTimeouts(T0.AddSeconds(2.5)));

        Assert.Equal(2, Assert.Single(c.Gaps).MissingBytes);
        Assert.Equal("xy", c.Text());
    }

    [Fact]
    public void LateDataAfterGap_IsDiscardedAsDuplicate()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 3, "cd")));
        c.Add(r.CheckTimeouts(T0.AddSeconds(5)));
        c.Add(r.Add(Seg(6, 1, "ab"))); // kommt zu spät

        Assert.Equal("cd", c.Text());
        Assert.Equal(1, r.GetStatistics(Forward)!.DuplicateSegments);
    }

    [Fact]
    public void BufferLimit_ForcesGap()
    {
        var (r, c) = Create(maxBuffered: 8);
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 11, "12345")));
        c.Add(r.Add(Seg(0.2, 16, "67890"))); // 10 Bytes warten > 8

        var gap = Assert.Single(c.Gaps);
        Assert.Equal(TcpGapReason.BufferLimit, gap.Reason);
        Assert.Equal(10, gap.MissingBytes);
        Assert.Equal("1234567890", c.Text());
    }

    [Fact]
    public void Flush_ReportsRemainingGapsAsEndOfRecording()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 4, "zz")));

        c.Add(r.Flush(T0.AddSeconds(0.2)));

        Assert.Equal(TcpGapReason.EndOfRecording, Assert.Single(c.Gaps).Reason);
        Assert.Equal("zz", c.Text());
    }

    [Fact]
    public void SequenceNumberWraparound_IsHandled()
    {
        var (r, c) = Create();
        const uint isn = 0xFFFF_FFF0;
        c.Add(r.Add(Seg(0, isn, flags: TcpFlags.Syn)));
        var first = isn + 1;                          // 0xFFFFFFF1
        var second = unchecked(first + 10u);           // 0xFFFFFFFB
        var third = unchecked(second + 10u);           // 0x00000005 – nach dem Überlauf

        c.Add(r.Add(Seg(0.1, third, "CCCCCCCCCC")));   // kommt zuerst
        c.Add(r.Add(Seg(0.2, first, "AAAAAAAAAA")));
        c.Add(r.Add(Seg(0.3, second, "BBBBBBBBBB")));

        Assert.Equal("AAAAAAAAAABBBBBBBBBBCCCCCCCCCC", c.Text());
        Assert.Equal([0L, 10L, 20L], c.Events.OfType<TcpStreamData>().Select(d => d.Offset));
        Assert.Empty(c.Gaps);
    }

    [Fact]
    public void WithoutSyn_StartsMidStream()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 5000, "mitten")));
        c.Add(r.Add(Seg(0.1, 5006, "drin")));

        Assert.Equal("mittendrin", c.Text());
        Assert.Equal(0, c.Events.OfType<TcpStreamData>().First().Offset);
        Assert.True(r.GetStatistics(Forward)!.StartedMidStream);
    }

    [Fact]
    public void Fin_ClosesAfterAllDataArrived()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 4, "def", TcpFlags.Ack | TcpFlags.Fin))); // FIN kommt vor „abc“
        Assert.Empty(c.Closed);

        c.Add(r.Add(Seg(0.2, 1, "abc")));

        var closed = Assert.Single(c.Closed);
        Assert.Equal(TcpCloseReason.Fin, closed.Reason);
        Assert.Equal("abcdef", c.Text());
        Assert.True(r.GetStatistics(Forward)!.Closed);
    }

    [Fact]
    public void Rst_ClosesAndDiscardsWaitingData()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 0, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0.1, 10, "wartet")));
        c.Add(r.Add(Seg(0.2, 1, flags: TcpFlags.Rst)));

        var closed = Assert.Single(c.Closed);
        Assert.Equal(TcpCloseReason.Reset, closed.Reason);
        Assert.Equal(6, closed.DiscardedBytes);
        Assert.Equal(string.Empty, c.Text());

        c.Add(r.Add(Seg(0.3, 1, "danach")));
        Assert.Equal(string.Empty, c.Text());
    }

    [Fact]
    public void Directions_AreIndependent()
    {
        var (r, c) = Create();
        c.Add(r.Add(Seg(0, 100, flags: TcpFlags.Syn)));
        c.Add(r.Add(Seg(0, 900, flags: TcpFlags.Syn | TcpFlags.Ack, reverse: true)));
        c.Add(r.Add(Seg(0.1, 101, "frage")));
        c.Add(r.Add(Seg(0.2, 901, "antwort", reverse: true)));

        var back = new TcpStreamKey(Forward.Destination, Forward.Source);
        Assert.Equal("frage", c.Text(Forward));
        Assert.Equal("antwort", c.Text(back));
        Assert.Equal(2, r.Streams.Count);
    }

    [Fact]
    public void UdpPacket_IsIgnored()
    {
        var (r, _) = Create();
        Assert.True(PacketHeaderParser.TryParse(Udp(0, Local, 1, Server, 2, [1, 2]), out var udp));

        Assert.Empty(r.Add(udp!));
        Assert.Empty(r.Streams);
    }

    [Fact]
    public void Parser_ReadsSequenceNumberAndFlags()
    {
        var packet = Seg(0, 0xDEADBEEF, "x", TcpFlags.Psh | TcpFlags.Ack);

        Assert.Equal(0xDEADBEEFu, packet.Tcp!.SequenceNumber);
        Assert.Equal(TcpFlags.Psh | TcpFlags.Ack, packet.Tcp.Flags);
    }
}

