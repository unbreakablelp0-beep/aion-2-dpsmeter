namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Sammelt Schadensereignisse eines Kampfes und berechnet daraus die Statistik.
/// Nicht threadsicher.
/// </summary>
/// <remarks>
/// Kampfzeit = letzter minus erster bestätigter Treffer.
/// DPS = Schaden / Kampfzeit in Sekunden (für jeden Spieler dieselbe Kampfzeit).
/// Bei einer Kampfzeit von 0 (z. B. nur ein Treffer) ist DPS nicht berechenbar (null).
/// Unsichere Ereignisse werden nur gezählt, nie berechnet.
/// </remarks>
public sealed class CombatSession
{
    private readonly Dictionary<long, Combatant> _combatants = new();
    private readonly List<DamageEvent> _confirmedEvents = new();
    private int _unconfirmedEventCount;

    /// <summary>Registriert einen Spieler oder aktualisiert ihn (z. B. den Namen).</summary>
    public void RegisterCombatant(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        _combatants[combatant.Id] = combatant;
    }

    /// <summary>Fügt ein Schadensereignis hinzu.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Bei negativem Schaden.</exception>
    public void AddDamage(DamageEvent damageEvent)
    {
        ArgumentNullException.ThrowIfNull(damageEvent);
        ArgumentOutOfRangeException.ThrowIfNegative(damageEvent.Amount, nameof(damageEvent));

        if (damageEvent.Confidence == DataConfidence.Confirmed)
        {
            _confirmedEvents.Add(damageEvent);
        }
        else
        {
            _unconfirmedEventCount++;
        }
    }

    /// <summary>Setzt den Kampf zurück. Registrierte Spieler bleiben erhalten.</summary>
    public void Reset()
    {
        _confirmedEvents.Clear();
        _unconfirmedEventCount = 0;
    }

    /// <summary>Berechnet die aktuelle Statistik.</summary>
    public CombatStatistics GetStatistics()
    {
        if (_confirmedEvents.Count == 0)
        {
            return new CombatStatistics(null, null, 0, null, [], _unconfirmedEventCount);
        }

        var start = _confirmedEvents.Min(e => e.Timestamp);
        var end = _confirmedEvents.Max(e => e.Timestamp);
        var seconds = (end - start).TotalSeconds;
        var totalDamage = _confirmedEvents.Sum(e => e.Amount);

        var perCombatant = _confirmedEvents
            .GroupBy(e => e.SourceId)
            .Select(g =>
            {
                var damage = g.Sum(e => e.Amount);
                return new CombatantStatistics(
                    g.Key,
                    _combatants.GetValueOrDefault(g.Key),
                    damage,
                    g.Count(),
                    g.Count(e => e.IsCritical),
                    PerSecond(damage, seconds),
                    totalDamage == 0 ? 0 : (double)damage / totalDamage);
            })
            .OrderByDescending(s => s.TotalDamage)
            .ThenBy(s => s.SourceId)
            .ToList();

        return new CombatStatistics(start, end, totalDamage, PerSecond(totalDamage, seconds), perCombatant, _unconfirmedEventCount);
    }

    private static double? PerSecond(long damage, double seconds) => seconds > 0 ? damage / seconds : null;
}
