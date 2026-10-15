namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Merkt sich die Platzierung der Zeilen und meldet, wer seit der letzten Anzeige aufgestiegen (+1) oder abgerutscht (−1) ist.
/// Neue Zeilen melden nichts (kein Pfeil beim Erscheinen); wer vorübergehend fehlt, wird vergessen.
/// </summary>
public sealed class RankTracker
{
    private Dictionary<uint, int> _last = new();

    /// <summary>Neue Reihenfolge übernehmen; Ergebnis: je Kennung +1 (überholt), −1 (überholt worden). Unveränderte fehlen.</summary>
    public IReadOnlyDictionary<uint, int> Update(IReadOnlyList<uint> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var changes = new Dictionary<uint, int>();
        var now = new Dictionary<uint, int>();
        for (var i = 0; i < order.Count; i++)
        {
            now[order[i]] = i;
            if (_last.TryGetValue(order[i], out var before) && before != i) changes[order[i]] = i < before ? 1 : -1;
        }

        _last = now;
        return changes;
    }

    /// <summary>Alles vergessen (neuer Kampf, Zeilen neu aufgebaut).</summary>
    public void Reset() => _last = new();
}
