using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Capture;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Capture;

/// <summary>Programmauswahl mit simulierter Verbindungstabelle und Uhr – keine echten Verbindungen.</summary>
public class ProcessPacketSelectorTests
{
    private const int GamePid = 4242;
    private const int OtherPid = 7;

    private readonly ManualTime _time = new();
    private readonly FakeConnections _table = new();

    private ProcessPacketSelector<string> CreateSelector() =>
        new(GamePid, _table, _time, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));

    private static RawPacket Out(int localPort, TcpFlags flags = TcpFlags.Ack) =>
        Tcp(0, Local, localPort, Server, 7777, [1, 2, 3], flags: flags);

    [Fact]
    public void KeepsKnownPortOnlyAfterHoldBack()
    {
        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50001, Server, 7777));
        var selector = CreateSelector();

        Assert.Empty(selector.Add(Out(50001), "a"));
        _time.Advance(TimeSpan.FromSeconds(1.9));
        Assert.Empty(selector.Poll());
        _time.Advance(TimeSpan.FromSeconds(0.1));

        Assert.Equal(new[] { "a" }, selector.Poll());
        Assert.Equal(1, selector.Kept);
    }

    [Fact]
    public void ConnectionOpenedAfterStartIsCapturedIncludingSyn()
    {
        var selector = CreateSelector();
        Assert.Empty(selector.Add(Out(50001), "vorher")); // Verbindung gibt es noch nicht – bleibt nach Ablauf verworfen

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Empty(selector.Poll());

        // Login: SYN geht hinaus, bevor die Verbindung in der Tabelle steht.
        Assert.Empty(selector.Add(Out(50002, TcpFlags.Syn), "syn"));
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50002, Server, 7777));
        Assert.Empty(selector.Add(Out(50002), "daten1"));
        _time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Empty(selector.Add(Out(50002), "daten2")); // Tabelle wird hier neu gelesen (600 ms seit letztem Lesen)

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "syn", "daten1", "daten2" }, selector.Poll());
        Assert.Equal(1, selector.Dropped);
        Assert.Equal(1, selector.KnownPorts);
    }

    [Fact]
    public void DropsOtherProgramsAndKeepsOrder()
    {
        _table.Set(
            new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50001, Server, 7777),
            new ProcessConnection(OtherPid, TransportProtocol.Tcp, Local, 60000, Server, 443));
        var selector = CreateSelector();

        selector.Add(Out(50001), "1");
        selector.Add(Out(60000), "fremd");
        selector.Add(Tcp(0, Server, 7777, Local, 50001, [9]), "2"); // eingehend: Zielport passt
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(new[] { "1", "2" }, selector.Poll());
        Assert.Equal(1, selector.Dropped);
    }

    [Fact]
    public void MatchesUdpSeparatelyFromTcp()
    {
        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Udp, Local, 50001));
        var selector = CreateSelector();

        selector.Add(Udp(0, Local, 50001, Server, 9000, [1]), "udp");
        selector.Add(Out(50001), "tcp mit gleichem Port");

        Assert.Equal(new[] { "udp" }, selector.Flush());
    }

    [Fact]
    public void ConnectionSeenTooLateIsDropped()
    {
        var selector = CreateSelector();
        selector.Add(Out(50001), "zu früh");
        _time.Advance(TimeSpan.FromSeconds(2.5));
        Assert.Empty(selector.Poll());

        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50001, Server, 7777));
        selector.Add(Out(50001), "danach");

        Assert.Equal(new[] { "danach" }, selector.Flush());
        Assert.Equal(1, selector.Dropped);
    }

    [Fact]
    public void FlushReadsTableOnceMoreAndReleasesEverything()
    {
        var selector = CreateSelector();
        selector.Add(Out(50001), "a");
        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50001, Server, 7777));

        Assert.Equal(new[] { "a" }, selector.Flush());
    }

    [Fact]
    public void ClosedConnectionStaysKnown()
    {
        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50001, Server, 7777));
        var selector = CreateSelector();
        selector.Poll();
        _table.Set();
        _time.Advance(TimeSpan.FromSeconds(1));

        selector.Add(Out(50001, TcpFlags.Fin | TcpFlags.Ack), "fin");

        Assert.Equal(new[] { "fin" }, selector.Flush());
    }

    [Fact]
    public void TableErrorDoesNotStopSelection()
    {
        _table.Set(new ProcessConnection(GamePid, TransportProtocol.Tcp, Local, 50001, Server, 7777));
        var selector = CreateSelector();
        selector.Poll();
        _table.Fail = true;
        _time.Advance(TimeSpan.FromSeconds(1));

        selector.Add(Out(50001), "a");

        Assert.Equal(new[] { "a" }, selector.Flush());
        Assert.Equal(2, selector.RefreshErrors);
    }

    [Fact]
    public void UnreadablePacketIsDropped()
    {
        var selector = CreateSelector();
        selector.Add(Ethernet(0, 0x0806, new byte[28]), "arp");

        Assert.Empty(selector.Flush());
        Assert.Equal(1, selector.Dropped);
    }

    [Fact]
    public void RejectsHoldBackNotLongerThanRefresh()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessPacketSelector<string>(GamePid, _table, _time, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = T0;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeConnections : IProcessConnectionSource
    {
        private ProcessConnection[] _connections = [];

        public bool Fail { get; set; }

        public void Set(params ProcessConnection[] connections) => _connections = connections;

        public IReadOnlyList<ProcessConnection> GetConnections() =>
            Fail ? throw new InvalidOperationException("Tabelle nicht lesbar") : _connections;
    }
}
