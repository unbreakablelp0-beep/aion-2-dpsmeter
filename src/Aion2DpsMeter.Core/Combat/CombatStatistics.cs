namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Statistik eines einzelnen Verursachers.
/// </summary>
/// <param name="SourceId">Kennung des Verursachers.</param>
/// <param name="Combatant">Bekannter Spieler oder null, wenn unbekannt.</param>
/// <param name="TotalDamage">Summe des bestätigten Schadens.</param>
/// <param name="Hits">Anzahl bestätigter Treffer.</param>
/// <param name="CriticalHits">Anzahl bestätigter kritischer Treffer.</param>
/// <param name="Dps">Schaden pro Sekunde bezogen auf die Kampfzeit; null, wenn die Kampfzeit 0 ist.</param>
/// <param name="DamageShare">Anteil am Gesamtschaden (0 bis 1); 0, wenn es keinen Schaden gibt.</param>
public sealed record CombatantStatistics(
    long SourceId,
    Combatant? Combatant,
    long TotalDamage,
    int Hits,
    int CriticalHits,
    double? Dps,
    double DamageShare)
{
    /// <summary>Anzeigename; „Unbekannt (Id)“, wenn der Spieler nicht registriert ist.</summary>
    public string DisplayName => Combatant?.Name ?? $"Unbekannt ({SourceId})";

    /// <summary>Anteil kritischer Treffer (0 bis 1); 0 ohne Treffer.</summary>
    public double CriticalRate => Hits == 0 ? 0 : (double)CriticalHits / Hits;
}

/// <summary>
/// Zusammenfassung eines Kampfes. Alle Werte stammen nur aus bestätigten Ereignissen.
/// </summary>
/// <param name="Start">Zeitpunkt des ersten bestätigten Treffers oder null.</param>
/// <param name="End">Zeitpunkt des letzten bestätigten Treffers oder null.</param>
/// <param name="TotalDamage">Gesamtschaden aller Verursacher.</param>
/// <param name="Dps">Gesamt-DPS; null, wenn die Kampfzeit 0 ist.</param>
/// <param name="Combatants">Statistik je Verursacher, absteigend nach Schaden sortiert.</param>
/// <param name="UnconfirmedEventCount">Anzahl unsicherer Ereignisse, die nicht berechnet wurden.</param>
public sealed record CombatStatistics(
    DateTimeOffset? Start,
    DateTimeOffset? End,
    long TotalDamage,
    double? Dps,
    IReadOnlyList<CombatantStatistics> Combatants,
    int UnconfirmedEventCount)
{
    /// <summary>Kampfzeit vom ersten bis zum letzten bestätigten Treffer.</summary>
    public TimeSpan Duration => Start is { } s && End is { } e ? e - s : TimeSpan.Zero;

    /// <summary>true, wenn unsichere Daten verworfen wurden und die Werte daher unvollständig sein können.</summary>
    public bool HasUncertainData => UnconfirmedEventCount > 0;
}
