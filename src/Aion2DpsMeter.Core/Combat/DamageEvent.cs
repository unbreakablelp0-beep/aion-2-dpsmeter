namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Wie sicher ein Schadensereignis ist.
/// </summary>
public enum DataConfidence
{
    /// <summary>Aus nachweislich verstandenen Daten gewonnen; fließt in die Berechnung ein.</summary>
    Confirmed,

    /// <summary>Unsicher; wird nicht berechnet, sondern nur gezählt und gekennzeichnet.</summary>
    Unconfirmed,
}

/// <summary>
/// Ein einzelner Treffer.
/// </summary>
/// <param name="Timestamp">Zeitpunkt des Treffers.</param>
/// <param name="SourceId">Kennung des Verursachers (siehe <see cref="Combatant.Id"/>).</param>
/// <param name="TargetId">Kennung des Ziels.</param>
/// <param name="Amount">Schadenshöhe, nicht negativ.</param>
/// <param name="IsCritical">true bei kritischem Treffer.</param>
/// <param name="Confidence">Sicherheit der Daten.</param>
public sealed record DamageEvent(
    DateTimeOffset Timestamp,
    long SourceId,
    long TargetId,
    long Amount,
    bool IsCritical = false,
    DataConfidence Confidence = DataConfidence.Confirmed);
