using System.Buffers.Binary;
using System.Text;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>Kampfkraft (CP) und Ausrüstungswert (GS) eines Spielers; 0 = unbekannt.</summary>
public sealed record PlayerPower(long CombatPower, int GearScore);

/// <summary>
/// Kampfkraft nach der öffentlichen Beschreibung im GPL-3.0-Projekt cyberbadger6969/aion2-dps-meter (dort nach
/// A2Tools-DPS-Meter). Selbst geschrieben, kein Code übernommen:
/// <list type="bullet">
/// <item>Eigener Charakter (33 36): am Ende zweimal u64 – aktuelle und höchste Kampfkraft –, vom Ende her gesucht.</item>
/// <item>Gruppenliste (02 97), je Mitglied: Name (u8 + UTF-8), Klasse u32, Stufe u32, Ausrüstungswert u32 (manchmal ein
/// u32 davor), dann einige Flags, die Server-Nummer (u16) noch einmal, u16, ein Byte, Kampfkraft u64.</item>
/// </list>
/// </summary>
public static class CombatPowerHypothesis
{
    public const long Min = 1_000, Max = 10_000_000;

    /// <summary>Kampfkraft aus dem eigenen Datensatz (ganzer 33-36-Rahmen) oder 0.</summary>
    public static long FromSelfRecord(ReadOnlySpan<byte> body)
    {
        for (var o = body.Length - 16; o >= Math.Max(0, body.Length - 16 - 255); o--)
        {
            var current = BinaryPrimitives.ReadInt64LittleEndian(body[o..]);
            var highest = BinaryPrimitives.ReadInt64LittleEndian(body[(o + 8)..]);
            if (current is >= Min and <= Max && highest is >= Min and <= Max && current <= highest) return current;
        }

        return 0;
    }

    /// <summary>Kampfkraft und Ausrüstungswert eines Mitglieds (über seinen Namen) aus einer Gruppenliste, sonst null.</summary>
    public static PlayerPower? FromRoster(ReadOnlySpan<byte> roster, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var bytes = Encoding.UTF8.GetBytes(name);
        if (bytes.Length > 255) return null;
        Span<byte> needle = stackalloc byte[bytes.Length + 1];
        needle[0] = (byte)bytes.Length;
        bytes.CopyTo(needle[1..]);

        var p = roster.IndexOf(needle);
        if (p < 10) return null;
        var mask = roster[p - 10];
        var server = BinaryPrimitives.ReadUInt16LittleEndian(roster[(p - 2)..]); // oberste u16 der Kennung (dbid)
        var o = p + 1 + bytes.Length;
        if (o + 12 > roster.Length) return null;
        var gear = BinaryPrimitives.ReadInt32LittleEndian(roster[(o + 8)..]);
        o += 12;
        if ((mask & 0x01) != 0 && gear < 100 && o + 4 <= roster.Length)
        {
            var next = BinaryPrimitives.ReadInt32LittleEndian(roster[o..]);
            if (next is >= 100 and <= 50_000)
            {
                gear = next;
                o += 4;
            }
        }

        for (var at = o; at <= Math.Min(o + 10, roster.Length - 2); at++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(roster[at..]) != server) continue;
            if (at + 5 + 8 > roster.Length) return null;
            var power = BinaryPrimitives.ReadInt64LittleEndian(roster[(at + 5)..]);
            return power is >= Min and <= Max
                ? new PlayerPower(power, gear is >= 1 and <= 50_000 ? gear : 0)
                : null;
        }

        // Manche Einträge (in der Aufnahme 20261014-164636 einer von fünf) tragen in der Kennung eine andere Server-
        // Nummer als im Feld danach. Dann steht die Server-Nummer direkt nach dem Ausrüstungswert, die Kampfkraft 5 Bytes später.
        if (o + 5 + 8 <= roster.Length)
        {
            var direct = BinaryPrimitives.ReadInt64LittleEndian(roster[(o + 5)..]);
            if (direct is >= Min and <= Max) return new PlayerPower(direct, gear is >= 1 and <= 50_000 ? gear : 0);
        }

        return null;
    }
}
