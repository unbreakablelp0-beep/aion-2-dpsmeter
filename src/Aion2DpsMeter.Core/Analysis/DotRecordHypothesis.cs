using System.Buffers.Binary;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Ein Datensatz vom Typ 05 38. Bedeutungen sind <b>Hypothesen</b> aus einer Community-Beschreibung
/// (dort „DoT/HoT“, Schaden bzw. Heilung über Zeit) und nicht gesichert.
/// </summary>
/// <param name="Target">Mutmaßliches Ziel (Varint).</param>
/// <param name="Effect">Effekt-Byte; laut Beschreibung 0x02/0x0A = Schaden, 0x01/0x09/0x0B = Heilung.</param>
/// <param name="Actor">Mutmaßlicher Akteur (Varint).</param>
/// <param name="Skipped">Varint ohne beschriebene Bedeutung.</param>
/// <param name="Skill">Fertigkeit (u32 LE ÷ 100).</param>
/// <param name="Amount">Kandidatenwert (Betrag).</param>
/// <param name="Complete">true, wenn der Rahmen danach zu Ende ist.</param>
public sealed record DotRecordCandidate(uint Target, byte Effect, uint Actor, uint Skipped, uint Skill, uint Amount, bool Complete)
{
    /// <summary>Laut Beschreibung ein Schadenseffekt.</summary>
    public bool IsDamageEffect => Effect is 0x02 or 0x0A;

    /// <summary>Laut Beschreibung ein Heileffekt.</summary>
    public bool IsHealEffect => Effect is 0x01 or 0x09 or 0x0B;
}

/// <summary>
/// Zerlegt den Inhalt eines 05-38-Rahmens nach der öffentlichen Beschreibung im GPL-3.0-Projekt
/// cyberbadger6969/aion2-dps-meter – selbst geschrieben, kein Code übernommen.
/// </summary>
public static class DotRecordHypothesis
{
    public const ushort Type = 0x0538;

    /// <summary>Datensatz oder null, wenn die Felder nicht passen.</summary>
    public static DotRecordCandidate? Parse(ReadOnlySpan<byte> body)
    {
        var p = 0;
        if (!DamageRecordHypothesis.Varint(body, ref p, out var target) || target is 0 or > 9_999_999) return null;
        if (p >= body.Length) return null;
        var effect = body[p++];
        if (!DamageRecordHypothesis.Varint(body, ref p, out var actor) || actor is 0 or > 9_999_999) return null;
        if (!DamageRecordHypothesis.Varint(body, ref p, out var skipped)) return null;
        if (body.Length - p < 4) return null;
        var skill = BinaryPrimitives.ReadUInt32LittleEndian(body[p..]) / 100;
        p += 4;
        if (!DamageRecordHypothesis.Varint(body, ref p, out var amount)) return null;
        return new DotRecordCandidate(target, effect, actor, skipped, skill, amount, p == body.Length);
    }
}
