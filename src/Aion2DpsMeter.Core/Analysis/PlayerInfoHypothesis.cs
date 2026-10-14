using System.Buffers.Binary;
using System.Text;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Spielerangaben aus Typ 33 36 (laut Community: eigener Charakter) bzw. 45 36 / 44 36 (andere Spieler).
/// Bedeutungen sind <b>Hypothesen</b> und nicht gesichert.
/// </summary>
/// <param name="Type">Nachrichtentyp.</param>
/// <param name="EntityId">Kennung (dieselbe Art Kennung wie Akteur/Ziel in 04 38).</param>
/// <param name="Name">Name (UTF-8 gelesen).</param>
/// <param name="ServerId">Server laut Hypothese (1000–2999) oder null.</param>
public sealed record PlayerInfoCandidate(ushort Type, uint EntityId, string Name, ushort? ServerId)
{
    /// <summary>Laut Hypothese der eigene Charakter.</summary>
    public bool IsSelf => Type == PlayerInfoHypothesis.SelfType;
}

/// <summary>
/// Liest Kennung und Namen aus Spieler-Nachrichten nach der öffentlichen Beschreibung im GPL-3.0-Projekt
/// cyberbadger6969/aion2-dps-meter – selbst geschrieben, kein Code übernommen. Die Namenskodierung ist dort nicht
/// beschrieben; hier wird streng UTF-8 angenommen (ungültige Bytes oder Steuerzeichen → verworfen).
/// </summary>
public static class PlayerInfoHypothesis
{
    public const ushort SelfType = 0x3336;
    public const ushort PlayerType = 0x4536;
    public const ushort OldPlayerType = 0x4436;

    public static readonly IReadOnlySet<ushort> Types = new HashSet<ushort> { SelfType, PlayerType, OldPlayerType };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Angaben oder null, wenn die Felder nicht passen.</summary>
    public static PlayerInfoCandidate? Parse(ushort type, ReadOnlySpan<byte> body)
    {
        if (!Types.Contains(type)) return null;

        var p = 0;
        if (!DamageRecordHypothesis.Varint(body, ref p, out var id) || id is 0 or > 9_999_999) return null;
        if (body.Length - p < 4 + 1 + 1) return null;
        p += 4; // Maske 1, Bedeutung unbekannt
        if ((body[p++] & 0x01) == 0) return null;
        int length = body[p++];
        if (length is < 1 or > 36 || body.Length - p < length) return null;

        string name;
        try
        {
            name = StrictUtf8.GetString(body.Slice(p, length));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        if (name.Any(char.IsControl) || string.IsNullOrWhiteSpace(name)) return null;
        p += length;

        ushort? server = null;
        if (body.Length - p >= 2)
        {
            var s = BinaryPrimitives.ReadUInt16LittleEndian(body[p..]);
            if (s is >= 1000 and <= 2999) server = s;
        }

        return new PlayerInfoCandidate(type, id, name, server);
    }
}
