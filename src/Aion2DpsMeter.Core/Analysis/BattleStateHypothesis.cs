namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Kampfzustand eines Gegners.</summary>
/// <param name="Target">Kennung.</param>
/// <param name="Engaged">true = im Kampf, false = Kampf verlassen.</param>
/// <param name="Deadline">Zeitlimit (Berserk/Enrage) bei Bossen mit Zeitlimit, sonst null.</param>
public sealed record BattleState(uint Target, bool Engaged, DateTimeOffset? Deadline);

/// <summary>
/// Kampfzustand (21 8D) nach der öffentlichen Beschreibung im GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter. Selbst
/// geschrieben, kein Code übernommen: Gegner varint, varint, Zustand varint (1 = im Kampf, 0 = raus); bei Bossen mit
/// Zeitlimit folgt beim Kampfbeginn die Deadline als u64 ms (Unix), höchstens 1 h nach dem Rahmen.
/// </summary>
public static class BattleStateHypothesis
{
    public const ushort Type = 0x218D;

    /// <summary>Zustand oder null, wenn die Felder nicht passen. <paramref name="now"/> nur für die Plausibilität der Deadline.</summary>
    public static BattleState? Parse(ReadOnlySpan<byte> body, DateTimeOffset? now = null)
    {
        var o = 0;
        if (!Varint(body, ref o, out var target) || target is 0 or > uint.MaxValue) return null;
        if (!Varint(body, ref o, out _) || !Varint(body, ref o, out var toggle) || toggle > 1) return null;

        DateTimeOffset? deadline = null;
        if (toggle == 1 && o + 8 == body.Length)
        {
            var ms = BitConverter.ToInt64(body.Slice(o, 8));
            o += 8;
            if (ms is > 1_600_000_000_000 and < 2_600_000_000_000)
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                if (now is not { } n || (at > n && at - n <= TimeSpan.FromHours(1))) deadline = at;
            }
        }

        return o == body.Length ? new BattleState((uint)target, toggle == 1, deadline) : null;
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
