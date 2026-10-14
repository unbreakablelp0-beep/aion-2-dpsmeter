namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Ein Platz der Feldboss-Liste einer Karte.</summary>
/// <param name="Slot">Platz-Kennung (Karte × 100 + Platz).</param>
/// <param name="Alive">true: Boss lebt (Zeit = seit wann); false: tot (Zeit = wann er wiederkommt).</param>
/// <param name="Time">Zeitpunkt oder null, wenn das Spiel keine Zeit nennt (0).</param>
public sealed record FieldBossSlot(uint Slot, bool Alive, DateTimeOffset? Time)
{
    /// <summary>Platz auf der Karte (1, 2, …).</summary>
    public int Place => (int)(Slot % 100);
}

/// <summary>Feldboss-Liste einer Karte (01 91).</summary>
/// <param name="Map">Karten-Nummer.</param>
/// <param name="Count">Anzahl Plätze laut Kopf.</param>
/// <param name="Slots">Gelesene Plätze (bei einem nicht lesbaren Platz endet die Liste dort).</param>
public sealed record FieldBossList(uint Map, int Count, IReadOnlyList<FieldBossSlot> Slots);

/// <summary>
/// Feldboss-Liste nach der öffentlichen Beschreibung im GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter. Selbst
/// geschrieben, kein Code übernommen. Aufbau: u16 0, Karte u32, Anzahl u8, je Platz { lebt u8, Platz varint (Karte×100+n),
/// [lebt: x y z f32], [manchmal ein Zusatzbyte], Zeit u64 ms (Unix) }, Ende mit Nullen. Das Zusatzbyte wird erkannt,
/// indem nur die Lesart gilt, nach der der nächste Platz (oder das Ende) passt.
/// </summary>
public static class FieldBossListHypothesis
{
    public const ushort Type = 0x0191;
    private const long MinTime = 1_600_000_000_000, MaxTime = 2_600_000_000_000;

    public static FieldBossList? Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length < 7 || body[0] != 0 || body[1] != 0) return null;
        var map = BitConverter.ToUInt32(body.Slice(2, 4));
        int count = body[6];
        if (map == 0 || count is 0 or > 50) return null;

        var slots = new List<FieldBossSlot>(count);
        var o = 7;
        for (var n = 0; n < count; n++)
        {
            if (!TrySlot(body, ref o, map, n == count - 1, out var slot)) break;
            slots.Add(slot);
        }

        return new FieldBossList(map, count, slots);
    }

    private static bool TrySlot(ReadOnlySpan<byte> b, ref int o, uint map, bool last, out FieldBossSlot slot)
    {
        slot = null!;
        if (!Header(b, o, map, out var alive, out var id, out var at)) return false;
        if (alive) at += 12; // Position x y z
        for (var extra = 0; extra <= 1; extra++)
        {
            var t = at + extra;
            if (t + 8 > b.Length) break;
            var time = BitConverter.ToInt64(b.Slice(t, 8));
            if (time != 0 && time is < MinTime or > MaxTime) continue;
            var end = t + 8;
            var fits = last ? b[end..].IndexOfAnyExcept((byte)0) < 0 : Header(b, end, map, out _, out _, out _);
            if (!fits) continue;
            slot = new FieldBossSlot(id, alive, time == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(time));
            o = end;
            return true;
        }

        return false;
    }

    /// <summary>„lebt u8 (0/1), Platz varint“ mit Platz im Bereich dieser Karte.</summary>
    private static bool Header(ReadOnlySpan<byte> b, int o, uint map, out bool alive, out uint id, out int next)
    {
        alive = false;
        id = 0;
        next = o;
        if (o + 2 > b.Length || b[o] > 1) return false;
        alive = b[o] == 1;
        var p = o + 1;
        ulong value = 0;
        for (var shift = 0; p < b.Length && shift < 35; shift += 7)
        {
            var x = b[p++];
            value |= (ulong)(x & 0x7F) << shift;
            if ((x & 0x80) == 0)
            {
                if (value / 100 != map || value % 100 == 0) return false;
                id = (uint)value;
                next = p;
                return true;
            }
        }

        return false;
    }
}
