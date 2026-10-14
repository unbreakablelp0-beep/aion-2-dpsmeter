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

    /// <summary>
    /// Mitglieder in Listenreihenfolge (Name, Stufe, Ausrüstungswert, Kampfkraft) – für die Lobby-Anzeige. Bei einer nur
    /// gemerkten Gruppe leer (keine Werte bekannt).
    /// </summary>
    public IReadOnlyList<RosterMember> Members => _names is null ? PartyRosterHypothesis.Members(_body) : [];

    /// <summary>Kampfkraft und Ausrüstungswert eines Mitglieds (über den Namen) oder null.</summary>
    public PlayerPower? PowerOf(string name) => _names is null ? CombatPowerHypothesis.FromRoster(_body, name) : null;
}

/// <summary>
/// Gruppenliste nach der öffentlichen Beschreibung im GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter. Selbst
/// geschrieben, kein Code übernommen. Kopf: Gruppen-Schlüssel u32, Gruppenname (u8 Länge + UTF-8), Größe u8.
/// Belegt an der Aufnahme 20261013-172129 (README, v17): Größe 5, alle 5 Spieler mit Namen standen in der Liste.
/// </summary>
/// <summary>Ein Gruppenmitglied aus der Liste.</summary>
public sealed record RosterMember(string Name, int Level, int GearScore, long CombatPower);

public static class PartyRosterHypothesis
{
    public const ushort Type = 0x0297;

    /// <summary>
    /// Mitglieder suchen: je Eintrag Maske u8, Platz u8 (1–12, aufsteigend), Kennung (8 B), Name (u8 + UTF-8), danach
    /// Klasse u32, Stufe u32 (1–99), Ausrüstungswert u32. Belegt an der Lobby-Aufnahme 20261014-214503 (Stufen 45/30/24/24/23
    /// wie im Spiel). Kampfkraft über <see cref="CombatPowerHypothesis.FromRoster"/>.
    /// </summary>
    public static IReadOnlyList<RosterMember> Members(ReadOnlySpan<byte> body)
    {
        var members = new List<RosterMember>();
        var nextSlot = 1;
        for (var i = 0; i + 11 < body.Length && members.Count < 12; i++)
        {
            if (body[i + 1] != nextSlot) continue;
            int length = body[i + 10];
            var nameAt = i + 11;
            if (length is < 2 or > 36 || nameAt + length + 12 > body.Length) continue;
            string name;
            try
            {
                name = new UTF8Encoding(false, true).GetString(body.Slice(nameAt, length));
            }
            catch (DecoderFallbackException)
            {
                continue;
            }

            if (!name.All(char.IsLetterOrDigit)) continue;
            var o = nameAt + length;
            var level = BitConverter.ToInt32(body.Slice(o + 4, 4));
            var gear = BitConverter.ToInt32(body.Slice(o + 8, 4));
            if (level is < 1 or > 99) continue;
            var power = CombatPowerHypothesis.FromRoster(body, name);
            members.Add(new RosterMember(name, level, power?.GearScore ?? (gear is >= 1 and <= 50_000 ? gear : 0), power?.CombatPower ?? 0));
            nextSlot++;
            i = o;
        }

        return members;
    }

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
