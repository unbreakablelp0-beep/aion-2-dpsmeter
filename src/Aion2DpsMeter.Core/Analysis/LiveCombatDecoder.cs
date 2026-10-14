using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Wandelt live aufgenommene Rohpakete in Treffer um: TCP-Ströme zusammensetzen, Varint-Rahmen und LZ4-Bündel
/// lesen, dann Typ 04 38 (<see cref="DamageRecordHypothesis"/>) und Typ 05 38 mit Effekt 0x0A
/// (<see cref="DotRecordHypothesis"/>) als Treffer melden. Alles nach Community-Hypothesen, siehe README.
/// </summary>
/// <remarks>
/// Treffer und Spielerangaben werden nur aus Richtungen gemeldet, in denen mindestens <see cref="MinimumHeartbeats"/>
/// Heartbeat-Muster gesehen wurden – so werden zufällige Bytefolgen anderer Verbindungen nicht gedeutet. Rahmen
/// davor werden zurückgehalten (höchstens <see cref="MaxPendingFrames"/>) und ausgewertet, sobald die Sperre aufgeht.
/// Ankündigungen (Layout 0) und nicht passende Datensätze werden übersprungen. Selbsttreffer
/// (Verursacher = Ziel) werden weitergegeben; <see cref="LiveCombatMeter"/> verwirft sie.
/// Nicht threadsicher: nur von einem Thread aus aufrufen (dem Aufnahme-Thread).
/// </remarks>
public sealed class LiveCombatDecoder
{
    public const int MinimumHeartbeats = 3;

    /// <summary>So viele Rahmen je Richtung werden höchstens zurückgehalten, bis die Heartbeat-Sperre aufgeht.</summary>
    public const int MaxPendingFrames = 500;

    private static readonly IReadOnlySet<ushort> Types =
        new HashSet<ushort>(PlayerInfoHypothesis.Types) { DamageRecordHypothesis.Type, DotRecordHypothesis.Type, SummonOwnerHypothesis.Type, EnemyHpHypothesis.RemainingType, PartyRosterHypothesis.Type, FieldBossListHypothesis.Type, BattleStateHypothesis.Type, PartyHpHypothesis.Type };

    private readonly Action<CombatHit> _onHit;
    private readonly Action<PlayerInfoCandidate>? _onPlayer;
    private readonly Action<SummonOwnerCandidate>? _onSummon;
    private readonly Action<EnemyHpCandidate, DateTimeOffset>? _onEnemyHp;
    private readonly Action<PartyRoster>? _onParty;
    private readonly Action<FieldBossList>? _onFieldBosses;
    private readonly Action<long>? _onOwnCombatPower;
    private readonly Action<BattleState>? _onBattleState;
    private readonly Action<uint, DateTimeOffset>? _onDeath;
    private readonly TcpStreamReassembler _reassembler = new(TimeSpan.FromSeconds(2));
    private readonly Dictionary<TcpStreamKey, StreamHypothesisCheck> _checks = new();
    private readonly Dictionary<StreamHypothesisCheck, List<ObservedFrame>> _pending = new();

    /// <param name="onHit">Bekommt Treffer.</param>
    /// <param name="onPlayer">Optional: bekommt Spielerangaben (33 36 / 45 36 / 44 36), nur im Speicher.</param>
    /// <param name="onSummon">Optional: bekommt Beschwörungen mit mutmaßlichem Besitzer (Typ 41 36).</param>
    /// <param name="onDeath">Optional: bekommt Kennungen, deren HP auf 0 fiel (Gruppen-HP 1B 92 „tot“ oder 00 8D mit 0) – der Zähler filtert auf Spieler.</param>
    /// <param name="onBattleState">Optional: bekommt den Kampfzustand von Gegnern (21 8D), bei Bossen mit Zeitlimit inkl. Deadline.</param>
    /// <param name="onOwnCombatPower">Optional: bekommt die eigene Kampfkraft (aus 33 36).</param>
    /// <param name="onFieldBosses">Optional: bekommt die Feldboss-Liste einer Karte (01 91).</param>
    /// <param name="onParty">Optional: bekommt die Gruppenliste (02 97).</param>
    /// <param name="onEnemyHp">Optional: bekommt Gegner-HP (00 8D, beim Erscheinen 41 36 mit Max-HP).</param>
    public LiveCombatDecoder(
        Action<CombatHit> onHit,
        Action<PlayerInfoCandidate>? onPlayer = null,
        Action<SummonOwnerCandidate>? onSummon = null,
        Action<EnemyHpCandidate, DateTimeOffset>? onEnemyHp = null,
        Action<PartyRoster>? onParty = null,
        Action<FieldBossList>? onFieldBosses = null,
        Action<long>? onOwnCombatPower = null,
        Action<BattleState>? onBattleState = null,
        Action<uint, DateTimeOffset>? onDeath = null)
    {
        ArgumentNullException.ThrowIfNull(onHit);
        _onHit = onHit;
        _onPlayer = onPlayer;
        _onSummon = onSummon;
        _onEnemyHp = onEnemyHp;
        _onParty = onParty;
        _onFieldBosses = onFieldBosses;
        _onOwnCombatPower = onOwnCombatPower;
        _onBattleState = onBattleState;
        _onDeath = onDeath;
    }

    /// <summary>Ping zum Spielserver (passiv aus den TCP-Kopfzeilen der Spielverbindung).</summary>
    public PingEstimator Ping { get; } = new();

    // Spielserver: Absender des Stroms, in dem die Heartbeats bestätigt wurden.
    private Endpoint? _server;

    /// <summary>Gemeldete Beschwörungen mit Besitzer.</summary>
    public long SummonCount { get; private set; }

    /// <summary>Gemeldete Spielerangaben.</summary>
    public long PlayerInfoCount { get; private set; }

    /// <summary>Gemeldete Treffer (inkl. Selbsttreffer).</summary>
    public long HitCount { get; private set; }

    /// <summary>04-38-Rahmen, deren erster Datensatz nicht passte.</summary>
    public long RejectedFrames { get; private set; }

    /// <summary>Verarbeitet ein Paket. Nicht lesbare und Nicht-TCP-Pakete werden ignoriert.</summary>
    public void Add(RawPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!PacketHeaderParser.TryParse(packet, out var parsed) || parsed?.Tcp is null) return;
        if (_server is { } server)
        {
            if (parsed.Destination == server) Ping.Sent(parsed.Tcp.SequenceNumber, parsed.Payload.Length, parsed.Timestamp);
            else if (parsed.Source == server && parsed.Tcp.Flags.HasFlag(TcpFlags.Ack)) Ping.Acknowledged(parsed.Tcp.AcknowledgmentNumber, parsed.Timestamp);
        }

        foreach (var streamEvent in _reassembler.Add(parsed)) Route(streamEvent);
    }

    private void Route(TcpStreamEvent streamEvent)
    {
        if (streamEvent is TcpStreamClosed) return;
        if (!_checks.TryGetValue(streamEvent.Stream, out var check))
        {
            var midStream = _reassembler.GetStatistics(streamEvent.Stream)?.StartedMidStream ?? true;
            StreamHypothesisCheck? created = null;
            created = new StreamHypothesisCheck(!midStream, new FrameObserver(Types, frame => OnFrame(created!, frame)));
            check = created;
            _checks[streamEvent.Stream] = check;
        }

        check.Add(streamEvent);
        if (check.HeartbeatsSeen >= MinimumHeartbeats && _server != streamEvent.Stream.Source) _server = streamEvent.Stream.Source;

        // Sperre inzwischen offen (z. B. nur Heartbeats nachgekommen)? Dann Zurückgehaltenes jetzt auswerten.
        if (check.HeartbeatsSeen >= MinimumHeartbeats && _pending.Remove(check, out var waiting))
        {
            foreach (var earlier in waiting) Decode(earlier);
        }
    }

    private void OnFrame(StreamHypothesisCheck check, ObservedFrame frame)
    {
        if (check.HeartbeatsSeen < MinimumHeartbeats)
        {
            // Zurückhalten statt verwerfen: Login-Angaben und erste Treffer kommen oft vor dem dritten Heartbeat.
            if (!_pending.TryGetValue(check, out var list))
            {
                list = [];
                _pending[check] = list;
            }

            if (list.Count < MaxPendingFrames) list.Add(frame);
            return;
        }

        if (_pending.Remove(check, out var waiting))
        {
            foreach (var earlier in waiting) Decode(earlier);
        }

        Decode(frame);
    }

    private void Decode(ObservedFrame frame)
    {
        if (frame.Type == BattleStateHypothesis.Type)
        {
            if (_onBattleState is not null && BattleStateHypothesis.Parse(frame.Body, frame.Time) is { } state) _onBattleState(state);
            return;
        }

        if (frame.Type == PartyHpHypothesis.Type)
        {
            if (_onDeath is not null && PartyHpHypothesis.Parse(frame.Body) is { Alive: false } dead) _onDeath(dead.Target, frame.Time);
            return;
        }

        if (frame.Type == FieldBossListHypothesis.Type)
        {
            if (_onFieldBosses is not null && FieldBossListHypothesis.Parse(frame.Body) is { Slots.Count: > 0 } list) _onFieldBosses(list);
            return;
        }

        if (frame.Type == PartyRosterHypothesis.Type)
        {
            if (_onParty is not null && PartyRosterHypothesis.Parse(frame.Body) is { } roster) _onParty(roster);
            return;
        }

        if (frame.Type == EnemyHpHypothesis.RemainingType)
        {
            if (EnemyHpHypothesis.ParseRemaining(frame.Body) is { } hp)
            {
                _onEnemyHp?.Invoke(hp, frame.Time);
                if (hp.Hp == 0) _onDeath?.Invoke(hp.Target, frame.Time);
            }

            return;
        }

        if (frame.Type == SummonOwnerHypothesis.Type)
        {
            if (_onSummon is not null && SummonOwnerHypothesis.Parse(frame.Body) is { } summon)
            {
                SummonCount++;
                _onSummon(summon);
            }

            // Derselbe Typ meldet auch erscheinende Gegner mit Start- und Max-HP.
            if (_onEnemyHp is not null && EnemyHpHypothesis.ParseSpawn(frame.Body) is { } spawn) _onEnemyHp(spawn, frame.Time);
            return;
        }

        if (PlayerInfoHypothesis.Types.Contains(frame.Type))
        {
            if (_onPlayer is not null && PlayerInfoHypothesis.Parse(frame.Type, frame.Body) is { } player)
            {
                PlayerInfoCount++;
                _onPlayer(player);
            }

            if (_onOwnCombatPower is not null && frame.Type == 0x3336 && CombatPowerHypothesis.FromSelfRecord(frame.Body) is > 0 and var power) _onOwnCombatPower(power);

            return;
        }

        if (frame.Type == DamageRecordHypothesis.Type)
        {
            var result = DamageRecordHypothesis.Parse(frame.Body);
            if (result.Status == DamageFrameStatus.Rejected) RejectedFrames++;
            foreach (var r in result.Records)
            {
                Emit(new CombatHit(frame.Time, r.Actor, r.Target, r.Skill, r.Candidate, r.DamageType == 3, OverTime: false,
                    r.Modifier, r.Direction, r.ExtraHits));
            }
        }
        else if (DotRecordHypothesis.Parse(frame.Body) is { IsDamageEffect: true } dot && dot.Effect == 0x0A)
        {
            Emit(new CombatHit(frame.Time, dot.Actor, dot.Target, dot.Skill, dot.Amount, IsCritical: false, OverTime: true));
        }
    }

    private void Emit(CombatHit hit)
    {
        HitCount++;
        _onHit(hit);
    }
}
