namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Od-Energie, selbst an den Aufnahmen 20261014-181025 und 20261014-214050 ermittelt (Screenshots des Besitzers):
/// Eintrag „Stelle 01, Kennung 51591 (Varint 87 93 03), Wert, Zusatzwert“.
/// <list type="bullet">
/// <item>0B 61 beim Einloggen: Liste, darin „0C 01 87 93 03 0F 37“ → Zusatzwert 55.</item>
/// <item>0C 61 beim Einlösen: „01 0C 01 87 93 03 0F 5F 01 28 00 00 00“ → Zusatzwert 95, +40 (ebenso 200 → 240).</item>
/// </list>
/// Sicher ist nur der Zusatzwert (blau). Der Wert davor stand in beiden Aufnahmen auf 15 und hat sich nie geändert – ob
/// er die aufladende Od-Energie ist, ist noch offen, daher wird er nicht ausgegeben. Das Maximum wird nicht gesendet.
/// </summary>
public static class OdEnergyHypothesis
{
    public const ushort ListType = 0x0B61;
    public const ushort UpdateType = 0x0C61;

    private static readonly byte[] Entry = [0x0C, 0x01, 0x87, 0x93, 0x03];

    /// <summary>Zusatz-Od aus 0B 61 (Login-Liste) oder 0C 61 (Änderung), sonst null.</summary>
    public static long? ParseBonus(ushort type, ReadOnlySpan<byte> body)
    {
        if (type is not (ListType or UpdateType)) return null;
        var at = body.IndexOf(Entry);
        if (at < 0 || (type == UpdateType && at != 1)) return null;
        var o = at + Entry.Length;
        if (!Varint(body, ref o, out _) || !Varint(body, ref o, out var bonus)) return null;
        return bonus <= 100_000 ? (long)bonus : null;
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
