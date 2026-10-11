# Combat (Schadensberechnung)

Grundstruktur der DPS-Engine – unabhängig von Netzwerk und Protokoll:

- `Combatant` – Spieler (Id, Name, eigene Figur ja/nein)
- `DamageEvent` – ein Treffer (Zeit, Verursacher, Ziel, Schaden, kritisch, Datensicherheit)
- `CombatSession` – sammelt Treffer, `Reset()` für Kampf-Reset, `GetStatistics()` rechnet
- `CombatStatistics` / `CombatantStatistics` – Ergebnis

Regeln:

- Berechnet wird nur aus Ereignissen mit `DataConfidence.Confirmed`.
  Unsichere Ereignisse werden gezählt (`UnconfirmedEventCount`) und gekennzeichnet (`HasUncertainData`).
- Kampfzeit = letzter minus erster bestätigter Treffer.
- DPS = Schaden / Kampfzeit in Sekunden; bei Kampfzeit 0 nicht berechenbar (`null`).

Woher die Ereignisse kommen, ist hier bewusst offen: Die Protokollanalyse liefert sie
erst, wenn Nachrichten nachweislich verstanden sind.
