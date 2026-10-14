using System.Net;
using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Zähler für eine Richtung einer Verbindung.</summary>
/// <param name="Packets">Anzahl Pakete.</param>
/// <param name="WireBytes">Bytes auf der Leitung (inkl. aller Kopfzeilen).</param>
/// <param name="PayloadBytes">Nutzdaten-Bytes (ohne IP/TCP/UDP-Kopf).</param>
public sealed record DirectionSummary(long Packets, long WireBytes, long PayloadBytes);

/// <summary>Anzahl Pakete in einem Nutzdaten-Größenbereich.</summary>
public sealed record SizeBucket(string Label, long Count);

/// <summary>Grobe Einschätzung der Nutzdaten anhand der Byte-Entropie. Immer nur eine Schätzung.</summary>
public enum EntropyAssessment
{
    /// <summary>Zu wenige Nutzdaten für eine Aussage.</summary>
    TooLittleData,

    /// <summary>Niedrige Entropie – wirkt eher unverschlüsselt.</summary>
    LikelyPlain,

    /// <summary>Mittlere Entropie – keine klare Aussage möglich.</summary>
    Unclear,

    /// <summary>Hohe Entropie – wirkt verschlüsselt oder komprimiert.</summary>
    LikelyEncryptedOrCompressed,
}

/// <summary>Entropie-Schätzung der Nutzdaten.</summary>
/// <param name="BitsPerByte">Shannon-Entropie (0–8) oder null bei zu wenig Daten.</param>
/// <param name="SampleBytes">Anzahl ausgewerteter Nutzdaten-Bytes.</param>
/// <param name="Assessment">Einordnung.</param>
public sealed record EntropyEstimate(double? BitsPerByte, long SampleBytes, EntropyAssessment Assessment)
{
    /// <summary>Mindestmenge an Nutzdaten für eine Einschätzung.</summary>
    public const long MinimumSampleBytes = 1024;

    /// <summary>Berechnet die Schätzung aus einem Byte-Histogramm (256 Einträge).</summary>
    public static EntropyEstimate FromHistogram(IReadOnlyList<long> histogram)
    {
        ArgumentNullException.ThrowIfNull(histogram);
        if (histogram.Count != 256) throw new ArgumentException("Histogramm muss 256 Einträge haben.", nameof(histogram));

        var total = histogram.Sum();
        if (total < MinimumSampleBytes) return new EntropyEstimate(null, total, EntropyAssessment.TooLittleData);

        double entropy = 0;
        foreach (var count in histogram)
        {
            if (count == 0) continue;
            var p = (double)count / total;
            entropy -= p * Math.Log2(p);
        }

        var assessment = entropy switch
        {
            >= 7.5 => EntropyAssessment.LikelyEncryptedOrCompressed,
            < 6.0 => EntropyAssessment.LikelyPlain,
            _ => EntropyAssessment.Unclear,
        };
        return new EntropyEstimate(entropy, total, assessment);
    }
}

/// <summary>Auswertung einer Verbindung (TCP) bzw. eines Endpunktpaares (UDP).</summary>
/// <param name="Protocol">TCP oder UDP.</param>
/// <param name="A">Erster Endpunkt (wenn bekannt: der lokale).</param>
/// <param name="B">Zweiter Endpunkt (wenn bekannt: die Gegenstelle).</param>
/// <param name="AIsLocal">true, wenn A eine lokale Adresse ist; null, wenn das nicht bekannt ist.</param>
/// <param name="AToB">Pakete von A nach B.</param>
/// <param name="BToA">Pakete von B nach A.</param>
/// <param name="First">Erstes Paket.</param>
/// <param name="Last">Letztes Paket.</param>
/// <param name="PayloadSizes">Verteilung der Nutzdatengrößen.</param>
/// <param name="Entropy">Entropie-Schätzung über die Nutzdaten beider Richtungen.</param>
/// <param name="AToBHypothesis">Hypothesen-Prüfung des zusammengesetzten TCP-Stroms A → B (nur TCP mit Nutzdaten).</param>
/// <param name="BToAHypothesis">Hypothesen-Prüfung des zusammengesetzten TCP-Stroms B → A (nur TCP mit Nutzdaten).</param>
public sealed record ConversationSummary(
    TransportProtocol Protocol,
    Endpoint A,
    Endpoint B,
    bool? AIsLocal,
    DirectionSummary AToB,
    DirectionSummary BToA,
    DateTimeOffset First,
    DateTimeOffset Last,
    IReadOnlyList<SizeBucket> PayloadSizes,
    EntropyEstimate Entropy,
    StreamHypothesisResult? AToBHypothesis = null,
    StreamHypothesisResult? BToAHypothesis = null)
{
    public long TotalPackets => AToB.Packets + BToA.Packets;

    public long TotalWireBytes => AToB.WireBytes + BToA.WireBytes;

    public TimeSpan Duration => Last - First;

    /// <summary>Pakete pro Sekunde; null bei Dauer 0.</summary>
    public double? PacketsPerSecond => Duration.TotalSeconds > 0 ? TotalPackets / Duration.TotalSeconds : null;
}

/// <summary>Gesamtergebnis einer Aufzeichnungsauswertung.</summary>
/// <param name="TotalPackets">Alle Pakete der Datei.</param>
/// <param name="UnparsedPackets">Pakete, die nicht als IPv4/IPv6 + TCP/UDP gelesen werden konnten.</param>
/// <param name="First">Erstes Paket oder null.</param>
/// <param name="Last">Letztes Paket oder null.</param>
/// <param name="Conversations">Verbindungen, absteigend nach Bytes sortiert.</param>
public sealed record RecordingAnalysis(
    long TotalPackets,
    long UnparsedPackets,
    DateTimeOffset? First,
    DateTimeOffset? Last,
    IReadOnlyList<ConversationSummary> Conversations);

/// <summary>
/// Wertet Pakete einer Aufzeichnung aus: je Verbindung Pakete und Bytes pro Richtung,
/// Nutzdaten-Größenverteilung, Zeitraum, eine Entropie-Schätzung und je TCP-Richtung die
/// <see cref="StreamHypothesisCheck"/> (Community-Angaben als Hypothesen). Das Spielprotokoll
/// wird dabei nicht gedeutet.
/// </summary>
public sealed class RecordingAnalyzer
{
    /// <summary>Grenzen der Größenbereiche (Nutzdaten in Bytes, jeweils Untergrenze).</summary>
    private static readonly (int Min, string Label)[] Buckets =
    [
        (0, "0 B"),
        (1, "1–63 B"),
        (64, "64–255 B"),
        (256, "256–511 B"),
        (512, "512–1023 B"),
        (1024, "1024–1499 B"),
        (1500, "≥ 1500 B"),
    ];

    private readonly HashSet<IPAddress> _localAddresses;
    private readonly Dictionary<(TransportProtocol, Endpoint, Endpoint), Builder> _conversations = new();
    private long _total;
    private long _unparsed;
    private DateTimeOffset? _first;
    private DateTimeOffset? _last;
    private readonly TcpStreamReassembler _reassembler = new(TimeSpan.FromSeconds(2));
    private readonly Dictionary<TcpStreamKey, StreamHypothesisCheck> _checks = new();
    private bool _flushed;
    private readonly IReadOnlySet<ushort>? _observedTypes;
    private readonly Action<TcpStreamKey, ObservedFrame>? _onFrame;

    /// <param name="localAddresses">Eigene IP-Adressen (z. B. der Adapter), um „lokal“ zu erkennen; optional.</param>
    /// <param name="observedTypes">Optional: Nachrichtentypen, deren Rahmen an <paramref name="onFrame"/> gehen.</param>
    /// <param name="onFrame">Bekommt die beobachteten Rahmen mit Richtung.</param>
    public RecordingAnalyzer(
        IEnumerable<string>? localAddresses = null,
        IReadOnlySet<ushort>? observedTypes = null,
        Action<TcpStreamKey, ObservedFrame>? onFrame = null)
    {
        _observedTypes = observedTypes;
        _onFrame = onFrame;
        _localAddresses = (localAddresses ?? [])
            .Select(a => IPAddress.TryParse(a.Split('%')[0], out var ip) ? ip : null)
            .OfType<IPAddress>()
            .ToHashSet();
    }

    /// <summary>Wertet ein Paket aus.</summary>
    public void Add(RawPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        _total++;
        if (_first is null || packet.Timestamp < _first) _first = packet.Timestamp;
        if (_last is null || packet.Timestamp > _last) _last = packet.Timestamp;

        if (!PacketHeaderParser.TryParse(packet, out var parsed) || parsed is null)
        {
            _unparsed++;
            return;
        }

        var (a, b) = Order(parsed.Source, parsed.Destination);
        var key = (parsed.Protocol, a, b);
        if (!_conversations.TryGetValue(key, out var builder))
        {
            builder = new Builder(parsed.Protocol, a, b, KnownLocal(a, b));
            _conversations[key] = builder;
        }

        builder.Add(parsed, fromA: parsed.Source == a);

        if (parsed.Tcp is not null && !_flushed)
        {
            foreach (var streamEvent in _reassembler.Add(parsed)) Check(streamEvent);
        }
    }

    /// <summary>Liefert das Ergebnis. Danach werden keine TCP-Ströme mehr geprüft.</summary>
    public RecordingAnalysis GetResult()
    {
        if (!_flushed && _last is { } end)
        {
            foreach (var streamEvent in _reassembler.Flush(end)) Check(streamEvent);
        }

        _flushed = true;
        return new(
            _total,
            _unparsed,
            _first,
            _last,
            _conversations.Values
                .Select(b => b.Build(Hypothesis(b.A, b.B), Hypothesis(b.B, b.A)))
                .OrderByDescending(c => c.TotalWireBytes)
                .ThenBy(c => c.A.ToString(), StringComparer.Ordinal)
                .ToList());
    }

    private void Check(TcpStreamEvent streamEvent)
    {
        if (streamEvent is TcpStreamClosed) return;
        if (!_checks.TryGetValue(streamEvent.Stream, out var check))
        {
            var startedMidStream = _reassembler.GetStatistics(streamEvent.Stream)?.StartedMidStream ?? true;
            var key = streamEvent.Stream;
            var observer = _observedTypes is { } types && _onFrame is { } onFrame
                ? new FrameObserver(types, frame => onFrame(key, frame))
                : null;
            check = new StreamHypothesisCheck(startsAligned: !startedMidStream, observer);
            _checks[streamEvent.Stream] = check;
        }

        check.Add(streamEvent);
    }

    private StreamHypothesisResult? Hypothesis(Endpoint source, Endpoint destination) =>
        _checks.TryGetValue(new TcpStreamKey(source, destination), out var check) ? check.GetResult() : null;

    private (Endpoint A, Endpoint B) Order(Endpoint x, Endpoint y)
    {
        var xLocal = _localAddresses.Contains(x.Address);
        var yLocal = _localAddresses.Contains(y.Address);
        if (xLocal != yLocal) return xLocal ? (x, y) : (y, x);
        return string.CompareOrdinal(x.ToString(), y.ToString()) <= 0 ? (x, y) : (y, x);
    }

    private bool? KnownLocal(Endpoint a, Endpoint b)
    {
        var aLocal = _localAddresses.Contains(a.Address);
        var bLocal = _localAddresses.Contains(b.Address);
        return aLocal != bLocal ? aLocal : null;
    }

    private sealed class Builder(TransportProtocol protocol, Endpoint a, Endpoint b, bool? aIsLocal)
    {
        public Endpoint A => a;

        public Endpoint B => b;

        private readonly long[] _histogram = new long[256];
        private readonly long[] _sizes = new long[Buckets.Length];
        private long _aPackets, _aWire, _aPayload, _bPackets, _bWire, _bPayload;
        private DateTimeOffset _first = DateTimeOffset.MaxValue;
        private DateTimeOffset _last = DateTimeOffset.MinValue;

        public void Add(ParsedPacket packet, bool fromA)
        {
            var payload = packet.Payload.Span;
            if (fromA)
            {
                _aPackets++;
                _aWire += packet.WireLength;
                _aPayload += payload.Length;
            }
            else
            {
                _bPackets++;
                _bWire += packet.WireLength;
                _bPayload += payload.Length;
            }

            if (packet.Timestamp < _first) _first = packet.Timestamp;
            if (packet.Timestamp > _last) _last = packet.Timestamp;

            var bucket = Buckets.Length - 1;
            while (payload.Length < Buckets[bucket].Min) bucket--;
            _sizes[bucket]++;

            foreach (var value in payload) _histogram[value]++;
        }

        public ConversationSummary Build(StreamHypothesisResult? aToB, StreamHypothesisResult? bToA) => new(
            protocol,
            a,
            b,
            aIsLocal,
            new DirectionSummary(_aPackets, _aWire, _aPayload),
            new DirectionSummary(_bPackets, _bWire, _bPayload),
            _first,
            _last,
            Buckets.Select((bk, i) => new SizeBucket(bk.Label, _sizes[i])).ToList(),
            EntropyEstimate.FromHistogram(_histogram),
            protocol == TransportProtocol.Tcp ? aToB : null,
            protocol == TransportProtocol.Tcp ? bToA : null);
    }
}
