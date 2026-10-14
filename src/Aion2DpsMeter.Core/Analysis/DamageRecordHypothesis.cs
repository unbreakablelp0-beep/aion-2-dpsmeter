using System.Buffers.Binary;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Ergebnis der Zerlegung eines 04-38-Rahmens nach der Community-Hypothese.</summary>
public enum DamageFrameStatus
{
    /// <summary>Alle Datensätze gingen auf und der Rahmen wurde bis zum Ende gelesen.</summary>
    Complete,

    /// <summary>Mindestens ein Datensatz ging auf, danach blieb ein nicht passender Rest.</summary>
    PartlyParsed,

    /// <summary>Erster Datensatz hat Layout 0 – laut Hypothese eine Ankündigung ohne Wert.</summary>
    CastMarker,

    /// <summary>Schon der erste Datensatz passte nicht.</summary>
    Rejected,
}

/// <summary>
/// Ein Datensatz aus einem 04-38-Rahmen. Alle Bedeutungen (Ziel, Akteur, Fertigkeit, Kandidatenwert) sind
/// <b>Hypothesen</b> aus einer Community-Beschreibung und nicht gesichert. Der Kandidatenwert ist das Feld, das dort
/// als Schaden beschrieben wird – ob es dem Schaden im Spiel entspricht, muss erst abgeglichen werden.
/// </summary>
/// <param name="Target">Mutmaßliches Ziel (Varint).</param>
/// <param name="Switch">Schalter-Varint (untere 4 Bit = Layout, Bit 0x20 = Zusatztreffer).</param>
/// <param name="Flag">Weiteres Varint ohne beschriebene Bedeutung.</param>
/// <param name="Actor">Mutmaßlicher Akteur (Varint).</param>
/// <param name="Skill">Mutmaßliche Fertigkeit (u32, ggf. umgerechnet).</param>
/// <param name="HitByte">Einzelnes Byte nach der Fertigkeit.</param>
/// <param name="DamageType">Varint; laut Beschreibung 3 = kritisch.</param>
/// <param name="Modifier">Byte am Anfang des Zwischenblocks (Layout ≥ 5), sonst null.</param>
/// <param name="Direction">Byte zwei Stellen weiter (Layout ≥ 5), sonst null.</param>
/// <param name="ValueSkip">Wie viele Bytes vor den Werten übersprungen wurden.</param>
/// <param name="Scalar">Erster Wert, falls er nicht der Kandidatenwert ist.</param>
/// <param name="Candidate">Kandidatenwert (laut Beschreibung „Schaden“).</param>
/// <param name="ExtraHits">Anzahl Zusatzwerte (Bit 0x20).</param>
/// <param name="ExtraSum">Summe der Zusatzwerte.</param>
/// <param name="LifeSteal">Wert nach 03 00, falls vorhanden.</param>
/// <param name="TailClean">true, wenn danach Rahmenende, Folge-Datensatz oder 04 38 kommt.</param>
public sealed record DamageRecordCandidate(
    uint Target,
    uint Switch,
    uint Flag,
    uint Actor,
    uint Skill,
    byte HitByte,
    uint DamageType,
    byte? Modifier,
    byte? Direction,
    int ValueSkip,
    uint? Scalar,
    uint Candidate,
    int ExtraHits,
    long ExtraSum,
    uint? LifeSteal,
    bool TailClean)
{
    public int Layout => (int)(Switch & 0x0F);

    /// <summary>Akteur = Ziel (laut Beschreibung Selbstheilung, nicht Schaden).</summary>
    public bool SelfTarget => Actor == Target;
}

/// <summary>Zerlegung eines Rahmens.</summary>
public sealed record DamageFrameResult(DamageFrameStatus Status, IReadOnlyList<DamageRecordCandidate> Records, int ConsumedBytes, string? Problem);

/// <summary>
/// Zerlegt den Inhalt eines 04-38-Rahmens (Bytes nach dem Typ) nach der öffentlichen Beschreibung im
/// GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter – selbst geschrieben, kein Code übernommen. Die Grenzwerte
/// stammen aus dieser Beschreibung; dort sind die Längen für Layout 5 und 7 selbst als unbestätigt markiert.
/// </summary>
public static class DamageRecordHypothesis
{
    public const ushort Type = 0x0438;
    private const uint MaxEntityId = 9_999_999;
    private const uint MaxSkill = 299_999_999;
    private const uint MaxCandidate = 99_999_999;
    private const int MaxExtraHits = 25;

    /// <summary>Übersprungene Bytes vor den Werten je Layout, in Probierreihenfolge.</summary>
    private static int[] SkipCandidates(int layout) => layout switch
    {
        4 => [8],
        5 => [11, 12],
        6 => [11, 10],
        7 => [11, 14],
        _ => [],
    };

    public static DamageFrameResult Parse(ReadOnlySpan<byte> body)
    {
        var records = new List<DamageRecordCandidate>();
        var pos = 0;
        while (true)
        {
            var first = records.Count == 0;
            var status = TryRecord(body, ref pos, first, out var record, out var problem);
            if (status == RecordStatus.CastMarker) return new(DamageFrameStatus.CastMarker, records, pos, null);
            if (status == RecordStatus.Failed)
            {
                return first
                    ? new(DamageFrameStatus.Rejected, records, 0, problem)
                    : new(DamageFrameStatus.PartlyParsed, records, pos, problem);
            }

            records.Add(record!);
            if (!record!.TailClean) return new(DamageFrameStatus.PartlyParsed, records, pos, "Ende des Datensatzes passt nicht");
            if (pos >= body.Length) return new(DamageFrameStatus.Complete, records, pos, null);

            // Folge-Datensatz: Kennung (1–7, 00). 04 38 im Rahmen wird nicht weiter gedeutet.
            if (body.Length - pos >= 2 && body[pos] is >= 1 and <= 7 && body[pos + 1] == 0)
            {
                pos += 2;
                // Eigener Befund an echten Daten (nicht aus der Beschreibung): Rahmen enden oft mit der Kennung
                // 01 00 ohne folgenden Datensatz.
                if (pos == body.Length) return new(DamageFrameStatus.Complete, records, pos, null);
                continue;
            }

            return new(DamageFrameStatus.PartlyParsed, records, pos, "eingebettetes 04 38 nicht weiter gelesen");
        }
    }

    private enum RecordStatus { Ok, CastMarker, Failed }

    private static RecordStatus TryRecord(ReadOnlySpan<byte> b, ref int pos, bool first, out DamageRecordCandidate? record, out string? problem)
    {
        record = null;
        problem = null;
        var p = pos;

        if (!Varint(b, ref p, out var target) || target is 0 or > MaxEntityId) return Fail("Ziel", out problem);
        if (!Varint(b, ref p, out var sw)) return Fail("Schalter", out problem);
        var layout = (int)(sw & 0x0F);
        if (first && layout == 0)
        {
            pos = p;
            return RecordStatus.CastMarker;
        }

        if (layout is < 4 or > 7) return Fail($"Layout {layout}", out problem);
        if (!Varint(b, ref p, out var flag)) return Fail("Flag", out problem);
        if (!Varint(b, ref p, out var actor) || actor is 0 or > MaxEntityId) return Fail("Akteur", out problem);
        if (b.Length - p < 5) return Fail("Fertigkeit", out problem);
        var skill = BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
        p += 4;
        if (skill is >= 3_000_000 and <= 3_099_999) skill = skill * 10 + 1;
        if (skill is 0 or > MaxSkill) return Fail("Fertigkeit außerhalb", out problem);
        var hitByte = b[p++];
        if (!Varint(b, ref p, out var damageType)) return Fail("Schadensart", out problem);

        var blockStart = p;
        byte? modifier = null, direction = null;
        if (layout >= 5 && b.Length - blockStart >= 3)
        {
            modifier = b[blockStart];
            direction = b[blockStart + 2];
        }

        var extraHitsFlag = (sw & 0x20) != 0;
        (int Skip, uint? Scalar, uint Candidate, int End)? chosen = null, fallback = null;
        foreach (var skip in SkipCandidates(layout))
        {
            if (!TryValues(b, blockStart + skip, layout, out var scalar, out var candidate, out var end)) continue;
            if (scalar is null or 0 or (>= 1000 and <= 200_000))
            {
                chosen = (skip, scalar, candidate, end);
                break;
            }

            fallback ??= (skip, scalar, candidate, end);
        }

        if ((chosen ?? fallback) is not { } values) return Fail("Werte", out problem);

        // Ende des Datensatzes: zuerst nach eigenem Layout, dann nach Layout 6.
        var tailClean = false;
        var tailEnd = values.End;
        var extraHits = 0;
        long extraSum = 0;
        foreach (var tailLayout in layout == 6 ? new[] { 6 } : new[] { layout, 6 })
        {
            var t = values.End;
            if (!TryTail(b, ref t, tailLayout, extraHitsFlag, values.Candidate, out var hits, out var sum)) continue;
            if (!IsCleanEnd(b, t)) continue;
            tailClean = true;
            tailEnd = t;
            extraHits = hits;
            extraSum = sum;
            break;
        }

        uint? lifeSteal = null;
        if (tailClean && b.Length - tailEnd >= 2 && b[tailEnd] == 0x03 && b[tailEnd + 1] == 0x00)
        {
            var l = tailEnd + 2;
            if (Varint(b, ref l, out var ls) && IsCleanEnd(b, l))
            {
                lifeSteal = ls;
                tailEnd = l;
            }
        }

        record = new DamageRecordCandidate(target, sw, flag, actor, skill, hitByte, damageType, modifier, direction,
            values.Skip, values.Scalar, values.Candidate, extraHits, extraSum, lifeSteal, tailClean);
        pos = tailEnd;
        return RecordStatus.Ok;

        static RecordStatus Fail(string what, out string? problem)
        {
            problem = what;
            return RecordStatus.Failed;
        }
    }

    private static bool TryValues(ReadOnlySpan<byte> b, int at, int layout, out uint? scalar, out uint candidate, out int end)
    {
        scalar = null;
        candidate = 0;
        end = at;
        if (at > b.Length) return false;

        var p = at;
        if (!Varint(b, ref p, out var v1)) return false;
        if (v1 == 0 && !Varint(b, ref p, out v1)) return false; // optionale führende 0
        var afterFirst = p;
        if (!Varint(b, ref p, out var v2)) return false;

        if (layout == 6 && v1 is >= 1000 and <= 5_000_000 && v2 <= 25)
        {
            candidate = v1;
            end = afterFirst;
        }
        else
        {
            scalar = v1;
            candidate = v2;
            end = p;
        }

        return candidate is >= 1 and <= MaxCandidate;
    }

    private static bool TryTail(ReadOnlySpan<byte> b, ref int p, int layout, bool extraHitsFlag, uint candidate, out int hits, out long sum)
    {
        hits = 0;
        sum = 0;
        if (layout == 4 && (!Varint(b, ref p, out var extra) || extra is 0 or > MaxExtraHits)) return false;
        if (!extraHitsFlag) return true;

        if (!Varint(b, ref p, out var count) || count is 0 or > MaxExtraHits) return false;
        for (var i = 0; i < count; i++)
        {
            if (!Varint(b, ref p, out var v)) return false;
            sum += v;
        }

        hits = (int)count;
        return sum < candidate;
    }

    private static bool IsCleanEnd(ReadOnlySpan<byte> b, int p) =>
        p == b.Length
        || (b.Length - p >= 2 && ((b[p] is >= 1 and <= 7 && b[p + 1] == 0) || (b[p] == 0x04 && b[p + 1] == 0x38)));

    /// <summary>LEB128, höchstens 5 Bytes, Wert muss in 32 Bit passen.</summary>
    internal static bool Varint(ReadOnlySpan<byte> b, ref int p, out uint value)
    {
        value = 0;
        ulong v = 0;
        for (var i = 0; i < 5; i++)
        {
            if (p + i >= b.Length) return false;
            var x = b[p + i];
            v |= (ulong)(x & 0x7F) << (7 * i);
            if (x < 0x80)
            {
                if (v > uint.MaxValue) return false;
                value = (uint)v;
                p += i + 1;
                return true;
            }
        }

        return false;
    }
}
