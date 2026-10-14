namespace Aion2DpsMeter.Core.Combat;

/// <summary>Anzeige für die Boss-Leiste.</summary>
/// <param name="Target">Kennung des Gegners.</param>
/// <param name="Hp">Rest-HP.</param>
/// <param name="MaxHp">Max-HP oder null (Erscheinen nicht gesehen, z. B. mitten im Kampf eingestiegen).</param>
/// <param name="TimeToKill">Geschätzte Zeit bis 0 HP aus der HP-Abnahme der letzten Sekunden, sonst null.</param>
/// <param name="NpcCode">Gegner-Nummer (für den Namen), falls beim Erscheinen gesehen.</param>
/// <param name="TopHit">Höchster Einzeltreffer auf diesen Gegner (von den gezählten Spielern), falls bekannt.</param>
/// <param name="FightStart">Erster Treffer der gezählten Spieler auf diesen Gegner (für den Berserk-Countdown).</param>
/// <param name="Deadline">Zeitlimit (Berserk) laut Spiel (21 8D), falls gemeldet.</param>
public sealed record BossStatus(uint Target, long Hp, long? MaxHp, TimeSpan? TimeToKill, int? NpcCode = null, long? TopHit = null, DateTimeOffset? FightStart = null, DateTimeOffset? Deadline = null)
{
    /// <summary>Rest in Prozent (0–1) oder null ohne Max-HP.</summary>
    public double? Fraction => MaxHp is > 0 and var max ? Math.Clamp((double)Hp / max, 0, 1) : null;
}

/// <summary>
/// Merkt sich die zuletzt gemeldete HP je Gegner (00 8D) und die Max-HP beim Erscheinen (41 36). Die Zeit bis zum Kill
/// wird aus der HP selbst geschätzt, nicht aus dem gemessenen Schaden, weil bei großen Kämpfen nur ein Teil der
/// Treffer anderer Spieler sichtbar ist (README, v14). Threadsicher.
/// </summary>
public sealed class EnemyHpTracker
{
    /// <summary>Über diesen Zeitraum wird die HP-Abnahme für die Kill-Schätzung gemittelt.</summary>
    public static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(10);

    /// <summary>So lange gilt eine HP-Meldung als aktuell.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private readonly Dictionary<uint, Enemy> _enemies = new();
    private readonly Dictionary<uint, DateTimeOffset> _deadlines = new();

    /// <summary>
    /// Kampfzustand übernehmen: Beginn mit Deadline merkt das Zeitlimit (Berserk), Kampfende ohne Deadline vergisst es.
    /// </summary>
    public void ReportBattleState(Analysis.BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_lock)
        {
            if (state.Deadline is { } deadline) _deadlines[state.Target] = deadline;
            else if (!state.Engaged) _deadlines.Remove(state.Target);
            if (_deadlines.Count > 500) _deadlines.Clear();
        }
    }

    /// <summary>HP-Meldung aufnehmen. <paramref name="maxHp"/> nur beim Erscheinen (setzt den Verlauf zurück).</summary>
    public void Report(uint target, long hp, long? maxHp, DateTimeOffset time, int? npcCode = null)
    {
        lock (_lock)
        {
            if (maxHp is not null || !_enemies.TryGetValue(target, out var enemy))
            {
                enemy = new Enemy { MaxHp = maxHp, NpcCode = npcCode };
                _enemies[target] = enemy;
            }

            // Meldet ein Gegner mehr HP als das bekannte Max, stimmt das Max nicht (z. B. Kennung neu vergeben).
            if (enemy.MaxHp is { } max && hp > max) enemy.MaxHp = null;
            enemy.Samples.Add((time, hp));
            enemy.Samples.RemoveAll(s => s.Time < time - RateWindow);
            if (_enemies.Count > 2000) Prune(time);
        }
    }

    /// <summary>Status eines Gegners oder null, wenn keine (aktuelle) HP bekannt ist.</summary>
    public BossStatus? Status(uint target, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_enemies.TryGetValue(target, out var enemy) || enemy.Samples.Count == 0) return null;
            var (lastTime, hp) = enemy.Samples[^1];
            if (now - lastTime > StaleAfter) return null;

            TimeSpan? ttk = null;
            var (firstTime, firstHp) = enemy.Samples[0];
            var seconds = (lastTime - firstTime).TotalSeconds;
            if (hp == 0) ttk = TimeSpan.Zero;
            else if (seconds >= 2 && firstHp > hp)
            {
                var rate = (firstHp - hp) / seconds;
                ttk = TimeSpan.FromSeconds(Math.Min(hp / rate, 359_999));
            }

            return new BossStatus(target, hp, enemy.MaxHp, ttk, enemy.NpcCode, Deadline: _deadlines.TryGetValue(target, out var d) ? d : null);
        }
    }

    /// <summary>Alles vergessen (z. B. neue Aufnahme).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _enemies.Clear();
            _deadlines.Clear();
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var id in _enemies.Where(e => e.Value.Samples.Count == 0 || now - e.Value.Samples[^1].Time > StaleAfter).Select(e => e.Key).ToList())
        {
            _enemies.Remove(id);
        }
    }

    private sealed class Enemy
    {
        public long? MaxHp;
        public int? NpcCode;
        public readonly List<(DateTimeOffset Time, long Hp)> Samples = new();
    }
}
