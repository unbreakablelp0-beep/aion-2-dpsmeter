using System.Text;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Gruppenliste (02 97): gemeldete Größe und der Rahmeninhalt zum Nachschlagen der Mitgliedsnamen.</summary>
public sealed class PartyRoster
{
    private readonly byte[] _body;

    private readonly HashSet<string>? _names;

    public PartyRoster(int size, byte[] body)
    {
        Size = size;
        _body = body;
    }

    /// <summary>Gemerkte Gruppe (nur Namen, z. B. aus einer früheren Programmsitzung); ohne Kampfkraft.</summary>
    public PartyRoster(int size, IEnumerable<string> names)
    {
        Size = size;
        _body = [];
        _names = new HashSet<string>(names, StringComparer.Ordinal);
    }

    /// <summary>true, wenn diese Liste nur gemerkt ist (nicht vom Spiel in dieser Sitzung).</summary>
    public bool IsRemembered => _names is not null;

    /// <summary>Gruppengröße laut Kopf.</summary>
    public int Size { get; }

    /// <summary>
    /// Steht der Name (als Längenbyte + UTF-8) in der Liste? Robuster als ein vollständiges Zerlegen, weil die Felder
    /// zwischen den Mitgliedern unterschiedlich lang sind.
    /// </summary>
    public bool Contains(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (_names is not null) return _names.Contains(name);
        var bytes = Encoding.UTF8.GetBytes(name);
        if (bytes.Length > 255) return false;
        Span<byte> needle = stackalloc byte[bytes.Length + 1];
        needle[0] = (byte)bytes.Length;
        bytes.CopyTo(needle[1..]);
        return _body.AsSpan().IndexOf(needle) >= 0;
    }

    /// <summary>Kampfkraft und Ausrüstungswert eines Mitglieds (über den Namen) oder null.</summary>
    public PlayerPower? PowerOf(string name) => _names is null ? CombatPowerHypothesis.FromRoster(_body, name) : null;
}

/// <summary>
/// Gruppenliste nach der öffentlichen Beschreibung im GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter. Selbst
/// geschrieben, kein Code übernommen. Kopf: Gruppen-Schlüssel u32, Gruppenname (u8 Länge + UTF-8), Größe u8.
/// Belegt an der Aufnahme 20261013-172129 (README, v17): Größe 5, alle 5 Spieler mit Namen standen in der Liste.
/// </summary>
public static class PartyRosterHypothesis
{
    public const ushort Type = 0x0297;

    /// <summary>Liste oder null, wenn der Kopf nicht passt.</summary>
    public static PartyRoster? Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length < 6) return null;
        int nameLength = body[4];
        var sizeAt = 5 + nameLength;
        if (nameLength > 64 || sizeAt >= body.Length) return null;
        int size = body[sizeAt];
        return size is >= 1 and <= 12 ? new PartyRoster(size, body.ToArray()) : null;
    }
}
