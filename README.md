# AionMeta – DPS-Meter für AION 2 (inoffiziell)

Ein Overlay-DPS-Meter für **AION 2 Global / EU** – C#, .NET 10, WPF, Windows 11 x64.

> **Inoffiziell.** Dieses Projekt steht in keiner Verbindung zu NCSOFT. AION ist eine Marke von NCSOFT.
> NCSOFT untersagt in seinen Nutzungsbedingungen nicht autorisierte Programme. Die Nutzung erfolgt **auf eigene Gefahr**.

## Was es macht

- **Rein passiv:** liest mit [Npcap](https://npcap.com/) die Netzwerkpakete mit, die ohnehin an den eigenen PC gehen.
  - Kein Zugriff auf den Spielspeicher und keine Injektion.
  - Keine Automatisierung und keine Eingaben ins Spiel.
  - Nichts wird an das Spiel oder den Server gesendet.
- **Overlay:** DPS, Schaden und Anteil je Spieler. Die Spieleransicht wählt sich automatisch: allein siehst du nur dich,
  in einer Gruppe dich und deine Gruppe.
  - Klassenfarben und Kampfkraft (eigene und Gruppe).
  - Tode im Kampf mit Totenkopf.
  - Kontraststufen für gute Lesbarkeit.
  - Automatisches Ausblenden ohne Kampf.
- **Bosse:**
  - HP-Leiste nur bei Dungeon- und Weltbossen.
  - Kill-Zeit-Schätzung.
  - Berserk-Countdown, wenn das Spiel ein Zeitlimit meldet.
- **Mehr:**
  - Spieler-Detailfenster mit Fähigkeiten und DPS-Kurve.
  - Kampfverlauf und Wochenstatistik.
  - Event-Alarme und Weltboss-Zeiten.
  - Tray-Symbol und Tastenkürzel.
- Alle Daten bleiben **lokal** unter `Dokumente\Aion2DpsMeter` (Kämpfe, Einstellungen, Aufnahmen).
- **Updates:** Der Meter fragt GitHub nach neuen Releases (abschaltbar, es werden keine Daten gesendet). Ein Klick auf den
  Hinweis im Overlay lädt die neue Version, prüft die SHA-256 und startet neu. Die EXE ist nicht signiert, deshalb kann
  Windows beim ersten Start warnen.

Viele Paketfelder sind **Hypothesen**, die an eigenen Aufnahmen geprüft wurden. Details stehen im
[Entwicklungsprotokoll](docs/Entwicklungsprotokoll.md).

## Was nicht enthalten ist

- **Keine Aufnahmen** (`.pcap`), keine Kampfdaten und keine Spielernamen.
- **Keine Community-Namenslisten** (Fähigkeiten, Gegner).
  - Liegen solche Listen lokal unter `Dokumente\Aion2DpsMeter`, nutzt das Programm sie (`skills-community.json`,
    `npcs-community.json`).
  - Ohne sie zeigt es Nummern statt Namen.

## Bauen

Voraussetzungen: Windows 11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) und
[Npcap](https://npcap.com/) für die Aufnahme.

```bash
dotnet build Aion2DpsMeter.slnx -c Release
```

```bash
dotnet test Aion2DpsMeter.slnx -c Release --filter "Category!=RequiresNpcap"
```

Eigenständige EXE (eine Datei):

```bash
dotnet publish src/Aion2DpsMeter.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/endnutzer-win-x64
```

Tests mit `Category=RequiresNpcap` brauchen ein installiertes Npcap. Tests mit `LocalRecording` laufen nur, wenn
`AION2DPS_HYPOTHESIS_PCAP` auf eine eigene Aufnahme zeigt.

## Projektstruktur

| Ordner | Inhalt |
|---|---|
| `src/Aion2DpsMeter.Core` | Paketrahmen, Hypothesen-Decoder, Kampfberechnung, Overlay-Texte, Einstellungen, Events |
| `src/Aion2DpsMeter.Capture.Npcap` | Passive Aufnahme über Npcap, `.pcap`-Dateien lesen |
| `src/Aion2DpsMeter.App` | WPF-Oberfläche: Overlay, Einstellungen, Detail- und Statistikfenster |
| `tests/` | xUnit-Tests (Core, App, Npcap) |
| `docs/` | Entwicklungsprotokoll und Diagnosen |

## Quellen und Dank

Ein Teil des Protokollverständnisses folgt der öffentlichen Beschreibung im Projekt
[cyberbadger6969/aion2-dps-meter](https://github.com/cyberbadger6969/aion2-dps-meter) (GPL-3.0).
Es wurde nur gelesen, **kein Code übernommen**; jede betroffene Klasse nennt die Quelle im Kommentar.

## Lizenz

[MIT](LICENSE)

---

## English (short)

**AionMeta** is an **unofficial** overlay DPS meter for **AION 2 Global / EU**, written in C# with .NET 10 and WPF for Windows 11 x64.

- **Passive only:** it reads the game's network packets with Npcap.
  - No memory reading and no injection.
  - No automation, and nothing is sent to the game.
- **Features:**
  - Overlay with DPS, damage and share per player, plus class colours and combat power.
  - Deaths per fight.
  - Boss HP bar, kill-time estimate and berserk countdown.
  - Player detail window with a DPS chart.
  - Fight history, weekly stats, event alarms and contrast levels.
- **Not included:** no packet recordings, no player names and no community name lists.
- **Disclaimer:** not affiliated with NCSOFT. Third-party tools may violate the game's terms of service, so use it at
  your own risk.
- **Build:** `dotnet build Aion2DpsMeter.slnx -c Release` (needs the .NET 10 SDK, plus Npcap to capture).
- **Credits:** parts of the protocol understanding follow the public description in cyberbadger6969/aion2-dps-meter
  (GPL-3.0). It was read only and no code was copied.
- **Licence:** MIT.
