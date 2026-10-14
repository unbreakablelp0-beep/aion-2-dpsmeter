namespace Aion2DpsMeter.Core.Analysis;

/// <summary>HP-Meldung eines Gegners.</summary>
/// <param name="Target">Kennung (wie das Ziel in 04 38).</param>
/// <param name="Hp">Rest-HP.</param>
/// <param name="MaxHp">Max-HP, falls in der Meldung enthalten (nur beim Erscheinen), sonst null.</param>
/// <param name="NpcCode">Gegner-Nummer (NPC-Vorlage) beim Erscheinen, sonst null.</param>
public sealed record EnemyHpCandidate(uint Target, long Hp, long? MaxHp, int? NpcCode = null);

/// <summary>
/// Gegner-HP nach der öffentlichen Beschreibung im GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter. Selbst
/// geschrieben, kein Code übernommen. Geprüft an den eigenen Aufnahmen (README, v14):
/// <list type="bullet">
/// <item>00 8D: Ziel varint, Art varint (2 = Gegner), varint, varint, HP u64. Bei zwei allein bekämpften Gegnern sank
/// die HP über den Kampf genau um den gemessenen Schaden.</item>
/// <item>41 36 (Erscheinen): erstes „01 aktuell max“ (Varints) nach der Kennung. Das Max passte in allen Fällen zum
/// bekannten Gegner-Maximum, die danach gemeldete HP blieb darunter.</item>
/// </list>
/// </summary>
public static class EnemyHpHypothesis
{
    public const ushort RemainingType = 0x008D;
    public const ushort SpawnType = 0x4136;
    private const long MaxPlausibleHp = 10_000_000_000;

    /// <summary>00 8D lesen; null, wenn die Felder nicht passen oder es kein Gegner (Art 2) ist.</summary>
    public static EnemyHpCandidate? ParseRemaining(ReadOnlySpan<byte> body)
    {
        var o = 0;
        if (!Varint(body, ref o, out var target) || !Varint(body, ref o, out var kind) || kind != 2 ||
            !Varint(body, ref o, out _) || !Varint(body, ref o, out _) || o + 8 > body.Length)
        {
            return null;
        }

        var hp = BitConverter.ToInt64(body.Slice(o, 8));
        return target is 0 or > uint.MaxValue || hp is < 0 or > MaxPlausibleHp ? null : new EnemyHpCandidate((uint)target, hp, null);
    }

    /// <summary>Aus 41 36 die Start-HP und Max-HP lesen (erstes passendes „01 aktuell max“); sonst null.</summary>
    public static EnemyHpCandidate? ParseSpawn(ReadOnlySpan<byte> body)
    {
        var o = 0;
        if (!Varint(body, ref o, out var target) || target is 0 or > uint.MaxValue) return null;
        for (var i = o; i < body.Length - 2; i++)
        {
            if (body[i] != 0x01) continue;
            var q = i + 1;
            if (Varint(body, ref q, out var current) && Varint(body, ref q, out var max) &&
                max is >= 100 and <= MaxPlausibleHp && current is > 0 && current <= max)
            {
                return new EnemyHpCandidate((uint)target, (long)current, (long)max, ParseNpcCode(body));
            }
        }

        return null;
    }

    /// <summary>
    /// Gegner-Nummer (NPC-Vorlage) aus 41 36: u24 direkt vor der Markierung <c>00 (00|40) 02</c> in den ersten 60 Bytes
    /// nach der Kennung (Community-Beschreibung). Null, wenn nicht gefunden.
    /// </summary>
    public static int? ParseNpcCode(ReadOnlySpan<byte> body)
    {
        var o = 0;
        if (!Varint(body, ref o, out var target) || target == 0) return null;
        var end = Math.Min(body.Length - 2, o + 60);
        for (var i = o; i < end; i++)
        {
            if (body[i] != 0x00 || (body[i + 1] != 0x40 && body[i + 1] != 0x00) || body[i + 2] != 0x02) continue;
            if (i < o + 3) return null;
            var code = body[i - 3] | body[i - 2] << 8 | body[i - 1] << 16;
            return code is >= 1_000_000 and < 10_000_000 ? code : null;
        }

        return null;
    }

    private static bool Varint(ReadOnlySpan<byte> b, ref int o, out ulong value)
    {
        value = 0;
        for (var shift = 0; o < b.Length && shift < 64; shift += 7)
        {
            var x = b[o++];
            value |= (ulong)(x & 0x7F) << shift;
            if ((x & 0x80) == 0) return true;
        }

        return false;
    }
}
