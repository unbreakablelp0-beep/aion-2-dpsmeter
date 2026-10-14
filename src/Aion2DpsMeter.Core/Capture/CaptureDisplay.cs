using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Aion2DpsMeter.Core.Capture;

/// <summary>
/// Hilfen für die Anzeige der Netzwerkdiagnose (deutsche Formatierung, Adaptervorschlag).
/// </summary>
public static class CaptureDisplay
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Bytes lesbar, z. B. „512 B“, „1,5 KB“, „2,25 MB“ (Basis 1024).</summary>
    public static string FormatBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(German, $"{bytes} B")
            : string.Create(German, $"{value:0.##} {units[unit]}");
    }

    /// <summary>Zahl mit Tausenderpunkten, z. B. „12.345“.</summary>
    public static string FormatCount(long count) => count.ToString("N0", German);

    /// <summary>Zeitspanne vom ersten bis zum letzten Paket als „mm:ss“ bzw. „h:mm:ss“.</summary>
    public static string FormatDuration(PacketStatistics stats)
    {
        var duration = Duration(stats);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", German)
            : duration.ToString(@"mm\:ss", German);
    }

    /// <summary>Pakete pro Sekunde zwischen erstem und letztem Paket, „–“ wenn nicht berechenbar.</summary>
    public static string FormatPacketRate(PacketStatistics stats)
    {
        var seconds = Duration(stats).TotalSeconds;
        return seconds > 0
            ? string.Create(German, $"{stats.PacketCount / seconds:0.#} Pakete/s")
            : "–";
    }

    /// <summary>Anzeigetext eines Adapters inkl. Adressen.</summary>
    public static string DescribeAdapter(NetworkAdapterInfo adapter)
    {
        var text = adapter.IsLoopback ? $"{adapter.DisplayName} (Loopback)" : adapter.DisplayName;
        var ipv4 = adapter.Addresses.Where(IsIPv4).ToList();
        return ipv4.Count > 0 ? $"{text} – {string.Join(", ", ipv4)}" : text;
    }

    /// <summary>
    /// Schlägt einen Adapter vor: bevorzugt kein Loopback mit „echter“ IPv4-Adresse
    /// (nicht 169.254.x.x), dann irgendein Nicht-Loopback, sonst den ersten. Null bei leerer Liste.
    /// </summary>
    public static NetworkAdapterInfo? SuggestAdapter(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        return adapters.FirstOrDefault(a => !a.IsLoopback && a.Addresses.Any(IsRoutableIPv4))
            ?? adapters.FirstOrDefault(a => !a.IsLoopback)
            ?? adapters.FirstOrDefault();
    }

    private static TimeSpan Duration(PacketStatistics stats) =>
        stats.FirstPacketAt is { } first && stats.LastPacketAt is { } last ? last - first : TimeSpan.Zero;

    private static bool IsIPv4(string address) =>
        IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsRoutableIPv4(string address) =>
        IPAddress.TryParse(address, out var ip)
        && ip.AddressFamily == AddressFamily.InterNetwork
        && !IPAddress.IsLoopback(ip)
        && !address.StartsWith("169.254.", StringComparison.Ordinal);
}
