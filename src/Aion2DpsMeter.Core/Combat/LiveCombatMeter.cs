namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Ein Treffer aus dem Live-Datenstrom. Die Felder stammen aus einer Community-Hypothese (Typ 04 38 bzw.
/// 05 38), die am 13.10.2026 an eigenen Aufnahmen abgeglichen wurde (Einzelwerte exakt, Summe 99,4 % bzw.
/// mit 05 38 genau) – sie gelten trotzdem nicht als gesichert.
/// </summary>
/// <param name="Time">Zeitpunkt des Pakets.</param>
/// <param name="Actor">Mutmaßlicher Verursacher.</param>
/// <param name="Target">Mutmaßliches Ziel.</param>
/// <param name="Skill">Mutmaßliche Fertigkeit.</param>
/// <param name="Amount">Schaden (Kandidatenwert).</param>
/// <param name="IsCritical">Laut Hypothese kritischer Treffer.</param>
/// <param name="OverTime">true für Schaden über Zeit (05 38).</param>
/// <param name="Modifier">Modifikatorbyte aus 04 38 (laut Community 0x02 Parade, 0x04 Perfekt, 0x08 Doppelt) oder null; nicht abgeglichen.</param>
/// <param name="Direction">Richtungsbyte aus 04 38 (laut Community 0x01 Rücken, 0x02 Front) oder null; nicht abgeglichen.</param>
/// <param name="ExtraHits">Anzahl Zusatzwerte (laut Community Mehrfachtreffer); nicht abgeglichen.</param>
public sealed record CombatHit(
    DateTimeOffset Time,
    uint Actor,
    uint Target,
    uint Skill,
    long Amount,
    bool IsCritical,
    bool OverTime,
    byte? Modifier = null,
    byte? Direction = null,
    int ExtraHits = 0);

/// <summary>Werte eines Verursachers im aktuellen Kampf.</summary>
/// <param name="Actor">Kennung (gilt vermutlich nur für die laufende Spielsitzung).</param>
/// <param name="Damage">Schaden gesamt (direkt + über Zeit).</param>
/// <param name="Hits">Direkte Treffer.</param>
/// <param name="CriticalHits">Davon kritisch.</param>
/// <param name="OverTimeDamage">Davon Schaden über Zeit.</param>
/// <param name="FirstHit">Erster Treffer dieses Verursachers.</param>
/// <param name="Dps">Schaden ÷ Zeit vom eigenen ersten Treffer bis zum letzten Treffer im Kampf (mindestens 1 s).</param>
/// <param name="Share">Anteil am Gesamtschaden (0–1).</param>
/// <param name="IsOwn">true, wenn eigener Charakter (von Hand gewählt oder in dieser Programmsitzung erkannt).</param>
/// <param name="IsRememberedOwn">
/// true, wenn die Kennung nur aus einer früheren Programmsitzung gemerkt ist – unsicher (nach Charakterwechsel oder
/// neuem Login falsch); wird nicht als <see cref="IsOwn"/> gewertet.
/// </param>
/// <param name="Name">Name laut Spielernachricht (Hypothese) oder null, wenn unbekannt.</param>
/// <param name="ClassPrefix">Häufigstes Fertigkeits-Präfix (siehe <see cref="SkillClassMap"/>) oder null.</param>
/// <param name="PetDamage">Davon Schaden eigener Pets/Beschwörungen (Besitzer aus Typ 41 36).</param>
/// <param name="IsLikelyPet">
/// Verursacher ohne Namen mit Pet-typischen Fertigkeiten (100.000–999.999), dessen Besitzer nicht erkannt wurde
/// (z. B. vor Aufnahmebeginn beschworen). Nur eine Beobachtung an eigenen Aufnahmen.
/// </param>
public sealed record LiveActorStatistics(
    uint Actor,
    long Damage,
    int Hits,
    int CriticalHits,
    long OverTimeDamage,
    DateTimeOffset FirstHit,
    double Dps,
    double Share,
    bool IsOwn,
    string? Name = null,
    int? ClassPrefix = null,
    long PetDamage = 0,
    bool IsLikelyPet = false,
    bool IsRememberedOwn = false,
    DateTimeOffset? LastHit = null,
    int Deaths = 0)
{
    /// <summary>Klasse zum Anzeigen („Bogenschütze“, „Klasse 12“) oder null.</summary>
    public string? ClassName => ClassPrefix is { } p ? SkillClassMap.Describe(p) : null;
}

/// <summary>Art eines Treffers in der Aufschlüsselung.</summary>
public enum HitKind
{
    /// <summary>Direkter Treffer (04 38).</summary>
    Direct,

    /// <summary>Schaden über Zeit (05 38).</summary>
    OverTime,

    /// <summary>Treffer eines Pets/einer Beschwörung, dem Besitzer zugerechnet.</summary>
    Pet,
}

/// <summary>
/// Zählungen nach Community-Angaben zu Modifikator-/Richtungsbyte und Zusatzwerten in 04 38 – <b>nicht abgeglichen</b>.
/// Quoten beziehen sich jeweils nur auf Treffer, bei denen das Feld vorhanden war.
/// </summary>
public sealed record CommunityFlags(
    int WithModifier,
    int Parry,
    int Perfect,
    int Double,
    int WithDirection,
    int Back,
    int Front,
    int DirectHits,
    int MultiHits)
{
    public double ParryRate => Rate(Parry, WithModifier);

    public double PerfectRate => Rate(Perfect, WithModifier);

    public double DoubleRate => Rate(Double, WithModifier);

    public double BackRate => Rate(Back, WithDirection);

    public double FrontRate => Rate(Front, WithDirection);

    public double MultiRate => Rate(MultiHits, DirectHits);

    private static double Rate(int part, int total) => total == 0 ? 0 : (double)part / total;
}

/// <summary>Werte einer Fähigkeit (Nummer aus den Daten) in der Aufschlüsselung.</summary>
/// <param name="Skill">Fertigkeitsnummer.</param>
/// <param name="Kind">Direkt, über Zeit oder Pet.</param>
/// <param name="Damage">Schaden.</param>
/// <param name="Share">Anteil am Schaden des Verursachers (0–1).</param>
/// <param name="Hits">Treffer bzw. Ticks.</param>
/// <param name="CriticalHits">Kritische (laut Hypothese Art = 3).</param>
/// <param name="Max">Größter Einzelwert.</param>
/// <param name="Average">Durchschnitt.</param>
/// <param name="Flags">Community-Zählungen (nicht abgeglichen) oder null.</param>
public sealed record SkillStatistics(uint Skill, HitKind Kind, long Damage, double Share, int Hits, int CriticalHits, long Max, double Average, CommunityFlags? Flags = null)
{
    public double CriticalRate => Hits == 0 ? 0 : (double)CriticalHits / Hits;
}

/// <summary>Schaden gegen ein Ziel.</summary>
public sealed record TargetDamage(uint Target, string? Name, long Damage);

/// <summary>Aufschlüsselung eines Verursachers.</summary>
/// <param name="Duration">Vom ersten (gefilterten) Treffer bis zum letzten Treffer im Kampf.</param>
/// <param name="Hits">Direkte Treffer inkl. Pet (ohne Ticks über Zeit).</param>
/// <param name="CriticalHits">Davon kritisch.</param>
/// <param name="Targets">Alle Ziele dieses Verursachers (ungefiltert), absteigend nach Schaden.</param>
/// <param name="Skills">Fähigkeiten, absteigend nach Schaden.</param>
public sealed record ActorDetail(
    uint Actor,
    string? Name,
    int? ClassPrefix,
    TimeSpan Duration,
    long Damage,
    double Dps,
    int Hits,
    int CriticalHits,
    IReadOnlyList<TargetDamage> Targets,
    IReadOnlyList<SkillStatistics> Skills,
    CommunityFlags? Flags = null,
    TimeSpan ActiveTime = default,
    int HitsWithExtra = 0)
{
    /// <summary>Pausen ab dieser Länge zählen nicht zur aktiven Zeit (Annahme, nicht vom Spiel übernommen).</summary>
    public static readonly TimeSpan ActiveGapLimit = TimeSpan.FromSeconds(5);

    /// <summary>Schaden ÷ aktive Zeit (mindestens 1 s).</summary>
    public double ActiveDps => Damage / Math.Max(1.0, ActiveTime.TotalSeconds);

    /// <summary>Ab so viel aktiver Zeit ist „DPS (aktiv)“ aussagekräftig (eigene Annahme).</summary>
    public static readonly TimeSpan MinActiveTime = TimeSpan.FromSeconds(3);

    /// <summary><see cref="ActiveDps"/> erst ab <see cref="MinActiveTime"/>, sonst null (z. B. alle Treffer fast gleichzeitig).</summary>
    public double? ActiveDpsIfMeaningful => ActiveTime >= MinActiveTime ? ActiveDps : null;

    public string? ClassName => ClassPrefix is { } p ? SkillClassMap.Describe(p) : null;

    public double CriticalRate => Hits == 0 ? 0 : (double)CriticalHits / Hits;
}

/// <summary>Momentaufnahme des aktuellen (oder zuletzt beendeten) Kampfs.</summary>
/// <param name="FightNumber">Laufende Nummer des Kampfs seit Start/Zurücksetzen (0 = noch keiner).</param>
/// <param name="Start">Erster Treffer.</param>
/// <param name="LastHit">Letzter Treffer.</param>
/// <param name="Active">false, wenn seit dem letzten Treffer die Leerlaufzeit vergangen ist.</param>
/// <param name="TotalDamage">Schaden aller Verursacher.</param>
/// <param name="Dps">Gesamtschaden ÷ Kampfdauer (mindestens 1 s); null ohne Treffer.</param>
/// <param name="Actors">Verursacher, absteigend nach Schaden.</param>
/// <param name="IgnoredSelfHits">Verworfene Treffer mit Verursacher = Ziel (laut Hypothese Selbstheilung).</param>
public sealed record LiveCombatSnapshot(
    int FightNumber,
    DateTimeOffset? Start,
    DateTimeOffset? LastHit,
    bool Active,
    long TotalDamage,
    double? Dps,
    IReadOnlyList<LiveActorStatistics> Actors,
    long IgnoredSelfHits,
    DateTimeOffset? End = null)
{
    /// <summary>Kampfdauer: bei laufendem Kampf bis „jetzt“, sonst bis zum letzten Treffer.</summary>
    public TimeSpan Duration => Start is { } s && (End ?? LastHit) is { } e ? e - s : TimeSpan.Zero;
}

/// <summary>
/// Zählt Treffer live je Verursacher. Ein neuer Kampf beginnt, wenn nach der Leerlaufzeit wieder ein Treffer
/// kommt; bis dahin bleibt der letzte Kampf sichtbar. Threadsicher (Treffer kommen vom Aufnahme-Thread,
/// gelesen wird von der Oberfläche).
/// </summary>
public sealed class LiveCombatMeter
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly Dictionary<uint, Accumulator> _actors = new();
    private DateTimeOffset? _start, _lastHit;
    private int _fight;
    private long _ignoredSelf;
    private uint? _manualOwn, _detectedOwn;
    private RememberedOwnCharacter? _remembered;
    private readonly Dictionary<uint, string> _names = new();

    // Fertigkeits-Präfixe je Verursacher (für die Klasse); bleiben wie die Namen über Kämpfe hinweg erhalten.
    private readonly Dictionary<uint, Dictionary<int, int>> _classes = new();

    // Beschwörung → Besitzer (Typ 41 36) und Verursacher mit Pet-typischen Fertigkeiten (100.000–999.999).
    private readonly Dictionary<uint, uint> _petOwner = new();
    private readonly HashSet<uint> _petSkillUsers = new();
    private long _petConflicts;

    public LiveCombatMeter(TimeSpan? idleTimeout = null)
    {
        IdleTimeout = idleTimeout ?? DefaultIdleTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdleTimeout, TimeSpan.Zero, nameof(idleTimeout));
    }

    /// <summary>Kampfende nach so langer Zeit ohne Treffer (einstellbar, größer 0).</summary>
    public TimeSpan IdleTimeout
    {
        get { lock (_lock) return _idleTimeout; }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            lock (_lock) _idleTimeout = value;
        }
    }

    private TimeSpan _idleTimeout;

    /// <summary>
    /// Verzögerung, mit der Treffer ankommen (z. B. 2 s Zurückhalten der Programmauswahl). Wird von „jetzt“ abgezogen,
    /// damit die laufende Kampfzeit nicht künstlich länger wird.
    /// </summary>
    public TimeSpan Lag { get; set; }

    /// <summary>
    /// Eigener Charakter: von Hand gewählt, sonst automatisch erkannt (<see cref="DetectedOwnActor"/>).
    /// Setzen wählt von Hand; null schaltet zurück auf „automatisch“.
    /// </summary>
    public uint? OwnActor
    {
        get { lock (_lock) return CurrentOwn(); }
        set { lock (_lock) _manualOwn = value; }
    }

    private Analysis.PartyRoster? _party;
    private readonly HashSet<uint> _markedEnemies = new();

    /// <summary>Kennung als Gegner merken (z. B. weil eine Gegner-HP-Meldung 00 8D für sie kam).</summary>
    public void MarkEnemy(uint id)
    {
        lock (_lock)
        {
            if (_markedEnemies.Count < 50_000) _markedEnemies.Add(id);
        }
    }

    /// <summary>
    /// Belegte Gegner, die nicht als Spieler zählen: Kennungen mit Gegner-HP-Meldung, Ziele von Treffern des eigenen
    /// Charakters oder benannter Spieler und namenlose Verursacher, die benannte Spieler treffen. Spieler mit Namen sind
    /// nie Gegner. Namenlose, die nur fremde Ziele treffen (wohl Spieler ohne empfangenen Namen), bleiben. Nur unter
    /// <c>_lock</c> aufrufen.
    /// </summary>
    private HashSet<uint> Enemies()
    {
        var friendly = new HashSet<uint>(_names.Keys);
        if (CurrentOwn() is { } own) friendly.Add(own);
        var enemies = new HashSet<uint>(_markedEnemies);
        foreach (var id in friendly)
        {
            if (!_actors.TryGetValue(id, out var acc)) continue;
            foreach (var r in acc.Records) enemies.Add(r.Target);
        }

        // Namenlose Verursacher, die Spieler mit Namen (oder mich) treffen, sind ebenfalls Gegner.
        foreach (var (id, acc) in _actors)
        {
            if (!friendly.Contains(id) && !enemies.Contains(id) && acc.Records.Any(r => friendly.Contains(r.Target))) enemies.Add(id);
        }

        enemies.ExceptWith(friendly);
        return enemies;
    }

    /// <summary>Anzahl der belegten Gegner, die im aktuellen Kampf selbst Schaden gemacht haben (aus der Liste genommen).</summary>
    public int HiddenEnemyActors
    {
        get
        {
            lock (_lock)
            {
                var enemies = Enemies();
                return _actors.Keys.Count(enemies.Contains);
            }
        }
    }

    /// <summary>Neueste Gruppenliste merken (nur im Speicher).</summary>
    public void ReportParty(Analysis.PartyRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        lock (_lock) _party = roster;
    }

    private long _ownCombatPower;

    /// <summary>Eigene Kampfkraft merken (aus dem eigenen Datensatz 33 36).</summary>
    public void ReportOwnCombatPower(long combatPower)
    {
        lock (_lock) _ownCombatPower = combatPower;
    }

    /// <summary>
    /// Kampfkraft eines Spielers: eigener Charakter aus 33 36 (sonst Gruppenliste), Gruppenmitglieder aus der letzten
    /// Gruppenliste (über den Namen). Null, wenn unbekannt (z. B. fremde Spieler – das Spiel schickt sie dafür nicht).
    /// </summary>
    public long? CombatPowerOf(uint actor)
    {
        lock (_lock)
        {
            if (actor == CurrentOwn() && _ownCombatPower > 0) return _ownCombatPower;
            return _party is { } party && _names.TryGetValue(actor, out var name) && party.PowerOf(name) is { CombatPower: > 0 } p
                ? p.CombatPower
                : null;
        }
    }

    /// <summary>Schaden je Sekunde eines Spielers im aktuellen Kampf (ab Kampfbeginn); null ohne Treffer.</summary>
    public IReadOnlyList<long>? DamagePerSecond(uint actor)
    {
        lock (_lock)
        {
            if (_start is not { } start || !_actors.TryGetValue(actor, out var acc) || acc.Records.Count == 0) return null;
            return DpsTimeline.PerSecond(acc.Records.Select(r => (r.Time, r.Amount)), start);
        }
    }

    /// <summary>Erster Treffer auf <paramref name="target"/> im aktuellen Kampf von <paramref name="sources"/> (Start für den Berserk-Countdown).</summary>
    public DateTimeOffset? FirstHitOn(uint target, IReadOnlySet<uint> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        lock (_lock)
        {
            DateTimeOffset? first = null;
            foreach (var (id, acc) in _actors)
            {
                if (!sources.Contains(id)) continue;
                foreach (var r in acc.Records)
                {
                    if (r.Target == target && (first is null || r.Time < first)) first = r.Time;
                }
            }

            return first;
        }
    }

    /// <summary>Höchster Einzeltreffer auf <paramref name="target"/> im aktuellen Kampf, nur von <paramref name="sources"/>; null ohne Treffer.</summary>
    public long? TopHit(uint target, IReadOnlySet<uint> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        lock (_lock)
        {
            long? top = null;
            foreach (var (id, acc) in _actors)
            {
                if (!sources.Contains(id)) continue;
                foreach (var r in acc.Records)
                {
                    if (r.Target == target && r.Kind != HitKind.OverTime && (top is null || r.Amount > top)) top = r.Amount;
                }
            }

            return top;
        }
    }

    /// <summary>
    /// Gemerkte Gruppe übernehmen (Namen aus einer früheren Sitzung), solange in dieser Sitzung keine echte Gruppenliste
    /// kam. Eine echte Liste ersetzt sie sofort.
    /// </summary>
    public void RememberParty(int size, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        lock (_lock)
        {
            if (_party is { IsRemembered: false }) return;
            _party = new Analysis.PartyRoster(size, names);
        }
    }

    /// <summary>
    /// Ein Austritt aus der Gruppe ist in den Paketen nicht erkennbar. Deshalb wird eine nur gemerkte Gruppe verworfen,
    /// sobald ein Kampf von mindestens 20 s nur mit mir (ohne ein gemerktes Mitglied) endet. true, wenn verworfen.
    /// </summary>
    public bool ForgetRememberedPartyAfterSoloFight(FightRecord fight)
    {
        ArgumentNullException.ThrowIfNull(fight);
        lock (_lock)
        {
            if (_party is not { IsRemembered: true } party || fight.End - fight.Start < TimeSpan.FromSeconds(20)) return false;
            if (!fight.Players.Any(p => p.IsOwn && p.Damage > 0)) return false;
            if (fight.Players.Any(p => !p.IsOwn && p.Name is { } n && party.Contains(n))) return false;
            _party = null;
            return true;
        }
    }

    /// <summary>Namen der erkannten Gruppenmitglieder (inkl. ich) und die Größe – zum lokalen Merken.</summary>
    public (int Size, IReadOnlyList<string> Names)? PartySnapshot()
    {
        lock (_lock)
        {
            if (_party is not { } party) return null;
            var names = _names.Values.Where(party.Contains).Distinct().ToList();
            return names.Count == 0 ? null : (party.Size, names);
        }
    }

    /// <summary>true, wenn die aktuelle Gruppe nur gemerkt ist (keine Gruppenliste in dieser Sitzung).</summary>
    public bool PartyIsRemembered
    {
        get { lock (_lock) return _party?.IsRemembered == true; }
    }

    /// <summary>Gruppengröße laut letzter Gruppenliste oder null (keine Gruppe gesehen).</summary>
    public int? PartySize
    {
        get { lock (_lock) return _party?.Size; }
    }

    /// <summary>Kennungen der Spieler, deren Name in der letzten Gruppenliste steht (leer ohne Gruppe).</summary>
    public IReadOnlySet<uint> PartyMembers()
    {
        lock (_lock)
        {
            if (_party is not { } party) return new HashSet<uint>();
            return _names.Where(n => party.Contains(n.Value)).Select(n => n.Key).ToHashSet();
        }
    }

    /// <summary>
    /// Das Ziel, das der eigene Charakter in den letzten <paramref name="window"/> am meisten getroffen hat (Schaden).
    /// Null, wenn keiner der Verursacher in dem Zeitraum etwas getroffen hat (bzw. der eigene Charakter unbekannt ist).
    /// </summary>
    /// <param name="sources">
    /// Wessen Treffer zählen: null = eigener Charakter (Standard); sonst genau diese Kennungen (z. B. ich + Gruppe, oder alle).
    /// </param>
    public uint? MainTarget(DateTimeOffset now, TimeSpan window, IReadOnlySet<uint>? sources = null)
    {
        lock (_lock)
        {
            // Ohne erkannten eigenen Charakter gibt es standardmäßig kein Hauptziel (sonst zeigt die Boss-Leiste
            // z. B. eine Trainingspuppe, die gerade andere Spieler schlagen).
            var from = now - window;
            IReadOnlySet<uint> ids = sources ?? (CurrentOwn() is { } own ? new HashSet<uint> { own } : new HashSet<uint>());
            return _actors.Where(kv => ids.Contains(kv.Key)).SelectMany(kv => kv.Value.Records)
                .Where(r => r.Time >= from && r.Time <= now)
                .GroupBy(r => r.Target)
                .Select(g => (Target: g.Key, Damage: g.Sum(r => r.Amount)))
                .OrderByDescending(g => g.Damage)
                .Select(g => (uint?)g.Target)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Gemerkter eigener Charakter aus einer früheren Programmsitzung (gilt nur, solange seitdem nicht neu eingeloggt
    /// wurde). Wird verwendet, bis ein neues 33 36 kommt; verworfen, wenn ein anderer Spieler mit dieser Kennung und
    /// anderem Namen gemeldet wird.
    /// </summary>
    public RememberedOwnCharacter? RememberedOwn
    {
        get { lock (_lock) return _remembered; }
    }

    /// <summary>Setzt den gemerkten eigenen Charakter (Name wird für die Kennung übernommen).</summary>
    public void Remember(RememberedOwnCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        lock (_lock)
        {
            _remembered = character;
            _names.TryAdd(character.Id, character.Name);
            if (character.ClassPrefix is { } prefix) _rememberedClass = (character.Name, prefix);
        }
    }

    // Gemerkte Klasse des eigenen Charakters (an den Namen gebunden); bleibt auch nach neuem Einloggen gültig.
    private (string Name, int Prefix)? _rememberedClass;
    private readonly Dictionary<uint, List<DateTimeOffset>> _deaths = new();

    // Gemerkte Kennungen zählen bewusst nicht: sie sind nach Charakterwechsel/neuem Login falsch (13.10.2026 beobachtet).
    private uint? CurrentOwn() => _manualOwn ?? _detectedOwn;

    /// <summary>Automatisch erkannter eigener Charakter (laut Hypothese aus Typ 33 36) oder null.</summary>
    public uint? DetectedOwnActor
    {
        get { lock (_lock) return _detectedOwn; }
    }

    /// <summary>true, wenn der eigene Charakter von Hand gewählt wurde.</summary>
    public bool OwnActorIsManual
    {
        get { lock (_lock) return _manualOwn is not null; }
    }

    /// <summary>
    /// Merkt sich den Namen einer Kennung (nur im Speicher). <paramref name="isSelf"/> = laut Hypothese der eigene
    /// Charakter; dann wird er automatisch erkannt.
    /// </summary>
    public void ReportPlayer(uint id, string name, bool isSelf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_lock)
        {
            _names[id] = name;
            if (isSelf)
            {
                _detectedOwn = id;
                _remembered = null; // frisch erkannt – der gemerkte Stand ist überholt
            }
            else if (_remembered is { } r && r.Id == id && r.Name != name)
            {
                _remembered = null; // Kennung gehört inzwischen jemand anderem (neu eingeloggt)
            }
        }
    }

    /// <summary>Bekannter Name einer Kennung oder null.</summary>
    public string? NameOf(uint id)
    {
        lock (_lock) return _names.GetValueOrDefault(id);
    }

    /// <summary>Nimmt einen Treffer auf. Treffer mit Verursacher = Ziel werden nur gezählt, nicht gewertet.</summary>
    public void Add(CombatHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        lock (_lock)
        {
            if (hit.Actor == hit.Target)
            {
                _ignoredSelf++;
                return;
            }

            if (_lastHit is { } last && hit.Time - last > IdleTimeout)
            {
                // Alter Kampf ist vorbei: für den Verlauf festhalten, falls ihn noch niemand abgeholt hat.
                if (_exportedFight != _fight && BuildFightRecord() is { } finished) _pendingFights.Add(finished);
                _exportedFight = _fight;
                _actors.Clear();
                _start = null;
            }

            if (_start is null) _fight++;
            _start ??= hit.Time;
            if (hit.Time < _start) _start = hit.Time;
            if (_lastHit is null || hit.Time > _lastHit) _lastHit = hit.Time;

            // Pet/Beschwörung mit bekanntem Besitzer: dem Besitzer zurechnen – aber nur, wenn die Klasse nicht widerspricht.
            var actor = hit.Actor;
            var fromPet = false;
            if (_petOwner.TryGetValue(hit.Actor, out var owner))
            {
                var hitPrefix = SkillClassMap.Prefix(hit.Skill);
                var ownerPrefix = MainPrefix(owner);
                if (hitPrefix is not null && ownerPrefix is not null && hitPrefix != ownerPrefix)
                {
                    _petConflicts++;
                }
                else
                {
                    actor = owner;
                    fromPet = true;
                }
            }

            if (hit.Skill is >= 100_000 and <= 999_999) _petSkillUsers.Add(hit.Actor);

            if (SkillClassMap.Prefix(hit.Skill) is { } prefix)
            {
                if (!_classes.TryGetValue(actor, out var counts))
                {
                    counts = new Dictionary<int, int>();
                    _classes[actor] = counts;
                }

                counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
            }

            if (!_actors.TryGetValue(actor, out var acc))
            {
                acc = new Accumulator(hit.Time);
                _actors[actor] = acc;
            }

            acc.Add(hit, fromPet);
        }
    }

    /// <summary>Abstand, unter dem eine weitere 0-HP-Meldung desselben Spielers kein neuer Tod ist.</summary>
    public static readonly TimeSpan DeathDebounce = TimeSpan.FromSeconds(5);

    /// <summary>
    /// HP eines Spielers fiel auf 0 (1B 92 „tot“ oder 00 8D). Zählt nur bekannte Spieler (mit Namen) und höchstens einmal
    /// je <see cref="DeathDebounce"/>; Gegner-Kennungen werden ignoriert.
    /// </summary>
    public void ReportDeath(uint actor, DateTimeOffset time)
    {
        lock (_lock)
        {
            if (!_names.ContainsKey(actor)) return;
            if (!_deaths.TryGetValue(actor, out var list))
            {
                list = [];
                _deaths[actor] = list;
            }

            if (list.Count > 0 && (time - list[^1]).Duration() < DeathDebounce) return;
            list.Add(time);
            if (list.Count > 200) list.RemoveRange(0, 100);
        }
    }

    private int DeathsIn(uint actor, DateTimeOffset from, DateTimeOffset to) =>
        _deaths.TryGetValue(actor, out var list) ? list.Count(t => t >= from && t <= to) : 0;

    /// <summary>
    /// Merkt sich den Besitzer einer Beschwörung (laut Hypothese aus Typ 41 36). Deren Treffer zählen danach beim
    /// Besitzer (Spalte „davon Pet“), solange die Klasse der Fertigkeiten nicht widerspricht.
    /// </summary>
    public void ReportSummon(uint summon, uint owner)
    {
        if (summon == owner || owner == 0) return;
        lock (_lock)
        {
            _petOwner[summon] = owner;

            // Schon gezählte Treffer des Pets (kamen vor der Besitzer-Nachricht) nachträglich zum Besitzer verschieben.
            if (!_actors.TryGetValue(summon, out var pet)) return;
            var petPrefix = MainPrefix(summon);
            var ownerPrefix = MainPrefix(owner);
            if (petPrefix is not null && ownerPrefix is not null && petPrefix != ownerPrefix)
            {
                _petConflicts++;
                return;
            }

            if (!_actors.TryGetValue(owner, out var target))
            {
                target = new Accumulator(pet.First);
                _actors[owner] = target;
            }

            target.Merge(pet);
            _actors.Remove(summon);
            if (_classes.Remove(summon, out var counts))
            {
                if (!_classes.TryGetValue(owner, out var ownerCounts))
                {
                    ownerCounts = new Dictionary<int, int>();
                    _classes[owner] = ownerCounts;
                }

                foreach (var (prefix, n) in counts) ownerCounts[prefix] = ownerCounts.GetValueOrDefault(prefix) + n;
            }
        }
    }

    /// <summary>Pet-Treffer, die wegen widersprechender Klasse nicht dem Besitzer zugerechnet wurden.</summary>
    public long PetConflicts
    {
        get { lock (_lock) return _petConflicts; }
    }

    /// <summary>
    /// Klasse (Präfix) eines Spielers aus seinen Fertigkeiten; für den eigenen Charakter ohne Treffer die gemerkte Klasse
    /// (gleicher Name). Null, wenn nichts bekannt ist.
    /// </summary>
    public int? ClassPrefixOf(uint actor)
    {
        lock (_lock)
        {
            if (MainPrefix(actor) is { } known) return known;
            return _rememberedClass is { } r && (actor == CurrentOwn() || (_names.TryGetValue(actor, out var n) && n == r.Name))
                && (!_names.TryGetValue(actor, out var name) || name == r.Name)
                ? r.Prefix
                : null;
        }
    }

    /// <summary>Klassenname eines Spielers („Bogenschütze“) oder null.</summary>
    public string? ClassNameOf(uint actor) => ClassPrefixOf(actor) is { } p ? SkillClassMap.Describe(p) : null;

    private int? MainPrefix(uint actor) =>
        _classes.TryGetValue(actor, out var counts) && counts.Count > 0 ? counts.MaxBy(p => p.Value).Key : null;

    private readonly List<FightRecord> _pendingFights = new();
    private int _exportedFight;

    /// <summary>
    /// Liefert beendete Kämpfe für den Verlauf, jeden genau einmal: solche, die ein neuer Kampf verdrängt hat, und den
    /// aktuellen, sobald er seit <see cref="IdleTimeout"/> ohne Treffer ist.
    /// </summary>
    public IReadOnlyList<FightRecord> DrainFinishedFights(DateTimeOffset now)
    {
        lock (_lock)
        {
            var result = new List<FightRecord>(_pendingFights);
            _pendingFights.Clear();
            if (_lastHit is { } last && !IsActive(now, last) && _exportedFight != _fight && BuildFightRecord() is { } current)
            {
                result.Add(current);
                _exportedFight = _fight;
            }

            return result;
        }
    }

    /// <summary>Beim Beenden: auch einen noch laufenden Kampf für den Verlauf liefern (falls nicht schon geschehen).</summary>
    public FightRecord? ExportCurrentFight()
    {
        lock (_lock)
        {
            if (_exportedFight == _fight) return null;
            _exportedFight = _fight;
            return BuildFightRecord();
        }
    }

    private FightRecord? BuildFightRecord()
    {
        if (_start is not { } start || _lastHit is not { } end || _actors.Count == 0) return null;

        var own = CurrentOwn();
        var enemies = Enemies();
        var players = _actors
            .Where(kv => !enemies.Contains(kv.Key))
            .Select(kv => new FightPlayerRecord(
                kv.Key,
                _names.GetValueOrDefault(kv.Key),
                MainPrefix(kv.Key),
                kv.Value.Damage,
                kv.Value.Damage / Seconds(end - kv.Value.First),
                kv.Key == own,
                kv.Value.Records
                    .GroupBy(r => (r.Target, r.Skill, r.Kind))
                    .Select(g => new FightSkillRecord(g.Key.Target, g.Key.Skill, g.Key.Kind, g.Sum(r => r.Amount), g.Count(),
                        g.Count(r => r.IsCritical), g.Max(r => r.Amount), g.Min(r => r.Time)))
                    .ToList(),
                DpsTimeline.PerSecond(kv.Value.Records.Select(r => (r.Time, r.Amount)), start)))
            .OrderByDescending(p => p.Damage)
            .ToList();
        var targetNames = players.SelectMany(p => p.Skills.Select(s => s.Target)).Distinct()
            .Where(_names.ContainsKey)
            .ToDictionary(t => t, t => _names[t]);
        return new FightRecord(Guid.NewGuid(), FightRecord.DefaultName(start), start, end,
            own is { } o ? _names.GetValueOrDefault(o) : null, players, targetNames);
    }

    /// <summary>Beginnt von vorn. Namen und die Wahl bzw. Erkennung des eigenen Charakters bleiben.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            // Bewusst Zurückgesetztes kommt nicht in den Verlauf (die Daten sind danach weg); bereits verdrängte Kämpfe bleiben.
            _actors.Clear();
            _start = null;
            _lastHit = null;
            _fight = 0;
            _exportedFight = 0;
            _ignoredSelf = 0;
        }
    }

    /// <summary>
    /// Momentaufnahme. Läuft der Kampf (letzter Treffer höchstens <see cref="IdleTimeout"/> her), zählt die Zeit bis
    /// <paramref name="now"/> (abzüglich <see cref="Lag"/>) – die DPS fallen also, wenn niemand mehr trifft. Ist er
    /// beendet, wird bis zum letzten Treffer gerechnet.
    /// </summary>
    public LiveCombatSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_start is not { } start || _lastHit is not { } last)
            {
                return new LiveCombatSnapshot(_fight, null, null, false, 0, null, [], _ignoredSelf);
            }

            var end = FightEnd(now, last);
            var enemies = Enemies();
            var counted = _actors.Where(kv => !enemies.Contains(kv.Key)).ToList();
            var total = counted.Sum(kv => kv.Value.Damage);
            var own = CurrentOwn();
            var actors = counted
                .Select(kv => new LiveActorStatistics(
                    kv.Key,
                    kv.Value.Damage,
                    kv.Value.Hits,
                    kv.Value.Crits,
                    kv.Value.OverTime,
                    kv.Value.First,
                    kv.Value.Damage / Seconds(end - kv.Value.First),
                    total == 0 ? 0 : (double)kv.Value.Damage / total,
                    kv.Key == own,
                    _names.GetValueOrDefault(kv.Key),
                    MainPrefix(kv.Key),
                    kv.Value.PetDamage,
                    _petSkillUsers.Contains(kv.Key) && !_names.ContainsKey(kv.Key),
                    own is null && _remembered?.Id == kv.Key,
                    kv.Value.Last,
                    DeathsIn(kv.Key, start, last + IdleTimeout)))
                .OrderByDescending(a => a.Damage)
                .ThenBy(a => a.Actor)
                .ToList();

            return new LiveCombatSnapshot(_fight, start, last, IsActive(now, last), total,
                total / Seconds(end - start), actors, _ignoredSelf, end);
        }
    }

    private bool IsActive(DateTimeOffset now, DateTimeOffset last) => now - Lag - last <= IdleTimeout;

    private DateTimeOffset FightEnd(DateTimeOffset now, DateTimeOffset last)
    {
        if (!IsActive(now, last)) return last;
        var shifted = now - Lag;
        return shifted > last ? shifted : last;
    }

    private static double Seconds(TimeSpan span) => Math.Max(1.0, span.TotalSeconds);

    /// <summary>
    /// Aufschlüsselung eines Verursachers im aktuellen Kampf, optional nur gegen ein Ziel. null, wenn er keinen Schaden hat.
    /// </summary>
    /// <param name="now">Für die laufende Kampfzeit wie bei <see cref="Snapshot"/>; null = bis zum letzten Treffer.</param>
    public ActorDetail? Detail(uint actor, uint? target = null, DateTimeOffset? now = null)
    {
        lock (_lock)
        {
            if (!_actors.TryGetValue(actor, out var acc) || _lastHit is not { } lastHit) return null;
            var last = now is { } n ? FightEnd(n, lastHit) : lastHit;

            var targets = acc.Records
                .GroupBy(r => r.Target)
                .Select(g => new TargetDamage(g.Key, _names.GetValueOrDefault(g.Key), g.Sum(r => r.Amount)))
                .OrderByDescending(t => t.Damage)
                .ToList();
            var records = target is { } t ? acc.Records.Where(r => r.Target == t).ToList() : acc.Records;
            if (records.Count == 0) return new ActorDetail(actor, _names.GetValueOrDefault(actor), MainPrefix(actor), TimeSpan.Zero, 0, 0, 0, 0, targets, []);

            var damage = records.Sum(r => r.Amount);
            var duration = last - records.Min(r => r.Time);
            var direct = records.Where(r => r.Kind != HitKind.OverTime).ToList();
            var skills = records
                .GroupBy(r => (r.Skill, r.Kind))
                .Select(g =>
                {
                    var hits = g.Count();
                    return new SkillStatistics(
                        g.Key.Skill,
                        g.Key.Kind,
                        g.Sum(r => r.Amount),
                        damage == 0 ? 0 : (double)g.Sum(r => r.Amount) / damage,
                        hits,
                        g.Count(r => r.IsCritical),
                        g.Max(r => r.Amount),
                        (double)g.Sum(r => r.Amount) / hits,
                        Flags(g.ToList()));
                })
                .OrderByDescending(s => s.Damage)
                .ThenBy(s => s.Skill)
                .ToList();

            return new ActorDetail(actor, _names.GetValueOrDefault(actor), MainPrefix(actor), duration, damage,
                damage / Seconds(duration), direct.Count, direct.Count(r => r.IsCritical), targets, skills, Flags(records),
                ActiveTime(records), direct.Count + direct.Sum(r => r.ExtraHits));
        }
    }

    private sealed record HitRecord(DateTimeOffset Time, uint Target, uint Skill, long Amount, bool IsCritical, HitKind Kind,
        byte? Modifier, byte? Direction, int ExtraHits);

    /// <summary>Summe der Abstände zwischen aufeinanderfolgenden Treffern; Pausen ab <see cref="ActorDetail.ActiveGapLimit"/> zählen nicht.</summary>
    private static TimeSpan ActiveTime(IEnumerable<HitRecord> records)
    {
        var times = records.Select(r => r.Time).Order().ToList();
        var active = TimeSpan.Zero;
        for (var i = 1; i < times.Count; i++)
        {
            var gap = times[i] - times[i - 1];
            if (gap < ActorDetail.ActiveGapLimit) active += gap;
        }

        return active;
    }

    private static CommunityFlags Flags(IReadOnlyCollection<HitRecord> records)
    {
        var withModifier = records.Where(r => r.Modifier is not null).ToList();
        var withDirection = records.Where(r => r.Direction is not null).ToList();
        var direct = records.Where(r => r.Kind != HitKind.OverTime).ToList();
        return new CommunityFlags(
            withModifier.Count,
            withModifier.Count(r => (r.Modifier!.Value & 0x02) != 0),
            withModifier.Count(r => (r.Modifier!.Value & 0x04) != 0),
            withModifier.Count(r => (r.Modifier!.Value & 0x08) != 0),
            withDirection.Count,
            withDirection.Count(r => (r.Direction!.Value & 0x01) != 0),
            withDirection.Count(r => (r.Direction!.Value & 0x02) != 0),
            direct.Count,
            direct.Count(r => r.ExtraHits > 0));
    }

    private sealed class Accumulator(DateTimeOffset first)
    {
        public DateTimeOffset First { get; private set; } = first;
        public DateTimeOffset Last { get; private set; } = first;
        public long Damage, OverTime, PetDamage;
        public int Hits, Crits;
        public readonly List<HitRecord> Records = new();

        /// <summary>Übernimmt alle Werte eines Pets (zählt komplett als Pet-Schaden).</summary>
        public void Merge(Accumulator pet)
        {
            if (pet.First < First) First = pet.First;
            if (pet.Last > Last) Last = pet.Last;
            Damage += pet.Damage;
            OverTime += pet.OverTime;
            PetDamage += pet.Damage;
            Hits += pet.Hits;
            Crits += pet.Crits;
            Records.AddRange(pet.Records.Select(r => r with { Kind = HitKind.Pet }));
        }

        public void Add(CombatHit hit, bool fromPet)
        {
            Records.Add(new HitRecord(hit.Time, hit.Target, hit.Skill, hit.Amount, hit.IsCritical,
                fromPet ? HitKind.Pet : hit.OverTime ? HitKind.OverTime : HitKind.Direct,
                hit.Modifier, hit.Direction, hit.ExtraHits));
            if (hit.Time < First) First = hit.Time;
            if (hit.Time > Last) Last = hit.Time;
            Damage += hit.Amount;
            if (fromPet) PetDamage += hit.Amount;
            if (hit.OverTime)
            {
                OverTime += hit.Amount;
                return;
            }

            Hits++;
            if (hit.IsCritical) Crits++;
        }
    }
}
