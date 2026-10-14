namespace Aion2DpsMeter.Core.Combat;

/// <summary>DPS-Verlauf: Schaden je Sekunde in eine geglättete Kurve umrechnen (gleitender Schnitt).</summary>
public static class DpsTimeline
{
    /// <summary>Standard-Glättung: Schnitt über die letzten so vielen Sekunden.</summary>
    public const int DefaultWindowSeconds = 5;

    /// <summary>Schaden je Sekunde aus Treffern (Zeit, Betrag) ab <paramref name="start"/> bis zum letzten Treffer.</summary>
    public static IReadOnlyList<long> PerSecond(IEnumerable<(DateTimeOffset Time, long Amount)> hits, DateTimeOffset start)
    {
        ArgumentNullException.ThrowIfNull(hits);
        var buckets = new List<long>();
        foreach (var (time, amount) in hits)
        {
            var second = (int)Math.Floor((time - start).TotalSeconds);
            if (second < 0 || second > 36_000) continue; // höchstens 10 h
            while (buckets.Count <= second) buckets.Add(0);
            buckets[second] += amount;
        }

        return buckets;
    }

    /// <summary>Geglättete DPS je Sekunde: Schnitt der letzten <paramref name="window"/> Sekunden (am Anfang über die vorhandenen).</summary>
    public static IReadOnlyList<double> Smooth(IReadOnlyList<long> perSecond, int window = DefaultWindowSeconds)
    {
        ArgumentNullException.ThrowIfNull(perSecond);
        window = Math.Max(1, window);
        var result = new double[perSecond.Count];
        long sum = 0;
        for (var i = 0; i < perSecond.Count; i++)
        {
            sum += perSecond[i];
            if (i >= window) sum -= perSecond[i - window];
            result[i] = (double)sum / Math.Min(i + 1, window);
        }

        return result;
    }
}
