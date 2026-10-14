using System.Globalization;

namespace Aion2DpsMeter.Core.Capture;

/// <summary>Transportprotokoll einer Verbindung.</summary>
public enum TransportProtocol
{
    Tcp,
    Udp,
}

/// <summary>
/// Eine offene Netzwerkverbindung (bzw. ein UDP-Endpunkt) eines Prozesses.
/// </summary>
/// <param name="ProcessId">Prozess-Id (PID).</param>
/// <param name="Protocol">TCP oder UDP.</param>
/// <param name="LocalAddress">Lokale IP-Adresse.</param>
/// <param name="LocalPort">Lokaler Port.</param>
/// <param name="RemoteAddress">Gegenstelle (bei UDP und lauschenden TCP-Sockets null).</param>
/// <param name="RemotePort">Port der Gegenstelle oder null.</param>
public sealed record ProcessConnection(
    int ProcessId,
    TransportProtocol Protocol,
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress = null,
    int? RemotePort = null);

/// <summary>
/// Liefert die offenen Verbindungen aller Prozesse (nur lesend, z. B. über die Windows-Verbindungstabelle).
/// </summary>
public interface IProcessConnectionSource
{
    /// <summary>Alle aktuell offenen TCP-Verbindungen und UDP-Endpunkte mit Prozess-Id.</summary>
    IReadOnlyList<ProcessConnection> GetConnections();
}

/// <summary>Ein Prozess mit seinen Verbindungen, für die Auswahl in der Oberfläche.</summary>
/// <param name="ProcessId">PID.</param>
/// <param name="Name">Prozessname oder „Unbekannt“.</param>
/// <param name="Connections">Verbindungen des Prozesses.</param>
public sealed record ProcessConnectionGroup(int ProcessId, string Name, IReadOnlyList<ProcessConnection> Connections)
{
    /// <summary>Anzeigetext, z. B. „Spiel (PID 1234) – 3 Verbindungen“.</summary>
    public string DisplayName =>
        $"{Name} (PID {ProcessId}) – {Connections.Count} {(Connections.Count == 1 ? "Verbindung" : "Verbindungen")}";
}

/// <summary>
/// Gruppiert Verbindungen nach Prozess und baut daraus einen Paketfilter.
/// </summary>
public static class ProcessConnectionFilter
{
    /// <summary>
    /// Gruppiert nach PID (PID 0 = System-Leerlauf wird ausgelassen), sortiert nach Name, dann PID.
    /// </summary>
    /// <param name="connections">Alle Verbindungen.</param>
    /// <param name="resolveName">Liefert den Prozessnamen zur PID oder null.</param>
    public static IReadOnlyList<ProcessConnectionGroup> GroupByProcess(
        IEnumerable<ProcessConnection> connections, Func<int, string?> resolveName)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(resolveName);

        return connections
            .Where(c => c.ProcessId != 0)
            .GroupBy(c => c.ProcessId)
            .Select(g => new ProcessConnectionGroup(g.Key, resolveName(g.Key) ?? "Unbekannt", g.ToList()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.ProcessId)
            .ToList();
    }

    /// <summary>
    /// Baut einen libpcap/BPF-Filter, der Pakete der lokalen Ports dieser Verbindungen durchlässt,
    /// z. B. „(tcp port 50000) or (udp port 50001)“. Null, wenn es keine verwendbaren Ports gibt.
    /// </summary>
    /// <remarks>
    /// „port“ passt auf Quell- oder Zielport. Verwendet ein anderer Prozess denselben Port als
    /// Gegenstelle, können vereinzelt fremde Pakete enthalten sein. Neue Verbindungen, die
    /// nach dem Erstellen des Filters geöffnet werden, sind nicht enthalten.
    /// </remarks>
    public static string? BuildFilter(IEnumerable<ProcessConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var parts = connections
            .Where(c => c.LocalPort is > 0 and <= 65535)
            .Select(c => (Protocol: c.Protocol == TransportProtocol.Tcp ? "tcp" : "udp", c.LocalPort))
            .Distinct()
            .OrderBy(p => p.Protocol, StringComparer.Ordinal)
            .ThenBy(p => p.LocalPort)
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"({p.Protocol} port {p.LocalPort})"))
            .ToList();

        return parts.Count == 0 ? null : string.Join(" or ", parts);
    }

    /// <summary>
    /// Für den Ein-Klick-Start: das Spielprogramm in der Liste (Name „AION2“ bzw. enthält „aion2“/„aion 2“; Launcher,
    /// Updater und Fehlerberichte ausgenommen). Bei mehreren das mit den meisten Verbindungen; null, wenn keins passt –
    /// dann wählt man von Hand.
    /// </summary>
    public static ProcessConnectionGroup? FindGameProcess(IEnumerable<ProcessConnectionGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        string[] excluded = ["launch", "update", "patch", "crash", "report", "setup"];

        return groups
            .Where(g =>
            {
                var name = g.Name.ToLowerInvariant();
                return (name.Contains("aion2", StringComparison.Ordinal) || name.Contains("aion 2", StringComparison.Ordinal))
                    && !excluded.Any(x => name.Contains(x, StringComparison.Ordinal));
            })
            .OrderByDescending(g => g.Name.Equals("AION2", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Connections.Count)
            .FirstOrDefault();
    }

    /// <summary>Dateiname für eine Aufnahme, z. B. „aufnahme-20261012-011500.pcap“.</summary>
    public static string CreateRecordingPath(string directory, DateTimeOffset time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"aufnahme-{time:yyyyMMdd-HHmmss}.pcap"));
    }
}
