namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Leitet die Klasse aus den Fertigkeitsnummern ab. Beobachtung an eigenen Aufnahmen (13.10.2026): Alle
/// Fertigkeiten eines Verursachers beginnen mit denselben zwei Ziffern (z. B. 14xxxxxx). Welche Klasse dahinter
/// steckt, ist nur eingetragen, wenn es belegt ist; sonst wird „Klasse NN“ angezeigt.
/// </summary>
public static class SkillClassMap
{
    /// <summary>
    /// Belegte Zuordnungen. Ergänzen, sobald eine weitere Klasse sicher bekannt ist.
    /// 14 = Bogenschütze: Angabe des Nutzers zu seinem eigenen Charakter (Aufnahme 010703, 186 von 186 Klassen-Fertigkeiten mit 14).
    /// 11, 13, 15, 16, 17: Angaben des Nutzers (13.10.2026, 01:32) nach Beobachtung im Spiel.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> Known = new Dictionary<int, string>
    {
        [11] = "Kantor",
        [12] = "Templer", // Angabe des Nutzers (13.10.2026, 09:26)
        [13] = "Assassine",
        [14] = "Bogenschütze",
        [15] = "Magier",
        [16] = "Beschwörer",
        [17] = "Kleriker",
        [18] = "Gladiator", // Angabe des Nutzers (13.10.2026, 09:39)
    };

    /// <summary>
    /// Zwei führende Ziffern einer achtstelligen Klassen-Fertigkeit (10.000.000–29.999.999), sonst null
    /// (z. B. 1010000 oder 30xxxxxx, das laut Community Gegenstände sind).
    /// </summary>
    public static int? Prefix(uint skill) => skill is >= 10_000_000 and <= 29_999_999 ? (int)(skill / 1_000_000) : null;

    /// <summary>Anzeigename zu einem Präfix.</summary>
    public static string Describe(int prefix) => Known.TryGetValue(prefix, out var name) ? name : $"Klasse {prefix}";

    /// <summary>true, wenn die Klasse zu diesem Präfix belegt ist.</summary>
    public static bool IsKnown(int prefix) => Known.ContainsKey(prefix);
}
