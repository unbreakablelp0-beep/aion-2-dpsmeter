# AION 2 DPS Meter

Ein eigenständiges DPS-Meter für AION 2 Global / EU (C#, .NET 10, WPF, Windows 11 x64).

**Stand: Phase 3 (Aufzeichnungen laden) – Code fertig, Laufzeit noch ungeprüft.**

- Aufzeichnungen: `.pcap`/`.pcapng` laden und Pakete zählen (`RecordingLoader`,
  `PcapFileSource`). Tests mit selbst erzeugten synthetischen Dateien sind geschrieben,
  konnten wegen Smart App Control aber noch nicht ausgeführt werden.

- Schadensberechnung (Gesamtschaden, DPS, Kampfzeit, Treffer, kritische Treffer):
  Grundstruktur fertig, mit synthetischen Testdaten getestet.
- Netzwerkdiagnose: Npcap-Prüfung, Adapterliste, passive Aufnahme mit Paketzähler,
  Statuswechsel und verständliche Fehlermeldungen – als testbare Klassen, noch
  **nicht** in der Oberfläche.
- Es gibt **keine** Protokollanalyse und **keinen** Decoder. Aus Netzwerkpaketen
  werden keine AION-2-Werte gelesen; die Berechnung bekommt noch keine echten Spieldaten.
- Das Fenster soll den Status „Netzwerkaufnahme noch nicht eingerichtet“ zeigen;
  sein Start ist derzeit durch Smart App Control blockiert (siehe unten).

## Voraussetzungen

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Npcap](https://npcap.com/) für die passive Netzwerkaufnahme (getestet mit Npcap 1.89)

## Projektstruktur

```
Aion2DpsMeter.slnx                          Projektmappe (Solution)
src/Aion2DpsMeter.App/                      WPF-Oberfläche (Fenster, später Overlay)
src/Aion2DpsMeter.Core/                     Logik ohne Oberfläche, ohne Fremdpakete
    Capture/                                Netzwerkdiagnose: Status, Fehlerarten, Paketzähler,
                                            NetworkDiagnostics, Schnittstelle IPacketCaptureBackend
    Protocol/                               Protokollanalyse (noch leer)
    Combat/                                 Schadensberechnung (Spieler, Treffer, Statistik)
src/Aion2DpsMeter.Capture.Npcap/            Npcap-Anbindung über SharpPcap 6.3.1
tests/Aion2DpsMeter.Core.Tests/             Unit-Tests für Core (mit simulierten Paketen)
tests/Aion2DpsMeter.Capture.Npcap.Tests/    Integrationstests gegen das installierte Npcap
```

Abhängigkeiten (nur in diese Richtung):

```
App ──────────────► Core ◄────────── Capture.Npcap ──► SharpPcap ──► Npcap (wpcap.dll)
Core.Tests ───────► Core
Capture.Npcap.Tests ──► Capture.Npcap
```

Core kennt weder die Oberfläche noch Npcap. Die Diagnoselogik (`NetworkDiagnostics`)
spricht nur mit der Schnittstelle `IPacketCaptureBackend`; in den Unit-Tests steckt
dahinter eine simulierte Anbindung, im Programm später `NpcapCaptureBackend`.
Die App ist noch nicht mit der Npcap-Anbindung verbunden.

### Passive Aufnahme

`NpcapCaptureBackend` öffnet Adapter nur zum Lesen (kein Promiscuous-Modus), sendet
und verändert keine Pakete und gibt von jedem Paket nur Zeitpunkt und Länge weiter.
Paketinhalte werden nicht gespeichert.

## Bauen, testen, starten

Alle Befehle im Ordner `Aion2DpsMeter` (dort, wo diese README liegt) ausführen:

```
dotnet build
dotnet test
dotnet run --project src/Aion2DpsMeter.App
```

Nur die Unit-Tests (ohne Npcap): `dotnet test tests/Aion2DpsMeter.Core.Tests`
Npcap-Befund anzeigen: `dotnet test tests/Aion2DpsMeter.Capture.Npcap.Tests --logger "console;verbosity=detailed"`

## Automatische Tests auf GitHub (CI)

Weil Smart App Control auf dem Entwicklungs-PC Tests zufällig blockiert, wird jeder
Stand zusätzlich auf GitHub gebaut und getestet: `.github/workflows/ci.yml`
(Windows-Runner, .NET 10, `dotnet build -c Release`, `dotnet test -c Release`).
Die Testergebnisse (.trx) werden als Artefakt „testergebnisse“ gespeichert.

| Tests | Läuft in CI? |
|---|---|
| `Aion2DpsMeter.Core.Tests` (Schadensberechnung, Netzwerkdiagnose mit simulierten Paketen, RecordingLoader) | **ja** |
| `NpcapCaptureBackendTests` (Npcap-Version, Adapterliste) – Kategorie `RequiresNpcap` | **nein** – braucht installiertes Npcap |
| `PcapFileSourceTests` (pcap/pcapng lesen) – Kategorie `RequiresNpcap` | **nein** – das Lesen läuft über Npcaps wpcap.dll |
| `LiveCaptureTests` (10-s-Aufnahme) – Kategorien `LiveCapture`, `RequiresNpcap` | **nein** – braucht Npcap und echten Adapter |

Npcap wird auf dem Runner bewusst nicht installiert (Npcap-Lizenz). Die mit
`RequiresNpcap` markierten Tests können nur lokal laufen.

## Bekannter Laufzeitblocker: Smart App Control

Auf dem Entwicklungs-PC ist die Windows-Funktion **Smart App Control** eingeschaltet
und bleibt bewusst eingeschaltet. Sie blockiert selbst gebaute, nicht signierte
Dateien, die Microsoft unbekannt sind. Typische Meldung:

```
System.IO.FileLoadException: Could not load file or assembly '...\Aion2DpsMeter.<Name>.dll'.
Eine Anwendungssteuerungsrichtlinie hat diese Datei blockiert. (0x800711C7)
```

- `dotnet build` ist davon **nicht** betroffen.
- `dotnet test` und der App-Start können betroffen sein – je nachdem, welche Datei
  nach dem Build blockiert wird. Das entscheidet Windows pro Datei-Hash und kann sich
  nach jedem Build ändern.
- Nachweis im Ereignisprotokoll: *Microsoft → Windows → CodeIntegrity → Operational*,
  Ereignis-IDs **3077**, **3033** und **3118** (Regel `VerifiedAndReputableDesktop`).
- Es werden keine Sicherheitseinstellungen geändert und keine Umgehungen eingebaut.

Details: [docs/Diagnose-Phase1-Blockierung.md](docs/Diagnose-Phase1-Blockierung.md)

### Geprüfte Stände

**Phase 2 (12.10.2026, ca. 00:20):** Build fehlerfrei; Core.Tests 45/45 und
Capture.Npcap.Tests 3/3 bestanden, keine Sperre. Npcap 1.89 erkannt, 9 Adapter gelistet.

**Phase 3 (12.10.2026, 00:27) – aktueller Stand:**

| Schritt | Ergebnis |
|---|---|
| `dotnet build` | erfolgreich, 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **nicht ausgeführt** – `Aion2DpsMeter.Core.Tests.dll` von Smart App Control blockiert („Catastrophic failure“, 0 Tests gelaufen) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **10 von 10 fehlgeschlagen** – alle wegen Sperre von `Aion2DpsMeter.Core.dll` bzw. `Aion2DpsMeter.Capture.Npcap.dll`, nicht wegen Testlogik |
| Neue Phase-3-Funktionen (pcap/pcapng laden) | kompiliert, aber **zur Laufzeit noch nicht geprüft** |
| Live-Testaufnahme (10 s) | **nicht ausgeführt** (siehe unten) |
| App-Start | **nicht ausgeführt** (bewusst). Das Fenster wurde noch nie erfolgreich geöffnet. |

### Optionale Live-Testaufnahme

`LiveCaptureTests` nimmt 10 Sekunden rein passiv auf und zählt nur Pakete/Bytes.
Sie läuft nur, wenn die Umgebungsvariable gesetzt ist (PowerShell):

```
$env:AION2DPS_LIVE_CAPTURE_ADAPTER = "I225-V"
dotnet test tests/Aion2DpsMeter.Capture.Npcap.Tests --filter "Category=LiveCapture" --logger "console;verbosity=detailed"
Remove-Item Env:AION2DPS_LIVE_CAPTURE_ADAPTER
```

## Wichtige Hinweise

### Risiko: Kontosperre

Die Betriebsrichtlinien von AION 2 verbieten die Nutzung „nicht autorisierter
Programme“. DPS-Meter, die mit Npcap den Netzwerkverkehr mitlesen, können
darunter fallen – auch wenn sie rein passiv arbeiten. **Eine Kontosperre ist
möglich.** Die Nutzung geschieht auf eigenes Risiko. Lies die aktuellen
Nutzungsbedingungen des Spiels, bevor du das Programm mit dem Spiel verwendest.

Dieses Projekt arbeitet ausschließlich passiv: Es liest oder verändert keinen
Spielspeicher, injiziert keinen Code, sendet oder verändert keine Spielpakete
und automatisiert keine Spielfunktionen. Daten werden nur lokal gespeichert
und nicht automatisch übertragen.

### Overlay ≠ echter Schaden

Ein funktionierendes Fenster oder Overlay bedeutet **nicht**, dass echter
AION-2-Schaden gelesen wird. Das sind zwei getrennte Ziele: Werte werden erst
angezeigt, wenn die zugehörigen Nachrichten anhand selbst erfasster Daten
nachweislich verstanden sind. Unsichere oder fehlende Daten werden gekennzeichnet.
