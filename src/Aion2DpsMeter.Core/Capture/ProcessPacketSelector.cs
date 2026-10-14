using Aion2DpsMeter.Core.Analysis;

namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Nur Pakete dieses Programms aufnehmen. Die Verbindungen werden während der Aufnahme laufend neu
/// gelesen, damit auch Verbindungen erfasst werden, die erst nach dem Start entstehen.
/// </summary>
/// <param name="ProcessId">PID des gewählten Programms.</param>
/// <param name="Connections">Quelle der Verbindungstabelle (nur lesend).</param>
public sealed record ProcessSelection(int ProcessId, IProcessConnectionSource Connections);

/// <summary>
/// Wählt Pakete eines Programms aus, auch für Verbindungen, die erst während der Aufnahme entstehen.
/// </summary>
/// <remarks>
/// <para>
/// Jedes Paket wird <see cref="HoldBack"/> lang zurückgehalten. In dieser Zeit wird die Verbindungstabelle
/// alle <see cref="RefreshInterval"/> neu gelesen; danach wird entschieden. So bleiben auch die ersten
/// Pakete einer neuen Verbindung (SYN) erhalten, die gesendet werden, kurz bevor sie in der Tabelle steht.
/// </para>
/// <para>
/// Ein Paket gehört zum Programm, wenn sein Quell- oder Zielport (gleiches Protokoll) irgendwann während der
/// Aufnahme ein lokaler Port des Programms war – wie beim bisherigen Portfilter („port“ passt auf beide Seiten).
/// Grenzen: Verbindungen, die kürzer als ein Neulese-Abstand offen sind, können fehlen; nutzt später ein
/// anderes Programm denselben lokalen Port, werden dessen Pakete mit aufgenommen.
/// </para>
/// Die Reihenfolge der Pakete bleibt erhalten. Nicht threadsicher.
/// </remarks>
/// <typeparam name="T">Beliebige Begleitdaten je Paket (z. B. das Originalpaket zum Speichern).</typeparam>
public sealed class ProcessPacketSelector<T>
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultHoldBack = TimeSpan.FromSeconds(2);

    private readonly int _processId;
    private readonly IProcessConnectionSource _source;
    private readonly TimeProvider _time;
    private readonly Queue<(RawPacket Packet, T Item, DateTimeOffset Arrived)> _queue = new();
    private readonly HashSet<(TransportProtocol, int)> _ports = new();
    private DateTimeOffset? _lastRefresh;

    /// <param name="processId">PID des Programms.</param>
    /// <param name="source">Verbindungstabelle.</param>
    /// <param name="time">Uhr (für Tests austauschbar); Standard: Systemzeit.</param>
    /// <param name="refreshInterval">Abstand zwischen zwei Lesevorgängen der Verbindungstabelle.</param>
    /// <param name="holdBack">So lange wird jedes Paket zurückgehalten; muss größer als der Neulese-Abstand sein.</param>
    public ProcessPacketSelector(
        int processId,
        IProcessConnectionSource source,
        TimeProvider? time = null,
        TimeSpan? refreshInterval = null,
        TimeSpan? holdBack = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _processId = processId;
        _source = source;
        _time = time ?? TimeProvider.System;
        RefreshInterval = refreshInterval ?? DefaultRefreshInterval;
        HoldBack = holdBack ?? DefaultHoldBack;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RefreshInterval, TimeSpan.Zero, nameof(refreshInterval));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(HoldBack, RefreshInterval, nameof(holdBack));
    }

    public TimeSpan RefreshInterval { get; }

    public TimeSpan HoldBack { get; }

    /// <summary>Bisher bekannte lokale Ports des Programms.</summary>
    public int KnownPorts => _ports.Count;

    /// <summary>Behaltene Pakete.</summary>
    public long Kept { get; private set; }

    /// <summary>Verworfene Pakete (anderes Programm oder nicht lesbar).</summary>
    public long Dropped { get; private set; }

    /// <summary>Fehlgeschlagene Lesevorgänge der Verbindungstabelle (die bekannten Ports bleiben dann erhalten).</summary>
    public int RefreshErrors { get; private set; }

    /// <summary>Nimmt ein Paket an und liefert die Pakete, deren Wartezeit abgelaufen ist und die behalten werden.</summary>
    public IReadOnlyList<T> Add(RawPacket packet, T item)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var now = _time.GetUtcNow();
        RefreshIfDue(now);
        _queue.Enqueue((packet, item, now));
        return Release(now, all: false);
    }

    /// <summary>Ohne neues Paket aufrufen (z. B. bei Lese-Zeitüberschreitung), damit Wartende weiterkommen.</summary>
    public IReadOnlyList<T> Poll()
    {
        var now = _time.GetUtcNow();
        RefreshIfDue(now);
        return Release(now, all: false);
    }

    /// <summary>Ende der Aufnahme: Tabelle ein letztes Mal lesen und über alle wartenden Pakete entscheiden.</summary>
    public IReadOnlyList<T> Flush()
    {
        var now = _time.GetUtcNow();
        Refresh(now);
        return Release(now, all: true);
    }

    private void RefreshIfDue(DateTimeOffset now)
    {
        if (_lastRefresh is null || now - _lastRefresh >= RefreshInterval) Refresh(now);
    }

    private void Refresh(DateTimeOffset now)
    {
        _lastRefresh = now;
        IReadOnlyList<ProcessConnection> connections;
        try
        {
            connections = _source.GetConnections();
        }
        catch (Exception)
        {
            // Die Aufnahme soll an einem einzelnen Lesefehler nicht scheitern.
            RefreshErrors++;
            return;
        }

        foreach (var c in connections)
        {
            if (c.ProcessId == _processId && c.LocalPort is > 0 and <= 65535) _ports.Add((c.Protocol, c.LocalPort));
        }
    }

    private List<T> Release(DateTimeOffset now, bool all)
    {
        var result = new List<T>();
        while (_queue.Count > 0 && (all || now - _queue.Peek().Arrived >= HoldBack))
        {
            var (packet, item, _) = _queue.Dequeue();
            if (Belongs(packet))
            {
                Kept++;
                result.Add(item);
            }
            else
            {
                Dropped++;
            }
        }

        return result;
    }

    private bool Belongs(RawPacket packet)
    {
        if (!PacketHeaderParser.TryParse(packet, out var parsed) || parsed is null) return false;
        return _ports.Contains((parsed.Protocol, parsed.Source.Port))
            || _ports.Contains((parsed.Protocol, parsed.Destination.Port));
    }
}
