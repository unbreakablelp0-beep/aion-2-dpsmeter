namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Eine Richtung einer TCP-Verbindung (Quelle → Ziel).</summary>
public readonly record struct TcpStreamKey(Endpoint Source, Endpoint Destination)
{
    public override string ToString() => $"{Source} → {Destination}";
}

/// <summary>Ereignis der Stromzusammensetzung.</summary>
public abstract record TcpStreamEvent(TcpStreamKey Stream, DateTimeOffset Time);

/// <summary>Lückenlos zusammengesetzte Daten ab <paramref name="Offset"/> (Bytes seit Strombeginn).</summary>
public sealed record TcpStreamData(TcpStreamKey Stream, DateTimeOffset Time, long Offset, ReadOnlyMemory<byte> Data)
    : TcpStreamEvent(Stream, Time);

/// <summary>Warum eine Lücke gemeldet wurde.</summary>
public enum TcpGapReason
{
    /// <summary>Fehlende Daten kamen nicht innerhalb des Zeitlimits.</summary>
    Timeout,

    /// <summary>Zu viele Daten warteten hinter der Lücke.</summary>
    BufferLimit,

    /// <summary>Die Aufzeichnung endete, bevor die Lücke geschlossen wurde.</summary>
    EndOfRecording,
}

/// <summary>
/// Fehlende Bytes ab <paramref name="Offset"/>. Danach geht der Strom mit späteren Daten weiter;
/// die fehlenden Bytes sind verloren und werden nie geliefert.
/// </summary>
public sealed record TcpStreamGap(TcpStreamKey Stream, DateTimeOffset Time, long Offset, long MissingBytes, TcpGapReason Reason)
    : TcpStreamEvent(Stream, Time);

/// <summary>Wie eine Richtung beendet wurde.</summary>
public enum TcpCloseReason
{
    /// <summary>Reguläres Ende (FIN), alle Daten davor sind angekommen.</summary>
    Fin,

    /// <summary>Abbruch (RST); noch wartende Daten werden verworfen.</summary>
    Reset,
}

/// <summary>Die Richtung wurde beendet.</summary>
public sealed record TcpStreamClosed(TcpStreamKey Stream, DateTimeOffset Time, TcpCloseReason Reason, long DiscardedBytes)
    : TcpStreamEvent(Stream, Time);

/// <summary>Zähler einer Richtung.</summary>
/// <param name="DeliveredBytes">Gelieferte Bytes.</param>
/// <param name="Segments">Segmente mit Nutzdaten.</param>
/// <param name="OutOfOrderSegments">Segmente, die vor fehlenden Daten ankamen und warten mussten.</param>
/// <param name="DuplicateSegments">Vollständig doppelte Segmente (z. B. Wiederholungen).</param>
/// <param name="OverlapBytes">Teilweise doppelte Bytes, die abgeschnitten wurden.</param>
/// <param name="Gaps">Gemeldete Lücken.</param>
/// <param name="GapBytes">Insgesamt fehlende Bytes.</param>
/// <param name="StartedMidStream">true, wenn kein SYN gesehen wurde (Aufnahme begann mitten in der Verbindung).</param>
/// <param name="Closed">true nach FIN oder RST.</param>
public sealed record TcpStreamStatistics(
    long DeliveredBytes,
    int Segments,
    int OutOfOrderSegments,
    int DuplicateSegments,
    long OverlapBytes,
    int Gaps,
    long GapBytes,
    bool StartedMidStream,
    bool Closed);

/// <summary>
/// Setzt TCP-Segmente je Verbindung und Richtung in die richtige Reihenfolge (allgemeines TCP nach
/// RFC 9293, nichts Spielspezifisches). Erkennt Duplikate und Überlappungen, meldet Lücken nach einem
/// Zeitlimit und kommt mit dem Überlauf der 32-Bit-Sequenznummer zurecht.
/// </summary>
/// <remarks>
/// Für passiv mitgeschnittene Daten gedacht: Es wird nichts bestätigt oder gesendet. Überlappungen
/// werden nach „zuerst angekommen gilt“ aufgelöst. Nicht threadsicher.
/// </remarks>
public sealed class TcpStreamReassembler
{
    private readonly TimeSpan _gapTimeout;
    private readonly long _maxBufferedBytes;
    private readonly Dictionary<TcpStreamKey, Direction> _directions = new();

    /// <param name="gapTimeout">So lange wird auf fehlende Daten gewartet, bevor eine Lücke gemeldet wird.</param>
    /// <param name="maxBufferedBytesPerStream">Höchstens so viele Bytes warten hinter einer Lücke.</param>
    public TcpStreamReassembler(TimeSpan gapTimeout, long maxBufferedBytesPerStream = 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gapTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBufferedBytesPerStream, 1);
        _gapTimeout = gapTimeout;
        _maxBufferedBytes = maxBufferedBytesPerStream;
    }

    /// <summary>Alle bisher gesehenen Richtungen.</summary>
    public IReadOnlyCollection<TcpStreamKey> Streams => _directions.Keys;

    /// <summary>Verarbeitet ein Paket. Nicht-TCP-Pakete werden ignoriert.</summary>
    public IReadOnlyList<TcpStreamEvent> Add(ParsedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Tcp is not { } tcp) return [];

        var key = new TcpStreamKey(packet.Source, packet.Destination);
        if (!_directions.TryGetValue(key, out var dir))
        {
            dir = new Direction(key);
            _directions[key] = dir;
        }

        var events = new List<TcpStreamEvent>();
        if (dir.Closed) return events;

        var time = packet.Timestamp;
        var seq = tcp.SequenceNumber;
        var payload = packet.Payload;

        if (tcp.Flags.HasFlag(TcpFlags.Rst))
        {
            var discarded = dir.BufferedBytes;
            dir.Pending.Clear();
            dir.BufferedBytes = 0;
            dir.Closed = true;
            events.Add(new TcpStreamClosed(key, time, TcpCloseReason.Reset, discarded));
            return events;
        }

        if (tcp.Flags.HasFlag(TcpFlags.Syn))
        {
            if (!dir.Initialized)
            {
                dir.NextSeq = unchecked(seq + 1);
                dir.Initialized = true;
            }

            seq = unchecked(seq + 1); // SYN belegt eine Sequenznummer
        }

        if (!dir.Initialized)
        {
            // Aufnahme begann mitten in der Verbindung: ab hier zählen.
            dir.NextSeq = seq;
            dir.Initialized = true;
            dir.StartedMidStream = true;
        }

        if (!payload.IsEmpty)
        {
            dir.Segments++;
            Insert(dir, seq, payload, time, events);
        }

        if (tcp.Flags.HasFlag(TcpFlags.Fin))
        {
            dir.FinOffset = dir.Offset + unchecked((int)(seq - dir.NextSeq)) + payload.Length;
        }

        CheckTimeout(dir, time, events);
        TryCloseWithFin(dir, time, events);
        return events;
    }

    /// <summary>
    /// Meldet für alle Richtungen Lücken, deren Zeitlimit bis <paramref name="now"/> abgelaufen ist.
    /// </summary>
    public IReadOnlyList<TcpStreamEvent> CheckTimeouts(DateTimeOffset now)
    {
        var events = new List<TcpStreamEvent>();
        foreach (var dir in _directions.Values)
        {
            CheckTimeout(dir, now, events);
            TryCloseWithFin(dir, now, events);
        }

        return events;
    }

    /// <summary>Ende der Aufzeichnung: alle verbleibenden Lücken melden und wartende Daten liefern.</summary>
    public IReadOnlyList<TcpStreamEvent> Flush(DateTimeOffset now)
    {
        var events = new List<TcpStreamEvent>();
        foreach (var dir in _directions.Values)
        {
            while (dir.Pending.Count > 0) SkipGap(dir, TcpGapReason.EndOfRecording, now, events);
            TryCloseWithFin(dir, now, events);
        }

        return events;
    }

    /// <summary>Zähler einer Richtung oder null, wenn sie unbekannt ist.</summary>
    public TcpStreamStatistics? GetStatistics(TcpStreamKey key) =>
        _directions.TryGetValue(key, out var d)
            ? new TcpStreamStatistics(d.Delivered, d.Segments, d.OutOfOrder, d.Duplicates, d.OverlapBytes,
                d.Gaps, d.GapBytes, d.StartedMidStream, d.Closed)
            : null;

    private void Insert(Direction dir, uint seq, ReadOnlyMemory<byte> data, DateTimeOffset time, List<TcpStreamEvent> events)
    {
        // Vorzeichenbehafteter Abstand: funktioniert auch über den 32-Bit-Überlauf hinweg.
        var diff = unchecked((int)(seq - dir.NextSeq));
        var offset = dir.Offset + diff;

        if (diff <= 0)
        {
            Deliver(dir, offset, data, time, events);
            Drain(dir, events);
            return;
        }

        dir.OutOfOrder++;
        if (dir.Pending.TryGetValue(offset, out var existing))
        {
            if (existing.Data.Length >= data.Length)
            {
                dir.Duplicates++;
                return;
            }

            dir.BufferedBytes -= existing.Data.Length;
        }

        dir.Pending[offset] = new Pending(data, time);
        dir.BufferedBytes += data.Length;

        while (dir.BufferedBytes > _maxBufferedBytes && dir.Pending.Count > 0)
        {
            SkipGap(dir, TcpGapReason.BufferLimit, time, events);
        }
    }

    private static void Deliver(Direction dir, long offset, ReadOnlyMemory<byte> data, DateTimeOffset time, List<TcpStreamEvent> events)
    {
        var alreadyHave = dir.Offset - offset;
        if (alreadyHave >= data.Length)
        {
            dir.Duplicates++;
            return;
        }

        if (alreadyHave > 0)
        {
            dir.OverlapBytes += alreadyHave;
            data = data[(int)alreadyHave..];
        }

        events.Add(new TcpStreamData(dir.Key, time, dir.Offset, data));
        Advance(dir, data.Length);
        dir.Delivered += data.Length;
    }

    private static void Drain(Direction dir, List<TcpStreamEvent> events)
    {
        while (dir.Pending.Count > 0)
        {
            var (offset, pending) = dir.Pending.First();
            if (offset > dir.Offset) return;

            dir.Pending.Remove(offset);
            dir.BufferedBytes -= pending.Data.Length;
            Deliver(dir, offset, pending.Data, pending.Time, events);
        }
    }

    private void CheckTimeout(Direction dir, DateTimeOffset now, List<TcpStreamEvent> events)
    {
        while (dir.Pending.Count > 0 && now - dir.Pending.Values.Min(p => p.Time) >= _gapTimeout)
        {
            SkipGap(dir, TcpGapReason.Timeout, now, events);
        }
    }

    private static void SkipGap(Direction dir, TcpGapReason reason, DateTimeOffset time, List<TcpStreamEvent> events)
    {
        var next = dir.Pending.Keys.First();
        var missing = next - dir.Offset;
        if (missing > 0)
        {
            events.Add(new TcpStreamGap(dir.Key, time, dir.Offset, missing, reason));
            dir.Gaps++;
            dir.GapBytes += missing;
            Advance(dir, missing);
        }

        Drain(dir, events);
    }

    private static void TryCloseWithFin(Direction dir, DateTimeOffset time, List<TcpStreamEvent> events)
    {
        if (dir.Closed || dir.FinOffset is not { } fin || dir.Offset < fin || dir.Pending.Count > 0) return;

        dir.Closed = true;
        events.Add(new TcpStreamClosed(dir.Key, time, TcpCloseReason.Fin, 0));
    }

    private static void Advance(Direction dir, long bytes)
    {
        dir.Offset += bytes;
        dir.NextSeq = unchecked(dir.NextSeq + (uint)bytes);
    }

    private sealed record Pending(ReadOnlyMemory<byte> Data, DateTimeOffset Time);

    private sealed class Direction(TcpStreamKey key)
    {
        public TcpStreamKey Key { get; } = key;
        public bool Initialized;
        public bool StartedMidStream;
        public bool Closed;
        public uint NextSeq;
        public long Offset;
        public long? FinOffset;
        public readonly SortedDictionary<long, Pending> Pending = new();
        public long BufferedBytes;
        public long Delivered;
        public int Segments, OutOfOrder, Duplicates, Gaps;
        public long OverlapBytes, GapBytes;
    }
}
