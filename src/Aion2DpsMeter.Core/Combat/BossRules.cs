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
    /// Berserk-Prognose: reicht die aktuelle DPS bis zum Zeitlimit? Erst nach <paramref name="minFight"/> Kampf (sonst
    /// springt die Schätzung) und nur mit Zeitlimit vom Spiel. Stufe 0 = schafft ihr (≥ 30 s Puffer), 1 = knapp, 2 = zu langsam.
    /// </summary>
    public static (string Text, int Level)? BerserkForecast(BossStatus boss, DateTimeOffset now, TimeSpan minFight)
    {
        ArgumentNullException.ThrowIfNull(boss);
        if (boss.Deadline is not { } deadline || boss.TimeToKill is not { } ttk || boss.Hp <= 0) return null;
        if (boss.FightStart is not { } start || now - start < minFight) return null;
        var left = deadline - now;
        if (left <= TimeSpan.Zero) return null;
        var margin = left - ttk;
        static string Mm(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        if (margin >= TimeSpan.FromSeconds(30)) return ($"✔ Schafft ihr – ca. {Mm(margin)} Puffer", 0);
        if (margin >= TimeSpan.Zero) return ($"⚠ Knapp – nur noch ca. {Mm(margin)} Puffer", 1);
        var more = (ttk.TotalSeconds / left.TotalSeconds - 1) * 100;
        return ($"✖ Zu wenig DPS – ca. {Mm(-margin)} zu langsam (ca. {Math.Ceiling(more):0} % mehr DPS nötig)", 2);
    }

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
