namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Momentaufnahme der Paketstatistik.
/// </summary>
/// <param name="PacketCount">Anzahl Pakete.</param>
/// <param name="ByteCount">Summe der Paketlängen in Bytes.</param>
/// <param name="FirstPacketAt">Zeit des ersten Pakets oder null.</param>
/// <param name="LastPacketAt">Zeit des letzten Pakets oder null.</param>
public sealed record PacketStatistics(long PacketCount, long ByteCount, DateTimeOffset? FirstPacketAt, DateTimeOffset? LastPacketAt)
{
    /// <summary>Leere Statistik.</summary>
    public static PacketStatistics Empty { get; } = new(0, 0, null, null);
}

/// <summary>
/// Zählt Pakete und Bytes. Threadsicher, weil Pakete auf einem Hintergrundthread ankommen.
/// </summary>
public sealed class PacketCounter
{
    private readonly object _lock = new();
    private long _packets;
    private long _bytes;
    private DateTimeOffset? _first;
    private DateTimeOffset? _last;

    /// <summary>Zählt ein Paket.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Bei negativer Länge.</exception>
    public void Add(CapturedPacket packet)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(packet.Length, nameof(packet));

        lock (_lock)
        {
            _packets++;
            _bytes += packet.Length;
            if (_first is null || packet.Timestamp < _first) _first = packet.Timestamp;
            if (_last is null || packet.Timestamp > _last) _last = packet.Timestamp;
        }
    }

    /// <summary>Setzt alle Zähler zurück.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _packets = 0;
            _bytes = 0;
            _first = null;
            _last = null;
        }
    }

    /// <summary>Liefert den aktuellen Stand.</summary>
    public PacketStatistics Snapshot()
    {
        lock (_lock)
        {
            return new PacketStatistics(_packets, _bytes, _first, _last);
        }
    }
}
