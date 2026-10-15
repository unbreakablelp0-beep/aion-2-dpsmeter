namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Was als echter Boss gilt. Die Community-Gegnerliste markiert auch Objekte und Übungsziele als „Boss“ (z. B. Strahlendes
/// Totem, Trainingspuppe, Sandsack). Diese werden hier ausgeschlossen – für Boss-Leiste und Bestenliste.
/// </summary>
public static class BossRules
{
    /// <summary>Wortteile im Namen, die nie ein Boss sind (Objekte, Übungsziele). Groß-/Kleinschreibung egal.</summary>
    public static readonly IReadOnlyList<string> NotBossWords =
    [
        "Totem", "Trainingspuppe", "Sandsack", "Puppe", "Wachturm", "Reaktor", "Dimensionskern", "Kristall", "Siegelstein",
        "Säule", "Statue", "Generator",
    ];

    /// <summary>Name klingt nach Objekt/Übungsziel (siehe <see cref="NotBossWords"/>).</summary>
    public static bool IsObjectName(string? name) =>
        name is not null && NotBossWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Echter Boss für die Bestenliste: das Spiel meldet ein Zeitlimit (Berserk) ODER die Gegnerliste kennt ihn als Boss,
    /// er ist keine Übungspuppe und kein Objekt. Unbekannte Gegner zählen hier nicht – auch nicht mit sehr hohen HP.
    /// </summary>
    public static bool IsRealBoss(NpcInfo? npc, bool hasDeadline)
    {
        if (npc is not null && (npc.IsDummy || IsObjectName(npc.Name))) return false;
        return hasDeadline || npc is { IsBoss: true };
    }
}
