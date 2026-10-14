using System.Buffers.Binary;
using System.Text;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Wie der Besitzer gefunden wurde.</summary>
public enum SummonOwnerSource
{
    /// <summary>Elternverweis (06 + u32 Besitzer + Legion + 0 + Server + Name).</summary>
    ParentLink,

    /// <summary>Fester Anker 80 75 D5 2A BB 03 00 00 + Varint.</summary>
    Anchor,
}

/// <summary>
/// Beschwörung/Pet mit mutmaßlichem Besitzer aus Typ 41 36. Hypothese, nicht gesichert.
/// </summary>
/// <param name="RawId">Kennung wie im Rahmen (Varint).</param>
/// <param name="MappedId">Kennung nach der beschriebenen Umrechnung (über 1.000.000 → (roh &amp; 0x3FFF) | 0x4000).</param>
/// <param name="Kind">Art-Byte.</param>
/// <param name="Owner">Mutmaßlicher Besitzer.</param>
/// <param name="Source">Fundweg.</param>
public sealed record SummonOwnerCandidate(uint RawId, uint MappedId, byte Kind, uint Owner, SummonOwnerSource Source);

/// <summary>
/// Sucht in Typ 41 36 den Besitzer einer Beschwörung nach der öffentlichen Beschreibung im GPL-3.0-Projekt
/// cyberbadger6969/aion2-dps-meter – selbst geschrieben, kein Code übernommen.
/// </summary>
public static class SummonOwnerHypothesis
{
    public const ushort Type = 0x4136;

    private static readonly byte[] AnchorBytes = [0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0x00, 0x00];
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    /// <summary>Beschwörung mit Besitzer oder null.</summary>
    public static SummonOwnerCandidate? Parse(ReadOnlySpan<byte> body)
    {
        var p = 0;
        if (!DamageRecordHypothesis.Varint(body, ref p, out var raw) || raw == 0) return null;
        var mapped = raw > 1_000_000 ? (raw & 0x3FFF) | 0x4000 : raw;
        if (p >= body.Length) return null;

        var maskStart = p;
        var kind = body[maskStart];
        uint mask = body.Length - maskStart >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(body[maskStart..]) : 0;

        if (kind == 0x5F && (mask & 0x10) != 0 && FindParentLink(body, maskStart, raw, mapped) is { } parent)
        {
            return new SummonOwnerCandidate(raw, mapped, kind, parent, SummonOwnerSource.ParentLink);
        }

        if (kind is 0x5F or 0x1F or 0x1D or 0x5D)
        {
            var window = body[maskStart..Math.Min(body.Length, maskStart + 136)];
            var at = window.IndexOf(AnchorBytes);
            if (at >= 0)
            {
                var q = maskStart + at + AnchorBytes.Length;
                if (DamageRecordHypothesis.Varint(body, ref q, out var owner) && owner is >= 1 and <= 9_999_999 && owner != raw && owner != mapped)
                {
                    return new SummonOwnerCandidate(raw, mapped, kind, owner, SummonOwnerSource.Anchor);
                }
            }
        }

        return null;
    }

    private static uint? FindParentLink(ReadOnlySpan<byte> body, int from, uint raw, uint mapped)
    {
        for (var i = from; i + 1 + 4 + 4 + 2 + 2 + 1 <= body.Length; i++)
        {
            if (body[i] != 0x06) continue;
            var parent = BinaryPrimitives.ReadUInt32LittleEndian(body[(i + 1)..]);
            if (parent is 0 or > 9_999_999 || parent == raw || parent == mapped) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(body[(i + 9)..]) != 0) continue;
            var server = BinaryPrimitives.ReadUInt16LittleEndian(body[(i + 11)..]);
            if (server is < 1000 or > 2999) continue;
            int length = body[i + 13];
            if (length is < 1 or > 48 || body.Length - (i + 14) < length) continue;
            try
            {
                var name = StrictUtf8.GetString(body.Slice(i + 14, length));
                if (name.Any(char.IsControl)) continue;
            }
            catch (DecoderFallbackException)
            {
                continue;
            }

            return parent;
        }

        return null;
    }
}
