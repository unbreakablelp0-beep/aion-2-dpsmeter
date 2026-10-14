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

    private static readonly byte[] Entry = [0x0C, 0x01, 0x87, 0x93, 0x03];

    /// <summary>Od-Energie (aufladender Wert + Zusatzwert) aus 0B 61 (Login-Liste) oder 0C 61 (Änderung), sonst null.</summary>
    public static OdEnergy? Parse(ushort type, ReadOnlySpan<byte> body)
    {
        if (type is not (ListType or UpdateType)) return null;
        var at = body.IndexOf(Entry);
        if (at < 0 || (type == UpdateType && at != 1)) return null;
        var o = at + Entry.Length;
        if (!Varint(body, ref o, out var current) || !Varint(body, ref o, out var bonus)) return null;
        return current <= 100_000 && bonus <= 100_000 ? new OdEnergy((long)current, (long)bonus) : null;
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
