namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Lebenspunkte eines Gruppenmitglieds aus 1B 92.</summary>
/// <param name="Target">Kennung des Spielers (wie die Quelle in 04 38).</param>
/// <param name="Hp">Aktuelle HP.</param>
/// <param name="MaxHp">Max-HP.</param>
/// <param name="Alive">false = tot (letztes Byte 00).</param>
public sealed record PartyMemberHp(uint Target, long Hp, long MaxHp, bool Alive);

/// <summary>
/// Gruppen-HP (1B 92), selbst an der Aufnahme 20261013-172129 ermittelt: Kennung varint, aktuelle HP varint, Max-HP
/// varint, weitere Felder, letztes Byte 01 = lebt / 00 = tot. Etwa einmal pro Sekunde je Mitglied. Belegt beim
/// Gruppen-Wipe (alle fünf mit HP 0 und Byte 00, wenige Sekunden später wieder mit HP und 01).
/// </summary>
public static class PartyHpHypothesis
{
    public const ushort Type = 0x1B92;

    /// <summary>Meldung oder null, wenn die Felder nicht passen.</summary>
    public static PartyMemberHp? Parse(ReadOnlySpan<byte> body)
    {
        var o = 0;
        if (body.Length < 8 || !Varint(body, ref o, out var target) || !Varint(body, ref o, out var hp) || !Varint(body, ref o, out var max))
        {
            return null;
        }

        var flag = body[^1];
        if (target is 0 or > uint.MaxValue || max is 0 or > 100_000_000 || hp > max || flag > 1 || o >= body.Length) return null;
        return new PartyMemberHp((uint)target, (long)hp, (long)max, flag == 1 && hp > 0);
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
