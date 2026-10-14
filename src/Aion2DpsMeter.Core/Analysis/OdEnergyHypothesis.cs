namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Od-Energie: aufladender Wert und Zusatzwert (im Spiel „15 (+95)“), zusammen <see cref="Total"/>.</summary>
public sealed record OdEnergy(long Current, long Bonus)
{
    public long Total => Current + Bonus;
}

/// <summary>
/// Od-Energie, selbst an den Aufnahmen 20261014-181025 und 20261014-214050 ermittelt (Screenshots des Besitzers):
/// Eintrag „Stelle 01, Kennung 51591 (Varint 87 93 03), Wert, Zusatzwert“.
/// <list type="bullet">
/// <item>0B 61 beim Einloggen: Liste, darin „0C 01 87 93 03 0F 37“ → 15 (+55).</item>
/// <item>0C 61 beim Einlösen: „01 0C 01 87 93 03 0F 5F 01 28 00 00 00“ → 15 (+95), +40 (ebenso +200 → +240).</item>
/// </list>
/// Der Zusatzwert (blau) ist durch zwei Änderungen belegt. Der Wert davor stand in allen Proben auf 15 – wie im Spiel;
/// seine Änderung ist noch nicht beobachtet. Das Maximum (840) wird nicht gesendet.
/// </summary>
public static class OdEnergyHypothesis
{
    public const ushort ListType = 0x0B61;
    public const ushort UpdateType = 0x0C61;

    // Stelle 01 + Kennung 51591; davor ein Merkmal-Byte: 0x04 = Wert vorhanden, 0x08 = Zusatzwert vorhanden (0x0C = beides).
    private static readonly byte[] Entry = [0x01, 0x87, 0x93, 0x03];

    /// <summary>
    /// Od-Energie (aufladender Wert + Zusatzwert) aus 0B 61 (Login-Liste) oder 0C 61 (Änderung), sonst null. Belegt:
    /// 15 (+55/+95/+240) und 565 (+10) – Aufnahme 20261014-220333: Login „04 01 87 93 03 B5 04“ (nur Wert, kein Zusatz),
    /// Einlösen „01 0C 01 87 93 03 B5 04 0A 01 0A 00 00 00“ → 565 (+10).
    /// </summary>
    public static OdEnergy? Parse(ushort type, ReadOnlySpan<byte> body)
    {
        if (type is not (ListType or UpdateType)) return null;
        var search = 0;
        while (search < body.Length)
        {
            var found = body[search..].IndexOf(Entry);
            if (found < 0) return null;
            var at = search + found;
            search = at + 1;
            if (at < 1) continue;
            var flags = body[at - 1];
            if (flags is not (0x04 or 0x08 or 0x0C) || (type == UpdateType && at != 2)) continue;

            var o = at + Entry.Length;
            ulong current = 0, bonus = 0;
            if ((flags & 0x04) != 0 && !Varint(body, ref o, out current)) return null;
            if ((flags & 0x08) != 0 && !Varint(body, ref o, out bonus)) return null;
            return current <= 100_000 && bonus <= 100_000 ? new OdEnergy((long)current, (long)bonus) : null;
        }

        return null;
    }

    /// <summary>Nur der Zusatzwert (blau) oder null.</summary>
    public static long? ParseBonus(ushort type, ReadOnlySpan<byte> body) => Parse(type, body)?.Bonus;

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
