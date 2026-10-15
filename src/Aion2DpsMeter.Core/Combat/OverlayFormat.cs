using System.Globalization;

namespace Aion2DpsMeter.Core.Combat;

/// <summary>Eine Zeile im Overlay, fertig formatiert.</summary>
/// <param name="Deaths">Tode im aktuellen Kampf (Gruppen-HP 1B 92 / 00 8D auf 0), 0 = keine.</param>
/// <param name="MaybeOwn">Nur gemerkt, nicht sicher der eigene Charakter (unauffälliger markieren).</param>
/// <param name="Actor">Kennung (für das Detailfenster).</param>
/// <param name="IsLikelyPet">Pet ohne erkannten Besitzer (Pfoten-Symbol).</param>
/// <param name="DamageValue">Schaden als Zahl (für weich hochzählende Anzeige).</param>
/// <param name="DpsValue">DPS als Zahl.</param>
/// <param name="Hits">Zahl der Treffer (für „großer Treffer“-Hervorhebung).</param>
/// <param name="CombatPower">Kampfkraft, falls bekannt (eigener Charakter, Gruppenmitglieder).</param>
public sealed record OverlayRow(string Name, string Class, string Damage, string Dps, string Share, double ShareValue, bool IsOwn, int Deaths = 0, bool MaybeOwn = false, uint Actor = 0, bool IsLikelyPet = false, long DamageValue = 0, double DpsValue = 0, int Hits = 0, long? CombatPower = null);

/// <summary>Kopfzeile und Zeilen für das Overlay.</summary>
/// <param name="Header">Zusammenfassung in einer Zeile (z. B. zum Kopieren).</param>
/// <param name="Active">Kampf läuft (grüner Statuspunkt).</param>
/// <param name="Title">„Kampf“, „Kampf beendet“ oder „Inaktiv“.</param>
/// <param name="Elapsed">Kampfzeit „m:ss“ oder leer.</param>
/// <param name="TotalDamage">Gesamtschaden kurz oder leer.</param>
/// <param name="TotalDps">Gesamt-DPS kurz oder leer.</param>
/// <param name="OwnOnly">Nur der eigene Charakter wird gezeigt (Standard).</param>
/// <param name="OwnUnknown">Nur-ich-Modus, aber der eigene Charakter ist (noch) nicht erkannt.</param>
public sealed record OverlayContent(
    string Header,
    IReadOnlyList<OverlayRow> Rows,
    int HiddenRows,
    bool Active = false,
    string Title = "Inaktiv",
    string Elapsed = "",
    string TotalDamage = "",
    string TotalDps = "",
    bool OwnOnly = false,
    bool OwnUnknown = false);

/// <summary>Kompakte Darstellung der Live-Werte für das Overlay (ohne Oberfläche, testbar).</summary>
public static class OverlayFormat
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Anteile darunter werden nicht einzeln gezeigt (außer der eigene Charakter).</summary>
    public const double MinimumShare = 0.01;

    /// <summary>
    /// Baut den Inhalt: höchstens <paramref name="maxRows"/> Zeilen mit mindestens 1 % Anteil, absteigend nach
    /// Schaden; der eigene Charakter wird immer gezeigt (bei Bedarf statt der letzten Zeile).
    /// </summary>
    /// <param name="hideTinyRows">false: auch Spieler unter 1 % einzeln zeigen.</param>
    /// <param name="ownOnly">
    /// true: nur der eigene Charakter (und ein nur gemerkter „?“-Kandidat). Andere Spieler zählen weiter für Anteil
    /// und „+ n weitere“, werden aber nicht gezeigt.
    /// </param>
    /// <param name="party">Bei <paramref name="ownOnly"/>: Gruppenmitglieder, die zusätzlich gezeigt werden.</param>
    /// <param name="combatPower">Liefert die Kampfkraft eines Spielers (oder null).</param>
    /// <param name="idle">Leerlaufzeit für „Kampf“/„Kampf beendet“ im Modus <paramref name="ownOnly"/> (Standard 10 s).</param>
    public static OverlayContent Build(LiveCombatSnapshot snapshot, int maxRows, bool hideTinyRows = true, bool ownOnly = false, IReadOnlySet<uint>? party = null, TimeSpan? idle = null, Func<uint, long?>? combatPower = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);

        if (snapshot.Start is null) return new OverlayContent("AionMeta – noch keine Treffer", [], 0, OwnOnly: ownOnly);
        var elapsed = Elapsed(snapshot.Duration);
        var active = snapshot.Active;
        var title = active ? "Kampf" : "Kampf beendet";

        var shown = ownOnly
            ? snapshot.Actors.Where(a => a.IsOwn || a.IsRememberedOwn || party?.Contains(a.Actor) == true).Take(maxRows).ToList()
            : snapshot.Actors.Where(a => !hideTinyRows || a.Share >= MinimumShare || a.IsOwn).Take(maxRows).ToList();
        if (snapshot.Actors.FirstOrDefault(a => a.IsOwn) is { } own && !shown.Contains(own))
        {
            if (shown.Count < maxRows) shown.Add(own);
            else shown[^1] = own;
        }

        // Im Modus ich/Gruppe zählen für Kampfstatus, Kampfzeit und DPS nur die gezeigten Spieler – Fremde, die z. B.
        // nebenan eine Puppe schlagen, starten keinen Kampf und halten ihn nicht am Laufen.
        Func<LiveActorStatistics, double> dps = a => a.Dps;
        Func<LiveActorStatistics, double> share = a => a.Share;
        if (ownOnly)
        {
            // Anteil an den gezeigten Spielern: nur ich = 100 %, Gruppe = Anteil in der Gruppe.
            var shownTotal = shown.Sum(a => a.Damage);
            share = a => shownTotal == 0 ? 0 : (double)a.Damage / shownTotal;
            if (shown.Count == 0)
            {
                return new OverlayContent("AionMeta – noch keine eigenen Treffer", [], snapshot.Actors.Count, false, "Inaktiv", string.Empty,
                    OwnOnly: true, OwnUnknown: !snapshot.Actors.Any(a => a.IsOwn || a.IsRememberedOwn));
            }

            var groupStart = shown.Min(a => a.FirstHit);
            var groupLast = shown.Max(a => a.LastHit ?? a.FirstHit);
            var end = snapshot.End ?? snapshot.LastHit ?? groupLast;
            active = snapshot.Active && end - groupLast <= (idle ?? TimeSpan.FromSeconds(10));
            var groupEnd = active ? end : groupLast;
            elapsed = Elapsed(groupEnd - groupStart);
            title = active ? "Kampf" : "Kampf beendet";
            dps = a => a.Damage / Math.Max(1.0, (groupEnd - a.FirstHit).TotalSeconds);
        }

        var header = string.Create(German,
            $"{title} {snapshot.Duration.TotalSeconds:0} s · {Compact(snapshot.TotalDamage)} · {Compact((long)(snapshot.Dps ?? 0))} DPS");
        var rows = shown.Select(a => new OverlayRow(
                DisplayName(a),
                a.ClassName ?? string.Empty,
                Compact(a.Damage),
                Compact((long)dps(a)),
                (share(a) * 100).ToString("0.0", German) + " %",
                share(a),
                a.IsOwn,
                a.Deaths,
                a.IsRememberedOwn,
                a.Actor,
                a.IsLikelyPet,
                a.Damage,
                dps(a),
                a.Hits,
                combatPower?.Invoke(a.Actor)))
            .ToList();
        return new OverlayContent(header, rows, snapshot.Actors.Count - rows.Count,
            active,
            title,
            elapsed,
            Compact(snapshot.TotalDamage),
            Compact((long)(snapshot.Dps ?? 0)),
            ownOnly,
            ownOnly && rows.Count == 0);
    }

    /// <summary>
    /// Zusammenfassung beim Kampfende für den eigenen Charakter: „1:19 · 14,5K DPS · 22,3 % · Platz 1 von 5“ und – falls
    /// ein früherer Bestwert bekannt ist – „Bestwert 16,2K (−10 %)“ bzw. „neuer Bestwert!“. Null ohne eigenen Charakter.
    /// </summary>
    /// <param name="bestOwnDps">Beste eigene DPS aus früheren gespeicherten Kämpfen (ohne diesen), sonst null.</param>
    /// <param name="among">Nur unter diesen Spielern vergleichen (Anteil, Platz), z. B. ich + Gruppe; null = alle.</param>
    public static string? FightEndText(LiveCombatSnapshot snapshot, double? bestOwnDps, IReadOnlySet<uint>? among = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var actors = snapshot.Actors.Where(a => among is null || among.Contains(a.Actor) || a.IsOwn).ToList();
        var index = actors.FindIndex(a => a.IsOwn);
        if (index < 0 || snapshot.Start is null) return null;
        var own = actors[index];
        var total = actors.Sum(a => a.Damage);
        // Eigene Kampfzeit (erster bis letzter eigener Treffer) – nicht die des ganzen Gebiets.
        var span = (own.LastHit ?? snapshot.LastHit ?? own.FirstHit) - own.FirstHit;
        var ownDps = own.Damage / Math.Max(1.0, span.TotalSeconds);
        var parts = new List<string>
        {
            Elapsed(span),
            Compact((long)ownDps) + " DPS",
            (total == 0 ? 0 : (double)own.Damage / total * 100).ToString("0.0", German) + " %",
            $"Platz {index + 1} von {actors.Count}",
        };
        if (bestOwnDps is > 0 and var best)
        {
            parts.Add(ownDps > best
                ? "neuer Bestwert!"
                : $"Bestwert {Compact((long)best)} ({((ownDps / best - 1) * 100).ToString("+0;−0", German)} %)");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Text der Boss-Leiste: „62,4 % · 46,8M · Kill ~0:46“. Ohne Max-HP nur Rest-HP, ohne Schätzung kein Kill-Teil.
    /// </summary>
    public static string BossText(BossStatus boss)
    {
        ArgumentNullException.ThrowIfNull(boss);
        var parts = new List<string>();
        if (boss.Fraction is { } f) parts.Add((f * 100).ToString("0.0", German) + " %");
        parts.Add(Compact(boss.Hp) + (boss.MaxHp is { } max ? " / " + Compact(max) : " HP"));
        if (boss.TimeToKill is { } ttk && boss.Hp > 0) parts.Add("Kill ~" + Elapsed(ttk));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Manueller Berserk-Countdown: „Berserk in 4:12“, unter 0 „BERSERK!“. <paramref name="fightStart"/> = erster Treffer
    /// der gezählten Spieler auf den Boss. Null ohne eingetragene Zeit.
    /// </summary>
    public static string? BerserkText(TimeSpan? berserk, DateTimeOffset? fightStart, DateTimeOffset now)
    {
        if (berserk is not { } limit || fightStart is not { } start) return null;
        var left = limit - (now - start);
        return left <= TimeSpan.Zero ? "BERSERK!" : "Berserk in " + Elapsed(left + TimeSpan.FromMilliseconds(999));
    }

    /// <summary>Berserk-Countdown aus der Deadline des Spiels: „Berserk in 4:12“, danach „BERSERK!“.</summary>
    public static string BerserkTextUntil(DateTimeOffset deadline, DateTimeOffset now)
    {
        var left = deadline - now;
        return left <= TimeSpan.Zero ? "BERSERK!" : "Berserk in " + Elapsed(left + TimeSpan.FromMilliseconds(999));
    }

    /// <summary>Ohne Eintrag in der Gegnerliste gilt ein Gegner ab dieser Max-HP als Boss (Feld-/Dungeon-Bosse in den Aufnahmen: 15,5 Mio. bzw. 75 Mio.).</summary>
    public const long BossMinMaxHp = 5_000_000;

    /// <summary>
    /// Boss-Leiste nur für Dungeon- und Welt-/Feldbosse: mit Zeitlimit vom Spiel (21 8D) immer; sonst laut Community-
    /// Gegnerliste „Boss“ und keine Trainingspuppe; ohne Eintrag in der Liste erst ab <see cref="BossMinMaxHp"/> Max-HP
    /// (unsichere Fälle bleiben ausgeblendet).
    /// </summary>
    public static bool IsBoss(BossStatus boss, NpcInfo? npc)
    {
        ArgumentNullException.ThrowIfNull(boss);
        if (npc is not null && BossRules.IsObjectName(npc.Name)) return false; // Totems, Puppen, Objekte
        if (boss.Deadline is not null) return true;
        return npc is not null ? npc.IsBoss && !npc.IsDummy : boss.MaxHp >= BossMinMaxHp;
    }

    /// <summary>Aktuelle HP ausgeschrieben für die Boss-Leiste: „49.528.026 HP“.</summary>
    public static string BossHpText(BossStatus boss)
    {
        ArgumentNullException.ThrowIfNull(boss);
        return boss.Hp.ToString("#,0", German) + " HP";
    }

    /// <summary>Status unter dem Boss-Namen: „Kampf läuft · Kill ~0:46“, „Besiegt“, ohne Max-HP mit Hinweis.</summary>
    public static string BossStatusText(BossStatus boss)
    {
        ArgumentNullException.ThrowIfNull(boss);
        if (boss.Hp == 0) return "Besiegt";
        var text = "Kampf läuft";
        if (boss.TimeToKill is { } ttk) text += " · Kill ~" + Elapsed(ttk);
        if (boss.MaxHp is null) text += " · Max-HP unbekannt";
        return text;
    }

    /// <summary>DPS im Stil „28,81K/s“: unter 100K zwei Nachkommastellen (feiner als <see cref="Compact"/>).</summary>
    public static string PerSecond(double dps)
    {
        var value = (long)Math.Round(dps);
        var abs = Math.Abs(value);
        return abs is >= 1_000 and < 99_995
            ? (value / 1_000d).ToString("0.00", German) + "K/s"
            : Compact(value) + "/s";
    }

    /// <summary>Aktive Zeit: „&lt;0:01“ unter einer Sekunde (statt irreführend „0:00“), sonst wie <see cref="Elapsed"/>.</summary>
    public static string ActiveTime(TimeSpan active) => active < TimeSpan.FromSeconds(1) ? "<0:01" : Elapsed(active);

    /// <summary>„DPS (aktiv)“: Kurzzahl oder „–“, wenn zu wenig aktive Zeit (<see cref="ActorDetail.MinActiveTime"/>).</summary>
    public static string ActiveDps(double? dps) => dps is { } d ? Compact((long)d) : "–";

    /// <summary>Kampfzeit als „m:ss“ (bzw. „h:mm:ss“).</summary>
    public static string Elapsed(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    /// <summary>Text für die Zwischenablage (z. B. zum Einfügen in den Gruppen-Chat).</summary>
    public static string ToClipboardText(OverlayContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Rows.Count == 0) return "AionMeta – noch keine Treffer";

        var lines = new List<string>
        {
            $"AionMeta – {content.Title} {content.Elapsed} – gesamt {content.TotalDamage} ({content.TotalDps} DPS)",
        };
        lines.AddRange(content.Rows.Select((r, i) =>
            $"{i + 1}. {r.Name}{(r.Class.Length > 0 ? $" ({r.Class})" : "")}: {r.Damage} | {r.Dps} DPS | {r.Share}"));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Name, sonst „Pet? #Kennung“ oder „(Name unbekannt) #Kennung“.</summary>
    public static string DisplayName(LiveActorStatistics actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Name is { } name) return name;
        var id = actor.Actor.ToString(CultureInfo.InvariantCulture);
        return actor.IsLikelyPet ? $"Pet? #{id}" : $"(Name unbekannt) #{id}";
    }

    /// <summary>So lange nach dem eigenen Kampf bleibt der Gruppenhinweis noch stehen.</summary>
    public static readonly TimeSpan PartyHintAfterFight = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Hinweis „Gruppe x/y erkannt“ zeigen? Nur wenn eingeschaltet, die Gruppe größer als die erkannten Namen ist und ich
    /// gerade kämpfe oder der Kampf höchstens <see cref="PartyHintAfterFight"/> her ist – nicht dauerhaft im Leerlauf.
    /// </summary>
    public static bool ShowPartyHint(bool enabled, int? partySize, int known, bool fighting, TimeSpan? sinceFight) =>
        enabled && partySize is > 1 && known < partySize && (fighting || sinceFight is { } s && s <= PartyHintAfterFight);

    /// <summary>Kurze Zahl: 950, 1,30K, 12,4K, 1,88M. Grenzen so gewählt, dass das Runden nie „10,00K“ oder „1000,0K“ ergibt.</summary>
    public static string Compact(long value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 999_995_000 => (value / 1_000_000_000d).ToString("0.00", German) + "G",
            >= 999_950 => (value / 1_000_000d).ToString("0.00", German) + "M",
            >= 9_995 => (value / 1_000d).ToString("0.0", German) + "K",
            >= 1_000 => (value / 1_000d).ToString("0.00", German) + "K",
            _ => value.ToString("0", German),
        };
    }
}
