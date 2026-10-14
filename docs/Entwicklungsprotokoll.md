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
- Es gibt **keinen** Decoder für Spielwerte. Aus Netzwerkpaketen werden keine AION-2-Werte
  (Schaden, Namen, Opcodes) gelesen; die Berechnung bekommt noch keine echten Spieldaten.
  Seit Phase 4 wird nur der **Rahmenaufbau** als Hypothese geprüft (siehe unten).
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

## Phase 2 – Abschluss: was echt geprüft ist und was nur simuliert

Stand 12.10.2026, 01:15. Build 0 Warnungen/0 Fehler; Tests: Core 69/69,
Capture.Npcap 10/10, App 4/4 bestanden; keine Sperre durch Smart App Control
während dieses Laufs. Smart App Control ist eingeschaltet und wurde nicht verändert.

| Punkt | Umsetzung | Wie geprüft |
|---|---|---|
| Abstraktion Adapter/Aufnahme | `IPacketCaptureBackend`, `NetworkAdapterInfo`, `CapturedPacket` (Core) | Unit-Tests |
| Npcap getrennt von Kernlogik | eigenes Projekt `Aion2DpsMeter.Capture.Npcap`; Core hat keine Paketabhängigkeit | Projektverweise geprüft |
| Paketanzahl, Statuswechsel, Fehlerfälle, Abbruch | `NetworkDiagnostics`, `PacketCounter`, `CaptureErrorKind` | **nur simuliert** (`FakeCaptureBackend`, 23 + 5 Tests) |
| Npcap installiert? | Dienst läuft, Npcap 1.89, „Nur Administratoren“ aus – nur lesend geprüft | `GetInfo`-Test gegen echtes Npcap: **echt** |
| Adapterliste | 9 Adapter gefunden | Test gegen echtes Npcap: **echt** |
| Unbekannter Adapter | `AdapterNotFound` | Test gegen echtes Npcap: **echt** |
| pcap/pcapng lesen | `PcapFileSource` | echtes libpcap, aber **synthetische** Testdateien |
| **Echte Paketerfassung auf einem Adapter** | `NpcapCaptureBackend.CaptureAsync` | **echt, manuell im Fenster durch den Nutzer (12.10.2026, ~01:13):** Intel Ethernet I225-V, Start → Stopp: 935 Pakete, 187,72 KB, Zeitraum 00:05, 181,5 Pakete/s, Status danach „Aufnahme beendet“. Kein automatischer Test – `LiveCaptureTests` wurde weiterhin nie eingeschaltet ausgeführt. |
| Anzeige im Fenster | `MainWindow` | **vom Nutzer gestartet (12.10.2026, ~01:13): Fenster geht auf.** Zeigt „Bereit – 9 Netzwerkadapter gefunden“, die Npcap-Version, schlägt den Intel-Ethernet-Adapter vor; Start, Live-Zählung und Stopp funktionieren (siehe Zeile darüber). Bei einem neuen Build kann Smart App Control das Fenster wieder blockieren. |

## Phase 3 – Aufnahme eines Programms speichern (Stand 12.10.2026, ~01:30)

Neu:

- `ProcessConnections.cs` (Core): Verbindungen je Prozess, Gruppierung für die Auswahl,
  Bau eines libpcap/BPF-Filters aus den lokalen Ports (`ProcessConnectionFilter`),
  Dateiname für Aufnahmen.
- `CaptureOptions` (Core): Filter und Speicherpfad werden an die Aufnahme übergeben;
  neue Fehlerarten `InvalidFilter`, `SaveFileFailed`.
- `WindowsProcessConnectionSource` (Npcap-Projekt): liest die Windows-Verbindungstabelle
  (wie `netstat -ano`) nur lesend, **nur IPv4**.
- `NpcapCaptureBackend`: setzt den Filter im Treiber und schreibt die Pakete optional in eine
  `.pcap`-Datei (bestehende Dateien werden nie überschrieben). Gespeichert wird nur lokal unter
  `Dokumente\Aion2DpsMeter\Aufnahmen`; diese Dateien enthalten Paketinhalte und gehören nicht ins Repo.
- Fenster: „Programme laden“, Programmauswahl, „Nur Verbindungen dieses Programms aufnehmen“,
  „Aufnahme als .pcap-Datei speichern“.

Einschränkungen: Der Filter wird beim Start aus den gerade offenen Verbindungen gebaut –
Verbindungen, die das Programm später öffnet, fehlen. (Seit 13.10.2026 behoben, siehe
„Programmfilter erfasst jetzt auch später geöffnete Verbindungen“.) „port“ passt auf Quell- und Zielport,
vereinzelt können fremde Pakete mit gleichem Port enthalten sein. Kein IPv6.
Der Name der AION-2-Programmdatei wird nicht vorausgesetzt; man wählt das Programm aus der Liste.

| Prüfung | Ergebnis |
|---|---|
| `dotnet build` Core, Npcap, Testprojekte | 0 Warnungen, 0 Fehler |
| App kompiliert (Prüf-Build in separatem Ordner, weil das Fenster noch offen war) | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` (74) | **0 ausgeführt – alle blockiert**: Smart App Control sperrt die neu gebaute `Aion2DpsMeter.Core.dll` |
| `Aion2DpsMeter.Capture.Npcap.Tests` (16) | **0 ausgeführt – alle blockiert** (gleiche Ursache) |
| Neue Funktionen im Fenster | **nicht geprüft** (App noch nicht neu gebaut, Fenster nicht gestartet) |
| Voller Build nach Schließen des Fensters (01:21) | 0 Warnungen, 0 Fehler |
| Tests 01:21 | App.Tests **4/4 bestanden**; Core.Tests **0/74**, Capture.Npcap.Tests **0/16** – alle wegen gesperrter `Aion2DpsMeter.Core.dll` (16 Sperreinträge 3077). Das Fenster braucht dieselbe `Core.dll` und wird daher voraussichtlich ebenfalls nicht laufen. |
| Startversuch Nutzer 01:22 | Wie erwartet blockiert (`Aion2DpsMeter.Core.dll`). Die neue Fehlermeldung mit Verweis auf `startfehler.log` erschien – die Startfehler-Anzeige funktioniert damit nachweislich. |

## Auswertung gespeicherter Aufnahmen (Stand 12.10.2026, ~01:40)

Neu in `Core/Analysis`:

- `PacketHeaderParser`: liest nur Standard-Kopfzeilen (Ethernet inkl. VLAN, Loopback, Raw-IP;
  IPv4/IPv6; TCP/UDP). IPv4-Folgefragmente und IPv6-Erweiterungsheader werden nicht ausgewertet.
- `RecordingAnalyzer`: je Verbindung Pakete, Leitungs- und Nutzdaten-Bytes **je Richtung**
  (lokal ↔ Gegenstelle, wenn die eigenen IP-Adressen bekannt sind), Zeitraum, Pakete/s,
  Verteilung der Nutzdatengrößen und eine **Entropie-Schätzung** (≥ 7,5 Bit/Byte „wirkt
  verschlüsselt oder komprimiert“, < 6 „wirkt eher unverschlüsselt“, dazwischen „unklar“,
  unter 1024 Byte „zu wenig Daten“). Das ist ausdrücklich nur eine Schätzung.
- `RecordingReport`: deutscher Textbericht; im Fenster über „Aufnahme auswerten…“.
- Das Spielprotokoll wird **nicht** gedeutet – keine Opcodes, keine Schadenswerte.

Gefundener und behobener Fehler: libpcap überschrieb beim Speichern eine vorhandene Datei
trotz `FileMode.CreateNew`. Jetzt wird vorher selbst geprüft. Die Tests haben zusätzlich ein
Zeitlimit, damit ein Fehler den Testlauf nicht endlos blockiert.

| Prüfung (01:37) | Ergebnis |
|---|---|
| `dotnet build` | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **103/103 bestanden** (inkl. 13 Parser- und 13 Auswertungstests mit synthetischen Paketen) |
| `Aion2DpsMeter.App.Tests` | **4/4 bestanden** |
| `Aion2DpsMeter.Capture.Npcap.Tests` (17) | **0 ausgeführt** – Smart App Control blockiert die neu gebaute `Aion2DpsMeter.Capture.Npcap.Tests.dll`. Damit sind ungeprüft: der Fix gegen das Überschreiben, Filter/Speichern, die Verbindungstabelle und die Auswertung einer echten .pcap-Datei. |
| Vorheriger Lauf (01:2x, vor dem Fix) | Npcap-Tests 15/17 bestanden; 2 Fehler: ungültiger Testfilter „tcp and udp“ (Testfehler) und Überschreiben vorhandener Dateien (Programmfehler, jetzt behoben) |
| Fenster | nicht von mir gestartet. Startversuch Nutzer 01:39: blockiert – `Aion2DpsMeter.Capture.Npcap.dll` (Smart App Control); Fehlermeldung erschien korrekt. |

## TCP-Stromzusammensetzung (Stand 12.10.2026, ~01:55)

`Core/Analysis/TcpStreamReassembler.cs` – allgemeines TCP nach RFC 9293, nichts Spielspezifisches:

- setzt Segmente je Verbindung **und Richtung** in die richtige Reihenfolge,
  liefert lückenlose Daten mit Stromposition (`TcpStreamData`),
- erkennt vollständige Duplikate und schneidet Überlappungen ab („zuerst angekommen gilt“),
- meldet Lücken (`TcpStreamGap`) nach einem Zeitlimit, bei zu vielen wartenden Bytes
  oder am Ende der Aufzeichnung (`Flush`) und macht danach weiter,
- verarbeitet den Überlauf der 32-Bit-Sequenznummer,
- erkennt Aufnahmen, die mitten in einer Verbindung beginnen (kein SYN),
- beendet eine Richtung bei FIN (erst wenn alle Daten davor da sind) oder RST (wartende Daten verworfen).

Der Paket-Parser liefert dafür jetzt Sequenznummer, Bestätigungsnummer und Flags.
Es werden keine AION-2-Ports, -Opcodes, -Formate oder -Schlüssel verwendet.

| Prüfung (01:55) | Ergebnis |
|---|---|
| `dotnet build` | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **121/121 bestanden** (davon 18 neue Tests zur Stromzusammensetzung, synthetische Segmente) |
| `Aion2DpsMeter.App.Tests` | **0/4** – `Aion2DpsMeter.App.dll` von Smart App Control blockiert |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **0/17** – `Aion2DpsMeter.Capture.Npcap.dll` von Smart App Control blockiert; damit weiterhin ungeprüft: Überschreib-Schutz, Filter/Speichern, Verbindungstabelle, Auswertung echter .pcap-Dateien |
| Fenster | nicht von mir gestartet. Startversuche Nutzer 01:54/01:55: blockiert – `Aion2DpsMeter.App.dll` (Ereignis 3077); da die App-Datei selbst gesperrt ist, kann auch keine Fehlermeldung erscheinen. |

## Phase 4 – Hypothesen-Prüfung zum Datenaufbau (Stand 12.10.2026, ~23:55)

Geprüft werden öffentlich beschriebene **Community-Angaben** – ausdrücklich als Hypothesen, nicht als
gesicherte Fakten. Quelle der Angaben: das GPL-3.0-Projekt
[cyberbadger6969/aion2-dps-meter](https://github.com/cyberbadger6969/aion2-dps-meter) (Beschreibung in
README und Quelltext). **Es wurde kein Code daraus übernommen**; die Prüfung ist nach den beschriebenen
Fakten selbst geschrieben, damit dieses Projekt keine GPL-Ableitung wird.

Geprüfte Angaben:

- Rahmen beginnen mit einer Länge als Varint (LEB128, höchstens 5 Bytes). Rahmengröße = Wert +
  Varint-Bytes − 4, Wert mindestens 6, höchstens 65.535 Bytes (ohne Bündel höchstens 16.384).
  Nach dem Varint kann ein Byte 0xF0–0xFE folgen.
- LZ4-Bündel: Kennzeichen FF FF, dann entpackte Größe (4 Bytes, Little Endian), dann ein roher LZ4-Block;
  entpackt folgen wieder Rahmen (Füllbytes 00 erlaubt, höchstens 4 Ebenen verschachtelt).
- Heartbeat: Rahmen beginnen mit 0E 00 36 (auch 06 00 36 beschrieben), etwa 19 pro Sekunde.

Neu in `Core/Analysis`:

- `StreamHypothesisCheck` (Datei `ProtocolHypothesisCheck.cs`): bekommt die Ereignisse des
  `TcpStreamReassembler` je Richtung und zählt Heartbeat-Treffer (überall im Strom und genau an
  Rahmenanfängen), den Anteil des Stroms, der lückenlos in Rahmen aufgeht, Neuaufsetzer sowie
  gefundene, fehlerfrei entpackte und vollständig zerlegte Bündel. Nach Fehlern oder Lücken wird nur an
  einem Heartbeat-Muster neu aufgesetzt. Ergebnis je Hypothese: „bestätigt / nicht bestätigt / unklar /
  zu wenig Daten“; die Schwellen stehen im Code an den Eigenschaften `Heartbeat`, `Framing` und `Lz4`.
- Wichtig für die Deutung: Auch **Zufallsdaten** lassen sich oft über weite Strecken scheinbar in
  Varint-Rahmen zerlegen (Test: 64 KiB Zufall → 56 % „Abdeckung“). „Rahmen bestätigt“ verlangt
  deshalb zusätzlich, dass mindestens 10 Heartbeats zu mindestens 90 % genau an Rahmenanfängen liegen.
- `Lz4BlockDecoder`: selbst geschriebener Entpacker für rohe LZ4-Blöcke nach der öffentlichen
  Blockformat-Beschreibung (github.com/lz4/lz4, `doc/lz4_Block_format.md`). Bewusst **keine** NuGet-Bibliothek
  (z. B. K4os.Compression.LZ4), damit Core frei von Fremdpaketen bleibt und keine weitere fremde DLL unter
  Smart App Control laufen muss. Getestet nur mit von Hand gebauten Blöcken.
- `RecordingAnalyzer` setzt TCP-Ströme jetzt zusammen und hängt das Ergebnis je Richtung an die
  Verbindung; der Bericht im Fenster „Aufnahme auswerten…“ zeigt es pro TCP-Verbindung und Richtung an.
- Optionaler Test `RecordingHypothesisTests` (Npcap-Testprojekt): wertet eine eigene Aufnahme nur lesend
  aus, wenn `AION2DPS_HYPOTHESIS_PCAP` (Pfad) gesetzt ist; optional `AION2DPS_HYPOTHESIS_LOCAL` (eigene IP).

### Ergebnis an der echten Aufnahme

`aufnahme-20261012-232701.pcap` (vom Nutzer mit AION 2 aufgenommen, 9.319 Pakete, 121,7 s), von mir
über den optionalen Test **nur gelesen** ausgewertet, Spielverbindung 192.168.178.20:64694 ↔
193.202.112.138:13328. Die Aufnahme beginnt mitten in der Verbindung (kein SYN), daher wurde erst ab dem
ersten Heartbeat-Muster gelesen. Keine TCP-Lücken.

| Richtung | Strom | Heartbeat | Varint-Rahmen | LZ4-Bündel |
|---|---|---|---|---|
| Server → Client | 519.567 B | 0E 00 36: 2.434, 06 00 36: 2 (20,0/s) – **bestätigt** | 99,9 % in 17.659 Rahmen, 0 Neuaufsetzer, 90,6 % der Heartbeats am Rahmenanfang – **bestätigt** (knapp über der 90-%-Schwelle) | 227 gefunden, 227 entpackt, 227 vollständig in 4.462 innere Rahmen zerlegt – **bestätigt** |
| Client → Server | 84.403 B | 0 Treffer – **nicht bestätigt** | nicht prüfbar (kein Verbindungsanfang, kein Heartbeat zum Aufsetzen) – **unklar** | keine gefunden – **unklar** |

Deutung (vorsichtig): Für die Richtung Server → Client passen alle drei Angaben zu den echten Daten. Für
Client → Server lässt sich mit dieser Aufnahme nichts zum Rahmenaufbau sagen. Die beiden 443-Verbindungen
(TLS) zeigen erwartungsgemäß keine Treffer. Inhalte der Rahmen (Opcodes, Schaden) werden weiterhin
**nicht** gedeutet.

| Prüfung (~23:50) | Ergebnis |
|---|---|
| `dotnet build` Core, Core.Tests, Capture.Npcap.Tests | 0 Warnungen, 0 Fehler |
| App (Prüf-Build in separatem Ordner) | 0 Warnungen, 0 Fehler |
| Voller `dotnet build` / Build von App.Tests | **fehlgeschlagen nur wegen Dateisperre**: das Fenster (`Aion2DpsMeter.App`) war offen und sperrt `bin\…\Aion2DpsMeter.Core.dll`. Nicht beendet. |
| `Aion2DpsMeter.Core.Tests` | **150/150 bestanden** (davon 30 neu: 17 zur Hypothesen-Prüfung, 13 zum LZ4-Entpacker; nur synthetische Daten) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **18/18 bestanden**, keine Sperre durch Smart App Control; darin die echte Aufnahme (siehe oben) |
| `Aion2DpsMeter.App.Tests` | 4/4 bestanden, aber gegen den **alten** Build (Neubau wegen Sperre nicht möglich; App-Code unverändert) |
| Anzeige im Fenster | **nicht geprüft**: Fenster muss geschlossen, neu gebaut und vom Nutzer neu gestartet werden |

### Phase 4b – Häufigkeit der Nachrichtentypen (Stand 13.10.2026, ~00:05)

Anzeige im Fenster vom Nutzer bestätigt (23:53: gleiche Zahlen wie oben). Neu: `StreamHypothesisCheck`
zählt je Richtung die **Nachrichtentypen** – die 2 Bytes nach Längenangabe/Kennbyte, so wie sie im Strom
stehen – in normalen Rahmen und in vollständig zerlegten LZ4-Bündeln (`MessageTypeStatistics`: Anzahl,
davon in Bündeln, Rahmengröße min/max/Ø/häufigste, Anzahl Größen). Der Bericht listet die 20 häufigsten
Typen, aber **nur**, wenn die Rahmen-Hypothese in dieser Richtung bestätigt ist (sonst wären es
Zufallsbytes). Zusätzlich wird die Community-Hypothese „Typ 04 38 = Schaden“ nur **gezählt**, nicht gedeutet.

Echte Aufnahme, Server → Client: 73 verschiedene Typen in 21.894 Rahmen
(= 17.659 − 227 Bündel-Rahmen + 4.462 innere, passt auf). Häufigste: 2B 38 (3.456), 1D 37 (2.863),
00 8D (2.616), 00 36 (2.434, immer 11 B), **04 38: 1.386 (11,4/s), davon 273 in Bündeln, 27–43 B,
Ø 33,0, 16 verschiedene Größen**; umgekehrte Bytefolge 38 04: 0. Auffällig, aber ungedeutet: Bei den
meisten häufigen Typen ist das zweite Byte 0x38 oder 0x37. Ob 04 38 tatsächlich Schaden enthält, ist
damit **nicht** geprüft – nur, dass der Typ vorkommt.

| Prüfung (~00:03) | Ergebnis |
|---|---|
| Voller `dotnet build` (23:53, Fenster gerade neu gestartet) | 0 Warnungen, 0 Fehler |
| Voller `dotnet build` nach den Änderungen | nur Dateisperre durch das offene Fenster (PID 36560), nicht beendet |
| Build Core, Core.Tests, Capture.Npcap.Tests; App-Prüfbuild in separatem Ordner | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **153/153 bestanden** (3 neue Tests zu Nachrichtentypen, Berichtstest erweitert) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **18/18 bestanden**, darin die echte Aufnahme (nur gelesen) |
| Anzeige der Typenliste im Fenster | **vom Nutzer geprüft (13.10.2026, 00:09):** nach Neubau zeigt „Aufnahme auswerten…“ die Typenliste und die Zeile zu 04 38 mit denselben Zahlen wie der Testlauf |

### Programmfilter erfasst jetzt auch später geöffnete Verbindungen (Stand 13.10.2026, ~00:25)

Befund: Die Aufnahme `aufnahme-20261013-001052.pcap` (vor dem Einloggen gestartet, mit „Nur Verbindungen
dieses Programms aufnehmen“) enthielt keine Spielverbindung. Ursache im Code bestätigt: Der Filter wurde
**einmal beim Start** aus den gerade offenen lokalen Ports gebaut (`ProcessConnectionFilter.BuildFilter`);
Verbindungen, die erst danach entstehen (z. B. beim Login), wurden nie aufgenommen.

Neu:

- `ProcessPacketSelector<T>` (Core/Capture): liest die Verbindungstabelle während der Aufnahme alle
  0,5 s neu und hält jedes Paket 2 s zurück, bevor entschieden wird. So bleibt auch der Verbindungsaufbau
  (SYN) erhalten, der kurz vor dem Eintrag in der Tabelle gesendet wird. Ein Paket gehört zum Programm,
  wenn Quell- oder Zielport irgendwann während der Aufnahme ein lokaler Port des Programms war.
  Kein fest eingebauter Spielport – ausgewählt wird weiterhin über das Programm (PID).
- `CaptureOptions.Process` / `ProcessSelection`; `NpcapCaptureBackend` nimmt dann ohne Treiberfilter auf,
  wählt im Programm aus und speichert nur die ausgewählten Pakete. Beim Stoppen werden die noch
  zurückgehaltenen Pakete entschieden und gespeichert.
- Fenster: Start mit Programmauswahl braucht keine offenen Verbindungen mehr; der Hinweis lautet
  „Verbindungen werden laufend nachgelesen, Anzeige ca. 2 s verzögert“.

Grenzen: Verbindungen, die kürzer als ca. 0,5 s offen sind, können fehlen. Nutzt später ein anderes
Programm denselben lokalen Port, wird dessen Verkehr mit aufgenommen. Der Zähler im Fenster läuft ca. 2 s
hinterher. Das Programm muss bei „Programme laden“ schon in der Liste stehen, also schon mindestens eine
Verbindung haben. Wird ein Launcher statt des Spiels gewählt, fehlt das Spiel weiterhin.

| Prüfung (~00:25) | Ergebnis |
|---|---|
| Build Core, Core.Tests, Capture.Npcap.Tests; App-Prüfbuild in separatem Ordner | 0 Warnungen, 0 Fehler |
| Voller `dotnet build` | nur Dateisperre durch das offene Fenster (PID 11868), nicht beendet |
| `Aion2DpsMeter.Core.Tests` | **163/163 bestanden** (10 neue Tests mit simulierter Verbindungstabelle und Uhr) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **19/19 bestanden**. Neu `ProcessSelectionNpcapTests`: **echt** gegen Npcap-Loopback und die echte Windows-Verbindungstabelle. Der Test öffnet erst nach dem Start eine eigene Verbindung über 127.0.0.1; SYN und Nutzdaten landen in der Datei, nur Verkehr des Testprozesses. 3 Wiederholungen bestanden. Erster Lauf scheiterte an einer falschen Testannahme (Verbindung des Testprozesses zum Testrunner gehört tatsächlich zum Programm), Test korrigiert. |
| Mit dem Spiel im Fenster | **nicht geprüft** – muss der Nutzer nach Neubau ausprobieren |

### Aufnahme mit Verbindungsanfang (Stand 13.10.2026, ~00:35)

`aufnahme-20261013-001357.pcap` (vom Nutzer **ohne** Programmfilter aufgenommen, daher 297 Verbindungen,
119.477 Pakete; nur lokal gespeichert). Spielverbindung 192.168.178.20:59835 ↔ 193.202.112.138:13328,
74,9 s, **mit Verbindungsanfang**. Von mir nur gelesen ausgewertet.

- Server → Client (108.566 B): Heartbeat 1.445 (19,3/s) – bestätigt; Rahmen 100,0 % in 4.033 Rahmen,
  0 Neuaufsetzer, 99,4 % der Heartbeats am Rahmenanfang – bestätigt; LZ4 16 gefunden, 16 entpackt,
  15 vollständig zerlegt – bestätigt. 122 Typen; häufigste 1D 37 (1.692), 00 36 (1.445), 1C 37 (522).
  **04 38 kommt nicht vor.** Ob in dieser Aufnahme gekämpft wurde, ist mir nicht bekannt.
- Client → Server (16.959 B): keine Heartbeats, aber vom ersten Byte an **100,0 % in 1.393 Rahmen,
  0 Neuaufsetzer**. Die 2 Bytes nach der Länge sind dort aber fast alle verschieden (1.375 Werte bei
  1.393 Rahmen, meist 11 B) – sie wirken in dieser Richtung nicht wie ein Typfeld.

Änderung der Regel (nach Sichtung dieser Daten, offen dokumentiert): „Rahmen bestätigt“ gilt jetzt auch
**ohne** Heartbeats, wenn ab Verbindungsanfang gelesen wurde, ≥ 99 % in ≥ 100 Rahmen aufgehen und kein
einziger Rahmenkopf ungültig war. Begründung: Bei Zufallsdaten reißt die Kette nach wenigen Rahmen; ein Test
prüft 200 Zufallsströme à 16 KiB, keiner wird bestätigt. Der Bericht warnt außerdem, wenn mehr als die Hälfte
der Rahmen einen eigenen „Typ“ hat.

| Prüfung (~00:35) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Fehler |
| Voller Build | nur Dateisperre durch das offene Fenster (PID 11868), nicht beendet |
| `Aion2DpsMeter.Core.Tests` | **166/166 bestanden** |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **19/19 bestanden** (darin die Auswertung dieser Aufnahme, nur gelesen) |

### Vergleichsaufnahmen und Inhaltsanalyse 04 38 (Stand 13.10.2026, ~00:50)

Was in den Aufnahmen getan wurde (Angaben des Nutzers, 13.10.2026):

| Aufnahme | Tätigkeit | 04 38 |
|---|---|---|
| `aufnahme-20261012-232701.pcap` | erste Aufnahme mit Kampf (Einzelheiten unbekannt) | 1.386 (11,4/s) |
| `aufnahme-20261013-001357.pcap` | nur gestanden | **0** |
| `aufnahme-20261013-002252.pcap` | nur Kampf an der Puppe, ca. 60 s (mit neuem Programmfilter) | **1.619 (24,5/s)**, häufigster Typ |

Neu: `DamageRecordHypothesis` (Core/Analysis) zerlegt den Inhalt von 04-38-Rahmen nach der öffentlichen
Beschreibung in cyberbadger6969/aion2-dps-meter (GPL-3.0) – **selbst geschrieben, kein Code übernommen**.
Felder laut Hypothese: Ziel (Varint), Schalter (Varint; untere 4 Bit = Layout 4–7, Bit 0x20 = Zusatzwerte),
Flag (Varint), Akteur (Varint), Fertigkeit (u32 LE), 1 Byte, Art (Varint, 3 = „kritisch“), dann
layoutabhängig 8/11/10/12/14 Bytes übersprungen, zwei Werte (Varint; der zweite ist der „Kandidatenwert“,
dort als Schaden beschrieben), ggf. Zusatzwerte und `03 00` + Varint. Layout 0 im ersten Datensatz =
Ankündigung ohne Wert. Folge-Datensätze nach `x 00` (x = 1–7). **Eigene Ergänzung nach echten Daten:**
Eine solche Kennung direkt am Rahmenende zählt als sauberes Ende (fast alle echten Rahmen enden mit `01 00`).

`DamageCandidateReport`: Zusammenfassung je Richtung und CSV (Semikolon, UTF-8) mit einer Zeile je
Datensatz: Zeit, Sekunden seit Aufnahmebeginn, alle Felder, Kandidatenwert und Rohbytes. Fenster: Knopf
**„04-38-Kandidaten…“** – Aufnahme wählen, die CSV wird unter `Dokumente\Aion2DpsMeter\Auswertungen`
gespeichert (vorhandene Dateien werden nie überschrieben), die ersten 300 Datensätze werden angezeigt.
Kandidatenwerte sind **nicht** als Schaden gesichert; sie dienen dem Abgleich mit den Zahlen im Spiel.

Ergebnis (nur gelesen):

- Puppe (`002252`), 1.619 Rahmen: 950 vollständig zerlegt, 651 Ankündigungen, 18 teilweise (nach `01 00`
  folgt noch ein 2-Byte-Varint unbekannter Bedeutung), 0 nicht passend → Aufbau zu 98,9 % passend.
  968 Datensätze, Layouts 4: 140, 6: 828. 7 verschiedene Ziele, 12 Akteure, 50 Fertigkeiten.
  Kandidatenwert (Akteur ≠ Ziel): Summe 2.835.038, Min 334, Max 54.232, Ø 3.424. Akteur = Ziel: 140
  Datensätze, Summe 118.132. Nach Ziel: 29769 (489 Datensätze), 25357 (244), 19974 (95).
- Erste Aufnahme (`232701`), 1.386 Rahmen: 987 vollständig, 366 Ankündigungen, 33 teilweise, 0 nicht passend
  (97,6 %). 1.020 Datensätze, ein Akteur mit 988 davon; Kandidatenwert Summe 2.431.307, Min 30, Max 51.795.
- Stehen (`001357`): keine 04-38-Rahmen.

Wer davon der eigene Charakter und was die Puppe ist, ist **nicht** bekannt – dafür ist der Abgleich da.

| Prüfung (~00:50) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Warnungen, 0 Fehler |
| Voller Build | nur Dateisperre durch das offene Fenster (PID 18064), nicht beendet |
| `Aion2DpsMeter.Core.Tests` | **182/182 bestanden** (16 neue Tests zur 04-38-Zerlegung und CSV, synthetische Daten). Erster Lauf: 1 Fehler im Test selbst (Sonderfall Layout 6 übersehen), Test korrigiert. |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **20/20 bestanden**; darin die Auswertung der drei echten Aufnahmen (nur gelesen) |
| Knopf „04-38-Kandidaten…“ im Fenster | **vom Nutzer geprüft (13.10.2026, 00:36):** nach Neubau funktioniert er, CSV `aufnahme-20261013-003512-0438.csv` wurde angelegt |

### Erster kontrollierter Abgleich mit dem Spiel (13.10.2026, ~00:40)

Aufnahme `aufnahme-20261013-003512.pcap` (Puppe). Der Nutzer notierte im Spiel die Schadenszahlen
1580, 2280, 2350, 1354; die Kampfanalyse des Spiels (Strg+X) zeigte „Ziel: Trainingspuppe, 01:06,
Gesamtschaden 17,44K, DPS 263“. Gesucht wurde nur lesend in der CSV:

| Zahl im Spiel | in der CSV | Akteur | Ziel | Fertigkeit | Zeit |
|---|---|---|---|---|---|
| 1580 | Kandidatenwert, Rahmen 222 | 10398 | 29769 | 14020340 | 00:35:21.894 |
| 2350 | Kandidatenwert, Rahmen 517 | 10398 | 29769 | 14020340 | 00:35:38.643 |
| 1354 | Kandidatenwert, Rahmen 717 | 10398 | 29769 | 14020340 | 00:35:50.792 |
| 2280 | Kandidatenwert, Rahmen 604 | **9934** | **25357** | 15780007 | 00:35:42.795 |

Akteur 10398 (Akteur ≠ Ziel): 11 Datensätze, alle auf Ziel 29769, 4 Fertigkeiten; Summe der
Kandidatenwerte **15.964** (mit Zusatzwerten 16.268); erster Treffer 00:35:14.393, letzter 00:35:50.792
(36,4 s). Die Aufnahme deckt nur ca. 48,5 s ab, die Kampfanalyse 66 s.

Einschätzung: Drei von vier Zahlen stehen exakt als Kandidatenwert beim selben Akteur, Ziel und
derselben Fertigkeit → **starker Hinweis**, dass der Kandidatenwert der angezeigte Treffer-Schaden ist und
10398 der eigene Charakter. 2280 passt zu einem anderen Akteur und ist wohl Zufall oder eine fremde Zahl.
Die Summe (15.964) liegt unter 17.440; das ist mit der kürzeren Aufnahme vereinbar, **beweist aber
nichts** – ein Summenabgleich braucht eine Aufnahme, die den ganzen Kampf umfasst (Aufnahme vor dem
ersten Treffer starten, nach dem letzten stoppen, Kampfanalyse vorher zurücksetzen). Die Zusatzwerte
(z. B. 2×70 bei 1580) sind in der angezeigten Einzelzahl nicht enthalten; ihre Bedeutung ist unbekannt.

### Summen-Abgleich mit der Kampfanalyse (13.10.2026, ~00:42)

Aufnahme `aufnahme-20261013-003826.pcap` (Puppe), Kampfanalyse des Spiels: „Ziel: Trainingspuppe, 00:41,
Gesamtschaden 349,52K (DPS 8,46K)“. CSV und Aufnahme nur gelesen. Spielverbindung: 0 TCP-Lücken, alle 27
LZ4-Bündel vollständig zerlegt.

| Größe | Wert |
|---|---|
| 04 38, Akteur 10398 → Ziel 29769 | 116 Datensätze, Summe **347.484**, Min 696, Max 24.822; Treffer von 2,4 s bis 17,0 s |
| davon in „teilweise“ zerlegten Rahmen | 16 Datensätze (Fertigkeit 14380007), 35.834 – Datensatz gelesen; danach folgt `01 00` + ein 2-Byte-Varint (283–311), kein Schadenswert erkennbar |
| 04 38 inkl. Zusatzwerte | 364.629 (104,3 %) → Zusatzwerte gehören offenbar **nicht** in die Summe |
| 05 38, Akteur 10398 → 29769, Effekt 0x0A | 7 Datensätze, Betrag 312, 312, 284 × 5 = **2.044**; Abstand ≈ 1 s (6,5 s bis 21,1 s), Fertigkeit 14050000 |
| 05 38, Akteur 10398, Effekt 0x08 | 7 Datensätze **ohne** erkennbaren Betrag (das gelesene „36“ ist das erste Byte eines festen Endes `24 64 D6 00`) |
| **04 38 + 05 38 (0x0A)** | **349.528** |
| Kampfanalyse | 349,52K (gerundet 349.515–349.524, abgeschnitten 349.520–349.529) |

Einschätzung: Mit dem Schaden über Zeit liegt die Summe bei 349.528 – das passt, **wenn** das Spiel die
Anzeige abschneidet, und liegt 4 über dem Bereich, wenn es rundet. Wie das Spiel rundet, ist nicht bekannt.
Ohne 05 38 fehlen 2.036 (0,6 %). Der Aufbau von 05 38 passt nur teilweise zur Beschreibung: Bei 113 von 114
lesbaren Rahmen bleiben Bytes übrig (bei 10398 das feste Ende `24 64 D6 00`), und Effekt 0x08 hat keinen
Betrag. Die DPS des Spiels (8,46K = Summe ÷ 41 s) ist nicht direkt vergleichbar, weil die Kampfdauer des
Spiels länger ist als der Zeitraum der Treffer (2,4–21,1 s).

### Erste echte Funktion: Live-Schaden (Stand 13.10.2026, ~01:15)

Neu:

- `DotRecordHypothesis` (05 38): Ziel, Effekt-Byte, Akteur, ein Varint, Fertigkeit (u32 ÷ 100), Betrag – nach der
  Community-Beschreibung, selbst geschrieben. Gewertet wird live **nur Effekt 0x0A** (der einzige, der im
  Abgleich vorkam); 0x08 hat keinen Betrag. Die CSV der „04-38-Kandidaten…“ enthält jetzt auch 05-38-Zeilen
  (Spalte `Typ`).
- `LiveCombatDecoder` (Core/Analysis): setzt live TCP zusammen, liest Rahmen/Bündel und meldet Treffer aus
  04 38 und 05 38 (0x0A). Nur aus Richtungen mit mindestens 3 Heartbeat-Mustern – Folge: Bei einer Aufnahme
  mitten in der Verbindung können Treffer der ersten ca. 0,15 s fehlen (beim Nachspielen von `003512` genau
  ein Treffer).
- `LiveCombatMeter` (Core/Combat, threadsicher): Schaden je Verursacher, DPS ab dem eigenen ersten Treffer bis
  zum letzten Treffer im Kampf (mindestens 1 s), Anteil, Treffer, kritisch (Art = 3), Schaden über Zeit.
  Verursacher = Ziel wird verworfen und nur gezählt. Neuer Kampf, wenn nach 10 s ohne Treffer wieder einer
  kommt; „Zurücksetzen“ beginnt von vorn.
- `CaptureOptions.OnRawPacket`: Die Aufnahme gibt den Paketinhalt im Speicher an die Live-Auswertung weiter
  (wird dadurch nicht gespeichert). Fehler in der Auswertung beenden die Aufnahme nicht, sie werden gezählt.
- Fenster: Haken „Schaden live auswerten (Community-Hypothese, nur im Speicher)“ (Standard an) und Knopf
  **„Live-Schaden…“** – eigenes, normales Fenster (kein Overlay) mit Tabelle je Verursacher, Auswahl
  „Eigener Charakter“ (wird mit ► hervorgehoben; automatisch erkannt wird er nicht) und deutlichem Hinweis.
  Wegen der Programmauswahl kommen die Treffer ca. 2 s verzögert an.
- Nebenbei behoben: verstümmelte Umlaute in einer Npcap-Fehlermeldung („Nur Administratoren“).

Nachgespielt (Aufnahme-Datei statt Adapter, sonst derselbe Weg; nur gelesen):

| Aufnahme | Ergebnis |
|---|---|
| `003826` | 629 Treffer, 92 Selbsttreffer verworfen; **10398: 349.528** (davon über Zeit 2.044), 116 Treffer, 52 krit. – gleiche Summe wie die Handrechnung oben |
| `003512` | 497 Treffer; 13 Verursacher (mehrere Spieler an den Puppen) |
| `001357` (nur gestanden) | 0 Treffer |

| Prüfung (~01:15) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Warnungen, 0 Fehler |
| Voller Build | nur Dateisperre durch das offene Fenster (PID 21164), nicht beendet |
| `Aion2DpsMeter.Core.Tests` | **191/191 bestanden** (neu: 6 Live-Zähler, 3 Live-Decoder; synthetische Daten). Ein Testentwurf war falsch (alle Rahmen in einem Segment, Heartbeat-Sperre so nicht prüfbar) und wurde vor dem ersten Lauf korrigiert. |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **21/21 bestanden** |
| Live-Fenster mit dem laufenden Spiel | **vom Nutzer geprüft (13.10.2026, 00:57, Screenshot):** Aufnahme mit Programmauswahl „AION2 (PID 7076)“, Live-Fenster „Kampf 1 – läuft – 20,8 s“. Verursacher 10398: **321.664** (87 Treffer, 27 krit., über Zeit 2.196). Kampfanalyse des Spiels gleichzeitig: „Trainingspuppe, 00:17, Gesamtschaden 321,66K (DPS 18,33K)“ → **Summe stimmt** (321.664 ≙ 321,66K). DPS weicht ab (Live 20.822 vs. Spiel 18.330), weil die Zeitbasis verschieden ist: Live = eigener erster Treffer bis letzter Treffer im Kampf; das Spiel teilt offenbar durch ca. 17,5 s. Im Fenster war als „Eigener Charakter“ 65895 gewählt, nicht 10398. |

### Namen und eigener Charakter (Stand 13.10.2026, ~01:35)

Hypothese aus derselben Community-Quelle (selbst geschrieben, kein Code übernommen): Typ **33 36** = eigener
Charakter, **45 36 / 44 36** = andere Spieler; Inhalt: Kennung (Varint), 4 Bytes, 1 Byte (Bit 0 gesetzt),
Namenslänge (1 Byte, 1–36), Name; danach ggf. Server (u16, 1000–2999) und weitere Felder. Die
Namenskodierung ist dort nicht beschrieben; `PlayerInfoHypothesis` liest streng UTF-8 und verwirft ungültige
Bytes und Steuerzeichen.

Prüfung an den echten Aufnahmen (nur gelesen; Namen nur maskiert angesehen, nicht weitergegeben):

| Aufnahme | Ergebnis |
|---|---|
| `001357` (mit Login) | 33 36: 1 Rahmen, Felder passen, Kennung **11194**, Server 2309; 45 36: 1 |
| `003826`, `003512`, `002252` (ohne Login) | kein 33 36; 45 36: 8 / 11 / 18 Rahmen, alle Felder passen, keine Kennung mit zwei verschiedenen Namen. Namen gibt es nur für Spieler, die **während** der Aufnahme in Sichtweite kamen (in `003826` 2 von 17 Verursachern). |

Folgerungen: Die Kennung des eigenen Charakters wechselt je Login (11194 um 00:14, 10398 ab ca. 00:22). Der
eigene Charakter lässt sich nur erkennen, wenn die Aufnahme **vor dem Einloggen** startet. Dass die Kennung aus
33 36 wirklich die Treffer des eigenen Charakters trägt, ist **noch nicht** an einem Kampf geprüft. Eine
Klasse wird **nicht** angezeigt: Die Zuordnung Zahl → Klasse ist in der Quelle nicht nachvollziehbar
beschrieben, und die Felder dafür passten bei fremden Spielern nicht.

Neu:

- `LiveCombatDecoder` meldet zusätzlich Spielerangaben; Rahmen, die vor dem dritten Heartbeat kommen
  (z. B. 33 36 beim Login, erste Treffer), werden **zurückgehalten** und danach ausgewertet statt verworfen.
  Nachgespielt `003512`: der zuvor fehlende erste Treffer von 8899 wird jetzt mitgezählt (183.032 statt 166.410).
- `LiveCombatMeter`: Namen je Kennung (nur im Speicher), eigener Charakter automatisch aus 33 36, von Hand
  überschreibbar („(automatisch)“ schaltet zurück). Zurücksetzen behält Namen und Erkennung.
- Live-Fenster: Spalten Name und Kennung; Fußzeile sagt, ob der eigene Charakter automatisch erkannt wurde.

DPS-Angleichung an das Spiel: **noch offen.** Aus den Daten lässt sich nur sagen, dass die Spiel-Uhr über
den letzten Treffer hinaus läuft (`003826`: 349,52K ÷ 8,46K ≈ 41,3 s bei Treffern von 2,4–21,1 s; Live-Test:
321.664 ÷ 18.330 ≈ 17,5 s bei „00:17“). Wann sie startet und stoppt, ist ohne Messung am Kampfende nicht
belegbar.

| Prüfung (~01:35) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **200/200 bestanden** |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **22/22 bestanden** |
| Namen/Erkennung im Live-Fenster mit dem Spiel | **vom Nutzer geprüft (13.10.2026, 01:09, Aufnahme `aufnahme-20261013-010703.pcap`, vor dem Login gestartet):** eigener Charakter automatisch erkannt und mit ► markiert (Kennung 10275), Schaden **569.992** – Kampfanalyse des Spiels **569,99K** (00:45, DPS 12,41K). Namen anderer Spieler werden angezeigt. |

### Klasse und DPS-Zeitbasis (Stand 13.10.2026, ~01:20)

**Klasse:** Beobachtung an den Aufnahmen: Alle Klassen-Fertigkeiten eines Verursachers beginnen mit denselben
zwei Ziffern (10.000.000–29.999.999; 30xxxxxx = Gegenstände und 1010000 zählen nicht). Der Nutzer spielt
Bogenschütze; sein Charakter (10275) hat 186 von 186 Klassen-Fertigkeiten mit **14** → `SkillClassMap`:
14 = Bogenschütze (belegt). Alle anderen werden als „Klasse NN“ angezeigt, bis sie belegt sind; die Tabelle
in `SkillClassMap.cs` ist zum Ergänzen gedacht. Live-Fenster: neue Spalte „Klasse“.

**DPS-Zeitbasis:** nicht angeglichen, weil nicht eindeutig belegbar. Messung an `010703` (nur gelesen),
Charakter 10275 auf die Puppe: 187 direkte Treffer (567.602) + 8 Ticks über Zeit (2.390) = 569.992; erster
eigener Treffer 50,6 s, letzter direkter 76,4 s, letzter Tick 79,4 s (28,8 s). Das Spiel teilt durch
569.992 ÷ 12.410 ≈ **45,9 s**, läuft also ca. 17 s über den letzten Treffer hinaus; in `003826` waren es
ca. 22,6 s. Kein fester Nachlauf → die Spiel-Uhr hängt an etwas, das in den Daten (noch) nicht erkannt ist
(z. B. Ende des Kampfzustands). Das Live-Fenster weist darauf hin, dass die Kampfanalyse länger zählt.

Nachgespielt `010703` (Datei statt Adapter): 1.799 Treffer, 47 Spielerangaben, eigener Charakter erkannt
(10275, Bogenschütze), 569.992 – gleich wie live.

| Prüfung (~01:20) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **206/206 bestanden** |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **22/22 bestanden** |
| Klassenspalte im Live-Fenster mit dem Spiel | **nicht geprüft** |

### Overlay (Stand 13.10.2026, ~01:35)

`OverlayWindow` (App): ein **ganz normales WPF-Fenster** über dem Spiel – randlos, durchsichtiger Hintergrund,
immer im Vordergrund (`Topmost`), nicht aktivierbar (`ShowActivated = false`, `WS_EX_NOACTIVATE`, kein
Taskleisten-Eintrag), damit das Spiel den Fokus behält. Verschieben per Ziehen, Größe am Griff unten rechts.
„Klicks durchlassen“ schaltet `WS_EX_TRANSPARENT` (dann lässt es sich nicht verschieben). **Kein** Einhaken in
die Grafik des Spiels (kein DirectX-/Overlay-Hook), kein Lesen/Ändern von Spielspeicher, keine Codeinjektion,
keine Eingaben ans Spiel. Sichtbar nur im **Fenstermodus oder randlosen Vollbild**, nicht im exklusiven Vollbild.

Inhalt (`OverlayFormat`, Core, getestet): Kopfzeile (Kampf/beendet, Dauer, Gesamtschaden, DPS) und je Spieler
Name (sonst Kennung), Klasse, Schaden, DPS, Anteil in Kurzform (12,4K / 1,88M); höchstens 8 Zeilen, der eigene
Charakter immer sichtbar und gold hervorgehoben. Hauptfenster: „Overlay ein/aus“, „Zurücksetzen“,
„Klicks durchlassen“, Schrift –/+, Deckkraft.

Einstellungen (`OverlaySettingsStore`): `%LOCALAPPDATA%\Aion2DpsMeter\overlay.json` (Position, Größe,
Deckkraft, Schrift, Klicks durchlassen). Die Datei wird nur angelegt, wenn sie fehlt, und nur ersetzt, wenn sie
das Kennzeichen des Programms trägt; fremde oder kaputte Dateien bleiben unverändert (Hinweis im Fenster).

| Prüfung (~01:35) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Warnungen, 0 Fehler |
| `Aion2DpsMeter.Core.Tests` | **218/218 bestanden** (12 neu: Kurzzahlen, Zeilenauswahl, Einstellungsdatei inkl. Nicht-Überschreiben) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **22/22 bestanden** |
| Overlay mit dem Spiel | **vom Nutzer geprüft (13.10.2026, ~01:30, 3 Screenshots):** Overlay erscheint über dem Spiel mit Klasse, Schaden, DPS, Anteil. Rückmeldung: eigener Name fehlte (Aufnahme nach dem Login gestartet), Pets erscheinen als eigene Zeilen. |

### Klassen, Pets, gemerkter eigener Charakter (Stand 13.10.2026, ~08:45)

- **Klassen** (Angaben des Nutzers): 11 Kantor, 13 Assassine, 14 Bogenschütze, 15 Magier, 16 Beschwörer,
  17 Kleriker; **12 Templer** (Nutzer auf Rückfrage, 13.10.2026, 09:26 – „7“ war ein Versehen, Klassen-Fertigkeiten
  mit 7 vorne gibt es in keiner Aufnahme). 18 bleibt „Klasse 18“. Core.Tests danach 230/230; App-Neubau wartet,
  bis das Fenster geschlossen ist.
- **Charakterwechsel (13.10.2026, ~09:30):** Nutzer stoppte die Aufnahme, wechselte vom Bogenschützen (erkannt,
  Kennung 9102) auf den Templer und startete neu → Templer nicht erkannt, gemerkte 9102 passte nicht. Prüfung aller
  15 Aufnahmen (nur gelesen): 33 36 kommt nur je **einmal beim Login** (`001357`, `010703`); ob ein Teleport es
  erneut auslöst (wie es von anderen Metern berichtet wird), ist **nicht belegt** – keine Aufnahme mit bekanntem
  Teleport. Änderung: Ein nur gemerkter Charakter gilt **nicht** mehr als „ich“, sondern wird als „vermutlich ich“
  mit „?“ (Overlay: khaki) markiert, bis er von Hand bestätigt oder per 33 36 erkannt wird. Hinweistext im
  Live-Fenster entsprechend. Core.Tests 230/230, Npcap.Tests 23/23, App-Prüfbuild 0 Fehler; voller Build wartet
  auf geschlossenes Fenster (PID 23984).
- **Teleport-Test (`aufnahme-20261013-093400.pcap`, ohne Login, mitten in der Verbindung, nur gelesen):** 33 36 kommt
  bei **54,0 s** (in einem LZ4-Bündel) mit Kennung **3917** – das ist der Bogenschütze, der 96,9 % des Schadens macht
  (457.258); nachgespielt wird er automatisch als „ich“ erkannt und benannt. Zeitgleich eine Welle von
  45 36 (je 5 s sonst 1–4, ab 50 s: 22, 17, 12, 9) – passt zu einem Teleport/Gebietswechsel um 54 s; der genaue
  Teleport-Zeitpunkt ist nicht vom Nutzer bestätigt. Folgerung (eine Aufnahme): **Teleportieren liefert den
  eigenen Charakter und viele Namen erneut.** Hinweis im Live-Fenster entsprechend geändert.
- **Bestätigt (`aufnahme-20261013-093659.pcap`, Nutzer 09:38):** „erst ein ? und nach dem Port ein Pfeil im Overlay“.
  In den Daten: 33 36 bei **17,5 s** (Bündel), Kennung 3917, zeitgleich Welle von 45 36 (15–30 s: 26, 11, 15);
  nachgespielt 3917 = Bogenschütze (ich), 277.024. Damit an zwei Aufnahmen und im Live-Test belegt:
  **Teleport liefert eigenen Charakter und Namen erneut.**

### Aufgeräumte Oberfläche, Ein-Klick-Start und Endnutzer-EXE (Stand 13.10.2026, ~10:00)

- **Klasse 18 = Gladiator** (Nutzer, 09:39). Damit alle acht Klassen belegt: 11 Kantor, 12 Templer, 13 Assassine,
  14 Bogenschütze, 15 Magier, 16 Beschwörer, 17 Kleriker, 18 Gladiator.
- **Hauptfenster neu gegliedert:** oben „▶ Starten“ / „■ Stoppen“ mit Status; Bereich „Anzeige“ (Overlay ein/aus,
  Tabelle, Zurücksetzen, Klicks durchlassen, Schrift, Deckkraft); alles Technische (Netzwerkkarte, Programm,
  Speichern als .pcap, Auswertungen, Paketzähler, Npcap-Version) im zugeklappten Bereich **„Erweitert“**.
- **Ein-Klick-Start („▶ Starten“):** prüft Npcap (fehlt es: Meldung mit Link https://npcap.com/#download – Npcap
  wird aus Lizenzgründen **nicht** mitgeliefert), wählt die vorgeschlagene Netzwerkkarte und das Spielprogramm
  (`ProcessConnectionFilter.FindGameProcess`: Name „AION2“ bzw. enthält „aion2“/„aion 2“, Launcher/Updater/
  Fehlerbericht ausgenommen; sonst Meldung „AION 2 wurde nicht gefunden“ bzw. Handauswahl unter „Erweitert“),
  startet die Aufnahme nur für dieses Programm, **ohne** .pcap-Speichern, mit Live-Auswertung, und blendet das
  Overlay ein.
- **Overlay:** Anteil-Balken je Zeile in gedämpfter Klassenfarbe hinter der Zeile, Spalten bündig.
- **Endnutzer-EXE:** `dotnet publish src/Aion2DpsMeter.App -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/endnutzer-win-x64`
  → **eine Datei** `Aion2DpsMeter.App.exe` (≈ 134 MB, .NET-Laufzeit enthalten, kein .NET nötig). Ordner `artifacts/`
  ist in `.gitignore`. Voraussetzung beim Endnutzer: Windows 10/11 x64 und **installiertes Npcap**. Die EXE ist
  **nicht signiert** – Windows SmartScreen bzw. Smart App Control können sie bei anderen Nutzern warnen oder
  blockieren; es wird keine Umgehung eingebaut. Die EXE wurde von mir **nicht gestartet**.

| Prüfung (~10:00) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat | 0 Warnungen, 0 Fehler |
| `dotnet publish` (Release, win-x64, eine Datei) | erfolgreich, `artifacts/endnutzer-win-x64/Aion2DpsMeter.App.exe` |
| `Aion2DpsMeter.Core.Tests` | **233/233 bestanden** (neu: Spielprogramm-Auswahl, Gladiator) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **23/23 bestanden** |
| Neue Oberfläche, Ein-Klick-Start, EXE | **nicht geprüft** (Fenster des Nutzers offen, PID 14388; nicht angefasst) |

### Overlay im Stil des Vorbilds, Spieler-Details (Stand 13.10.2026, ~09:50)

Nach Screenshots eines anderen Meters (nur als Design-Vorbild; kein fremder Code, keine fremden Grafiken):

- **Overlay neu:** dunkles, abgerundetes Panel mit schmalem Rahmen; Kopfzeile mit Statuspunkt (grau inaktiv / grün
  Kampf), Titel und Kampfzeit „m:ss“, rechts Symbolknöpfe (Kopieren in die Zwischenablage, Zurücksetzen, Kompakt,
  Einklappen, Ausblenden); leer: „Warte auf Kampf“; Spielerzeilen als Balken (eigene Zeile violett mit „ICH“,
  sonst Klassenfarbe), links ein einfaches eigenes Klassenzeichen (Kreis mit zwei Buchstaben), Name, Schaden,
  rechts DPS fett und Anteil „0,0 %“; Fußzeile mit eigenem Namen bzw. „Teleportiere dich irgendwohin, um erkannt
  zu werden“ und Schloss für „Klicks durchlassen“. **Nicht gebaut:** Ping-Anzeige (aus den passiv gelesenen Daten
  nicht zuverlässig ableitbar), Update-Banner (kein Netzwerkzugriff nach außen), Zielname im Titel (Ziele sind
  nur Nummern; NPC-Namen nicht belegt).
- **Spieler-Details** (Klick auf eine Overlay-Zeile oder „Details…“ in der Tabelle): Name/Klasse, Gesamtschaden,
  „m:ss · DPS · Treffer · Krit %“, Ziel-Auswahl („Alle Ziele“ bzw. je Zielnummer), Tabelle pro Fähigkeit (Nummer aus
  04 38/05 38): Schaden, %, Treffer, Krit, Max, Ø mit Balken; „über Zeit“ und „Pet“ als eigene Zeilen. Eigene Namen
  für Fähigkeiten per Doppelklick, gespeichert in `Dokumente\Aion2DpsMeter\skills.json` (nur eigene Datei wird
  ersetzt). Grundlage: `LiveCombatMeter.Detail` (Treffer je Kampf im Speicher).
- **Weitere Felder (Parade, Rücken/Front, Perfekt, Doppelt, Mehrfach): nicht angezeigt, weil nicht belegt.**
  Kandidaten (nur gelesen, `093400`, Bogenschütze, 167 Treffer): Modifikatorbyte 0 = 130, 4 = 23, 8 = 10, 12 = 4
  (laut Community 0x04 „Perfekt“, 0x08 „Doppelt“); Richtungsbyte immer 2 (laut Community „Front“); Zusatzwerte
  0 = 95, 4 = 65, 1 = 5, 2 = 2. Bräuchte einen Abgleich mit dem Kampflog des Spiels.
- Neue EXE in neuem Ordner `artifacts/endnutzer-win-x64-v2/Aion2DpsMeter.App.exe` (≈ 134 MB; der vorige Ordner
  bleibt unverändert). Nicht von mir gestartet.

| Prüfung (~09:50) | Ergebnis |
|---|---|
| Build Core, Tests; App-Prüfbuild separat; `dotnet publish` | 0 Warnungen, 0 Fehler; Veröffentlichung erfolgreich |
| `Aion2DpsMeter.Core.Tests` | **241/241 bestanden** (neu: Aufschlüsselung, Ziel-Filter, Pet-/DoT-Zeilen, Fähigkeitsnamen, Kopiertext, Kampfzeit) |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **23/23 bestanden** |
| Neues Overlay, Details, EXE mit dem Spiel | **nicht geprüft** |

### Community-Spalten in den Details (Stand 13.10.2026, ~09:52)

Ein anderes Meter (Screenshot des Nutzers) zeigt Krit, Parade, Rücken, Front, Perfekt, Doppelt und Multi%. Dafür
werden jetzt die in 04 38 beschriebenen Felder durchgereicht (`CombatHit.Modifier/Direction/ExtraHits`) und gezählt
(`CommunityFlags`, Masken laut Community: Modifikator 0x02 Parade, 0x04 Perfekt, 0x08 Doppelt; Richtung 0x01
Rücken, 0x02 Front; Multi = Treffer mit Zusatzwerten). Quoten nur über Treffer, bei denen das Feld vorhanden ist
(Layout 4 hat keins). Im Detailfenster abschaltbar: „Community-Spalten … – noch nicht abgeglichen“ (Standard an),
Spalten mit * gekennzeichnet. **Nicht abgeglichen** – zum Abgleich beide Meter gleichzeitig an der Puppe laufen lassen.

Nachgespielt `093400` (Bogenschütze, 167 Treffer, Modifikator bei allen vorhanden): Parade 0,0 %, Perfekt 16,2 %,
Doppelt 8,4 %, Rücken 0,0 %, Front 100,0 %, Multi 43,1 %. (Screenshot des anderen Meters, anderer Kampf: Perfekt
16,9 %, Doppelt 5,2 %, Front 100 % – ähnliche Größenordnung, kein Beweis.)

EXE: `artifacts/endnutzer-win-x64-v3/Aion2DpsMeter.App.exe` (≈ 134 MB, neuer Ordner). Core.Tests **242/242**,
Npcap.Tests **23/23**, App-Prüfbuild und Veröffentlichung 0 Fehler. Nicht gestartet.

### Laufende Kampfzeit, Overlay-Höhe (Stand 13.10.2026, ~10:00)

- **Vom Nutzer geprüft (09:53, Screenshot):** v3-Overlay im Spiel neben dem anderen Meter: eigener Charakter (ICH) 609,2K,
  21,8K DPS; zweiter Spieler „Etheria“ (Bogenschütze, „Pet 31 %“), den das andere Meter nicht zeigt.
- **DPS-Zeitbasis geändert** (Wunsch des Nutzers, wie beim anderen Meter): Solange der Kampf läuft (letzter Treffer
  ≤ 10 s her), zählt die Zeit bis „jetzt“ – die DPS fallen, wenn niemand trifft. Danach gilt der Kampf als beendet,
  die Zeit wird auf den letzten Treffer eingefroren. Begründung: Ein fester Nachlauf wie bei der Spieluhr (gemessen
  ca. 17 s bzw. 22,6 s) ist nicht konstant, also nicht belegbar. Die Programmauswahl verzögert Treffer um 2 s; diese
  `Lag` wird von „jetzt“ abgezogen. Start je Spieler bleibt dessen erster Treffer (wie vereinbart, damit später
  Hinzugekommene nicht benachteiligt werden). Detailfenster mit derselben Basis.
- **Overlay-Höhe** folgt der Zeilenzahl (`SizeToContent.Height`); die Breite bleibt am Griff einstellbar.
- **„Etheria“:** keine gespeicherte Aufnahme dieses Kampfs (Ein-Klick-Start speichert nicht) – nicht nachprüfbar.
  Plausibel: ein anderer Spieler in der Nähe, der dieselbe Puppe trifft (das andere Meter filtert evtl. auf die
  eigene Gruppe oder das eigene Ziel). „Pet 31 %“ beim Bogenschützen hieße, dass Beschwörungen/Fallen dieses
  Spielers ihm per 41 36 zugeordnet wurden – bei Bogenschützen bisher nicht beobachtet, also offen.
- EXE: `artifacts/endnutzer-win-x64-v4/Aion2DpsMeter.App.exe` (neuer Ordner). Core.Tests **244/244**,
  Npcap.Tests **23/23**, Build und Veröffentlichung 0 Fehler. Nicht gestartet.

### Abgleich mit dem anderen Meter, gleicher Kampf (Stand 13.10.2026, ~10:05)

Screenshot des Nutzers (09:56), v4 neben dem anderen Meter: **Schaden eigener Charakter 203,9K in beiden – exakt gleich.**
Anderes Meter, Details: „0:11 · 17.3K DPS · 58 Zauber · 109 Treffer“, Krit 46,2 %, Parade 0 %, Rücken 0 %, Front
100 %, Perfekt 15,4 %, Doppelt 9,0 %. Unser Overlay im selben Moment: 0:22, 11,5K DPS (Zeit seit eigenem ersten
Treffer bis jetzt). Unser Detailfenster dieses Kampfs liegt noch nicht vor – Krit/Perfekt/Doppelt daher noch
nicht verglichen; keine Aufnahme dieses Kampfs.

Neu im Detailfenster (v5): **aktive Zeit** und **aktive DPS** (Summe der Abstände zwischen eigenen Treffern, Pausen
ab 5 s zählen nicht – eigene Annahme, Ziel: Vergleich mit „0:11“ des anderen Meters) sowie „Zauber“ (Datensätze) und
„Treffer inkl. Mehrfach*“ (Zauber + Zusatzwerte, Kandidat). Prüfung an `093400` (nur gelesen): 167 Zauber, 436
„Treffer inkl. Mehrfach“ (Faktor 2,6) – beim anderen Meter 58 zu 109 (Faktor 1,9), anderer Kampf. **Passt nicht
gut** → die Zusatzwerte sind wohl nicht einfach weitere Treffer; bleibt gekennzeichnet und offen. Fähigkeitsnamen
wurden **nicht** eingetragen (ohne unser Detailfenster desselben Kampfs keine sichere Zuordnung über Summen).

EXE: `artifacts/endnutzer-win-x64-v5/Aion2DpsMeter.App.exe`. Core.Tests **245/245**, Npcap.Tests **23/23**,
Build und Veröffentlichung 0 Fehler. Nicht gestartet.

### Nur Overlay für Endnutzer, Einstellungen im Overlay (v6, Stand 13.10.2026, ~10:15)

Wunsch des Nutzers: „Der Endnutzer soll das Fenster gar nicht haben – EXE öffnen, direkt das Overlay.“

- **Start ohne Hauptfenster:** `App.OnStartup` startet den Overlay-Modus (`MainWindow.StartOverlayMode`, Fenster
  bleibt unsichtbar): Overlay einblenden, dann automatisch Npcap prüfen, Netzwerkkarte und AION 2 wählen und die
  Live-Aufnahme starten (ohne Speichern). Läuft das Spiel nicht, steht im Overlay „Warte auf AION 2 …“ und es
  wird alle 5 s erneut versucht; fehlt Npcap, steht dort ein Hinweis auf npcap.com. Mit dem Startargument
  `--fenster` öffnet sich wie bisher das Entwickler-/Hauptfenster.
- **Overlay:** Zahnrad öffnet die Einstellungen; ✕ beendet im Overlay-Modus das Programm sauber (Aufnahme
  stoppen, Einstellungen speichern).
- **Einstellungsfenster** (`OverlaySettingsWindow`, dunkel, links Bereiche): *Allgemein* (passiv, Hinweis auf
  Kontosperre-Risiko, Fenstermodus-Hinweis, Overlay-Position zurücksetzen, Beenden), *Kampf*
  (Inaktivitäts-Timeout 3–120 s, Standard 10; Kampf zurücksetzen; Zielmodus, „Nur Gruppe“, „Nur Bosse/Puppen“
  ausgegraut „noch nicht verfügbar“ – in den Aufnahmen nicht belegt), *Anzeige* (Deckkraft, Schriftgröße,
  höchstens Spieler, unter 1 % ausblenden, Klicks durchlassen), *Tastenbelegung*, *Debug* (Tabelle,
  Entwickler-Fenster für .pcap-Aufnahme und Auswertungen). Kein Konto, kein Upload, keine Online-Verbindung.
- **Tastenkürzel** (`HotkeyManager`, Windows-Funktion RegisterHotKey – meldet nur diesem Programm, sendet nichts
  ans Spiel, kein Hook): Strg+Umschalt+K Klicks durchlassen an/aus (Weg zurück), Strg+Umschalt+O Overlay ein/aus,
  Strg+Umschalt+R Kampf zurücksetzen, Strg+Umschalt+E Einstellungen. Belegte Kürzel werden in den Einstellungen
  als „ohne Wirkung“ angezeigt. Ein Taskleisten-Symbol wurde **nicht** gebaut (bräuchte Windows Forms).
- Behoben: Zwischenstand-Baufehler CS0246 „TrayIcon“ (vom Nutzer bei `dotnet run` gesehen).

| Prüfung (~10:15) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** (`dotnet run` baut wieder) |
| Tests | Core **247/247**, App **4/4**, Npcap **23/23** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v6/Aion2DpsMeter.App.exe` |
| Overlay-Modus mit dem Spiel | **nicht geprüft** (nichts gestartet) |

### Kampfverlauf „Kämpfe“ (v7, Stand 13.10.2026, ~10:25)

- **Automatisch gespeichert:** Jeder beendete Kampf (Kampfende = Inaktivitäts-Timeout) – auch einer, den ein neuer
  Kampf verdrängt, bevor er abgeholt wurde, und beim Beenden der noch laufende – kommt als eigene JSON-Datei nach
  `Dokumente\Aion2DpsMeter\Kaempfe` (`kampf-JJJJMMTT-hhmmss.json`; belegte Namen bekommen „-2“ usw., **nie
  überschreiben**). Inhalt: Zusammenfassung je Spieler, Ziel und Fähigkeit (Schaden, Treffer, Krit, Max, erster
  Treffer), Name/Klasse, eigener Charakter – **keine Rohpakete**. Namen anderer Spieler nur lokal. Mit
  „Zurücksetzen“ gelöschte Kämpfe werden nicht gespeichert.
- **Fenster „Kämpfe“** (Knopf ☷ im Overlay oder Einstellungen → Kampf → Kämpfe): Suche nach Bezeichnung oder Spieler,
  Karten mit Name („Kampf TT.MM.JJJJ hh:mm“), eigenem Charakter, Datum/Uhrzeit, Dauer, Gesamtschaden, Spielerzahl;
  ✎ umbenennen, 🗑 löschen **nach Rückfrage** (nur eigene Dateien). Klick → Spieler des Kampfs → Klick → Details
  mit Zielauswahl (wie live, ohne Community-Spalten, da diese nicht gespeichert werden). Kein Cloud-Reiter.
- Kern: `FightHistory.cs` (`FightRecord`, `FightHistoryStore`), `LiveCombatMeter.DrainFinishedFights/ExportCurrentFight`.

| Prüfung (~10:25) | Ergebnis |
|---|---|
| Voller `dotnet build` | 0 Warnungen, 0 Fehler |
| Tests | Core **251/251** (neu: Export genau einmal, verdrängter Kampf, Zurückgesetztes nicht, Speichern ohne Überschreiben, Umbenennen/Löschen nur eigener Dateien, Details/Suche aus gespeicherten Kämpfen), App **4/4**, Npcap **23/23** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v7/Aion2DpsMeter.App.exe` |
| Verlauf mit dem Spiel | **nicht geprüft** (nichts gestartet) |

### Klassen-Symbole (v8, Stand 13.10.2026, ~10:35)

- **Eigene Vektor-Symbole** (`ClassIcons`, selbst gezeichnet, 24×24 auf Kreis in Klassenfarbe): Gladiator Schwert,
  Templer Schild, Assassine Dolch, Bogenschütze Bogen mit Pfeil, Magier Stern, Beschwörer Spirale, Kleriker Kreuz,
  Kantor Streitkolben, Pet Pfote, unbekannt „?“. Ersetzen die Buchstabenkreise in Overlay (Zeilen, Fußzeile),
  Detailfenster und Spielerliste gespeicherter Kämpfe.
- **Optionaler Icon-Ordner** `Dokumente\Aion2DpsMeter\Icons`: Legt der Nutzer dort eigene PNG-Dateien ab (kantor,
  templer, assassine, bogenschuetze, magier, beschwoerer, kleriker, gladiator, pet, unbekannt .png), werden sie statt
  der Vektor-Symbole genutzt; fehlt eine Datei oder ist sie ungültig, gilt das eigene Symbol. Das Programm legt dort
  nichts an außer – über „Einstellungen → Anzeige → Icon-Ordner öffnen“ – den leeren Ordner.
- **Offizielle Grafiken:** Die Klassen-Icons von AION 2 sind Eigentum von NCSoft und werden **nicht** mitgeliefert.
  Auf Wunsch des Nutzers wurde die eigene Spielinstallation (`D:\SteamLibrary\steamapps\common\AION2`) **nur lesend**
  nach losen Bilddateien durchsucht: gefunden wurde nur `Aion2\Content\Splash\Splash.bmp`; alle übrigen Grafiken
  liegen in gepackten Archiven (322 `.pak`, 267 `.ucas`/`.utoc`). Diese werden **nicht** entpackt – daher wurde nichts
  kopiert. Wer offizielle Symbole will, kann sie selbst (z. B. per Screenshot) als PNG in den Icon-Ordner legen.

| Prüfung (~10:35) | Ergebnis |
|---|---|
| Voller `dotnet build` | 0 Warnungen, 0 Fehler |
| Tests | Core **251/251**, App **5/5** (neu: alle Klassensymbole erzeugbar), Npcap **23/23** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v8/Aion2DpsMeter.App.exe` |
| Symbole mit dem Spiel | **nicht geprüft** (nichts gestartet) |
- **Pets/Beschwörungen:** `SummonOwnerHypothesis` liest den Besitzer aus Typ 41 36 (Community-Beschreibung,
  selbst geschrieben): Elternverweis (06 + u32 Besitzer + Legion + 0 + Server + Name) oder fester Anker
  `80 75 D5 2A BB 03 00 00` + Varint. Prüfung an 5 Aufnahmen (nur gelesen): in allen **34** Fällen, in denen ein
  so zugeordnetes Pet Schaden macht, hat der Besitzer dieselbe Klassen-Nummer (16 per Anker, 17 per
  Elternverweis), **0 Widersprüche**; die Schadenskennung ist die Rohkennung. `LiveCombatMeter` rechnet
  Pet-Schaden dem Besitzer zu („davon Pet“), auch nachträglich, wenn Treffer vor der Besitzer-Nachricht kamen;
  bei widersprechender Klasse nicht. Pets ohne erkannten Besitzer (z. B. vor Aufnahmebeginn beschworen)
  werden als „Pet?“ gekennzeichnet (Fertigkeiten 100.000–999.999 und kein Name – Beobachtung an den Aufnahmen).
- **Namen:** Sie kommen nur mit 33 36 (Login) bzw. 45 36 (Spieler kommt in Sichtweite); eine zweite Quelle für
  Spielernamen wurde nicht gefunden. Unbekannte Namen: „(Name unbekannt) #Kennung“.
- **Eigener Charakter gemerkt** (`OwnCharacterStore`): `%LOCALAPPDATA%\Aion2DpsMeter\eigener-charakter.json`,
  nur Kennung und Name des eigenen Charakters, bis 12 h; verworfen, sobald ein neues 33 36 kommt oder ein
  anderer Spieler mit dieser Kennung gemeldet wird. Datei nur ersetzt, wenn sie vom Programm stammt.
- **Overlay:** Zeilen unter 1 % ausgeblendet (in „+ n weitere“, eigener Charakter bleibt immer), Spalte
  „Pet x %“. Live-Fenster: Spalte „davon Pet“.

Nachgespielt (nur gelesen): `010703` Beschwörer 15042 **1.520.561**, davon Pet 665.032 (vorher nur 855.529);
`013014` Beschwörer 13569 **318.596**, davon Pet 122.584 (inkl. nachträglich zugeordnetem Pet 49865 mit
12.136); Kleriker 3172 mit Pet 1.619.

| Prüfung (~08:45) | Ergebnis |
|---|---|
| Voller `dotnet build` (Fenster war geschlossen) | **0 Warnungen, 0 Fehler** |
| `Aion2DpsMeter.Core.Tests` | **230/230 bestanden** |
| `Aion2DpsMeter.Capture.Npcap.Tests` | **23/23 bestanden** |
| `Aion2DpsMeter.App.Tests` | **4/4 bestanden** (aktueller Stand) |
| Pets, „(Name unbekannt)“, gemerkter Charakter im Spiel | **nicht geprüft** |

### Event-Alarme und Notausgang „Klicks durchlassen“ (v9, Stand 13.10.2026, ~10:30)

- **Zeitplan** (`Core/Events/GameEvents.cs`): Raumzeit-Riss (alle 3 h ab 02:00), Dimensionale Invasion (stündlich
  :30), Shugo-Fest (stündlich :00), Arena der Taktik (11:00, 19:00), Tages-Reset (09:00), Wochen-Reset (Mi 09:00),
  Artefakt-Belagerung (Mo/Do/Sa 21:00). Grundlage ist die vom Nutzer gezeigte EU-Übersicht; es gibt **keinen
  Internetabruf**. Gerechnet wird in der Zeitzone „W. Europe Standard Time“ über `TimeZoneInfo`, also mit
  Sommerzeit: Uhrzeiten, die es beim Umstellen nicht gibt, entfallen; doppelte zählen einmal.
- **Datei** `Dokumente\Aion2DpsMeter\events.json` (Kennung `Aion2DpsMeter-Events`): Sie wird nur angelegt, wenn sie
  fehlt, und ist danach von Hand änderbar. Das Programm überschreibt sie nie. Bei fremden oder kaputten Dateien gilt
  der Standard, und es erscheint ein Hinweis in den Einstellungen.
- **Einstellungen → Event-Alarme**:
  - Lautstärke
  - Ton (Glocke, Piep, Gong; alle selbst erzeugte Sinustöne) mit Test-Knopf
  - „Nächstes Event im Overlay“
  - je Event eine Karte mit nächstem Termin und Restzeit, Schalter „Erinnern“ und Vorlauf 1/5/10/15 Min.
  - Gespeichert wird in `overlay.json`.
- **Alarm** = Ton + 20 s eine goldene Zeile im Overlay, je Termin genau einmal. Sonst zeigt die Zeile
  „Nächstes: … in …“. Weltbosse sind nicht enthalten.
- **Notausgang für „Klicks durchlassen“**:
  1. Beim Einschalten erscheint 8 s lang im Overlay „🔒 Klicks durchgelassen – Strg+Umschalt+K zum Ausschalten“,
     dazu eine Sprechblase am Tray-Symbol. Ist Strg+Umschalt+K belegt, nennt der Hinweis das Tray-Symbol.
  2. Der Zustand wird **nicht gespeichert**, nach jedem Start ist er aus. Das gilt auch, wenn in der Datei `true`
     steht.
  3. Neues **Tray-Symbol** (selbst gezeichnet): Doppelklick schaltet „Klicks durchlassen“ aus. Das Menü enthält
     Klicks durchlassen, Overlay ein/aus, Einstellungen und Beenden. Es braucht kein Tastenkürzel.
- **Tastenkürzel ohne Fokus**: `RegisterHotKey` ist systemweit. Windows meldet die Kombination an das
  unsichtbare Nachrichtenfenster des Programms, auch wenn das Spiel im Vordergrund ist. Windows selbst belegt
  Strg+Umschalt+K/O/R/E nicht global. Ob das Spiel oder sein Schutzprogramm die Tasten vorher abfängt, ist **nicht
  geprüft**; dafür gibt es das Tray-Symbol. Belegte Kürzel stehen in Einstellungen → Tastenbelegung.
- Das Tray-Symbol braucht Windows Forms (`UseWindowsForms`). Die EXE wird dadurch größer (~174 MB statt ~141 MB).

| Prüfung (~10:30) | Ergebnis |
|---|---|
| Voller `dotnet build` (eigener Ausgabeordner, das laufende Fenster blieb offen) | **0 Warnungen, 0 Fehler** |
| Tests | Core **270/270**, App **8/8**, Npcap **23/23** |
| Neu in Core | Events: Termine, Mittwoch/Wochenende, Sommerzeit Ende/Anfang (25.10./29.03.2026), Erinnerung genau einmal, Datei-Vorlage/fremd/kaputt nie überschrieben, Normalisierung; „Klicks durchlassen“ nie gespeichert |
| Neu in App | `HotkeyManager`: Anmeldung echt bei Windows, Weiterleitung per selbst geposteter Meldung (kein echter Tastendruck); doppelte Anmeldung wird als belegt gemeldet |
| Strg+Umschalt+K/O/R/E auf diesem PC | Waren während des Tests belegt, und zwar vom laufenden DPS-Meter selbst (Prozess 27564). Ein Konflikt mit anderen Programmen ist damit **nicht** feststellbar. |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v9/Aion2DpsMeter.App.exe` |
| Alarmton, Tray, Hotkeys im Spiel | **nicht geprüft** (nichts gestartet) |

### Variante „persoenlich“ mit eingebetteten Klassenbildern (Stand 13.10.2026, ~10:40)

> **Enthält NCSoft-Grafiken – nicht weitergeben.** Nur für den eigenen Gebrauch des Besitzers. Zum Weitergeben
> immer die Endnutzer-EXE verwenden; sie enthält keine fremden Grafiken.

- Bauen mit `.\publish-persoenlich.ps1`. Ausgabe ist `artifacts\persoenlich-win-x64`; ein vorhandener Ordner wird
  nicht überschrieben, mit `-Output` lässt sich ein anderer Name angeben.
- Das Skript setzt `-p:EmbedPersonalIcons=true`. Dann bettet das App-Projekt die 8 PNGs aus
  `Dokumente\Aion2DpsMeter\Icons` beim Bauen als Ressource `PersonalIcons/<klasse>.png` ein. Fehlt eine Datei,
  bricht der Bau mit „Klassenbild fehlt: …“ ab.
- Die Bilder werden **nicht** ins Projekt kopiert. Zwischendateien liegen in einem eigenen Ordner unter `%TEMP%`,
  der danach gelöscht wird. `artifacts/` steht in `.gitignore`.
- Neben der EXE liegt `NICHT-WEITERGEBEN.txt`.
- Reihenfolge der Symbole:
  1. PNG im lokalen Icon-Ordner
  2. eingebettetes Bild
  3. eigenes Vektor-Symbol
- Die Bilder hat der Besitzer selbst aus einem Screenshot ausgeschnitten. Es wurden keine Spielarchive entpackt.

| Prüfung (~10:40) | Ergebnis |
|---|---|
| Bau mit leerem Icon-Ordner | bricht wie gewollt ab: „Variante persoenlich: Klassenbild fehlt: …“ |
| `publish-persoenlich.ps1` | `artifacts/persoenlich-win-x64/Aion2DpsMeter.App.exe` + `NICHT-WEITERGEBEN.txt` |
| Ressourcennamen in der EXE (Bytesuche) | persoenlich: **8** `PersonalIcons/*.png`; Endnutzer-v9: **0** |
| App-Tests | **9/9** (neu: Standard-Build ohne eingebettete Bilder) |
| Bilder im Overlay | **nicht geprüft** (nichts gestartet) |

### Einheitliche Kurzschreibweise, späte Namen (v10, Stand 13.10.2026, ~10:50)

- **Zahlen:** Ab 1.000 wird überall mit K gekürzt, unter 10K mit zwei Nachkommastellen. Beispiele: 1.298 → „1,30K“,
  14.512 → „14,5K“, 1.880.292 → „1,88M“. Die Grenzen sind so gesetzt, dass Runden nie „10,00K“ oder „1000,0K“
  ergibt (9.999 → „10,0K“, 999.960 → „1,00M“).
- **„(Name unbekannt) #…“:** Namen gibt es nur in 33 36 (Login/Teleport) und 45 36 (Spieler kommt in Sichtweite).
  Der Zähler sucht Namen bei jeder Anzeige neu heraus. Kommt der Name also später, steht er rückwirkend für den
  ganzen Kampf da; das ist schon so gebaut, neuer Test `LateNameIsAppliedRetroactively`. Kommt im Kampf keine solche
  Nachricht, gibt es keinen weiteren bekannten Weg. Von dem gemeldeten Kampf gibt es keine Aufnahme, also wurde
  dazu nichts nachgeprüft.
- Neue Ausgaben: `artifacts/endnutzer-win-x64-v10` und `artifacts/persoenlich-win-x64-v2`. Beide enthalten alles aus
  v9.

| Prüfung (~10:50) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **279/279**, App **9/9**, Npcap **23/23** |
| Bilder in der EXE (Bytesuche) | persoenlich-v2: **8**, Endnutzer-v10: **0** |
| Anzeige im Spiel | **nicht geprüft** (nichts gestartet) |

### Detailfenster im Overlay-Stil, „DPS (aktiv)“, Fähigkeitsnamen (v11, Stand 13.10.2026, ~11:10)

- **Detailfenster** im dunklen Overlay-Stil:
  - Kopf-Karte mit Klassen-Icon (40 px), Name, Klasse und Gesamtschaden in K-Schreibweise.
  - Kacheln für DPS, DPS (aktiv), Kampfzeit, aktiv, Zauber, Treffer* und Krit.
  - Die Tabelle steht auf einer abgerundeten Karte; die Balken haben die Klassenfarbe.
  - Schaden, Max und Ø werden als K-Zahlen gezeigt, die genauen Werte stehen im Tooltip.
- **„DPS (aktiv)“** erscheint erst ab **3 s** aktiver Zeit (`ActorDetail.MinActiveTime`, eigene Annahme), sonst steht
  dort „–“. Liegt die aktive Zeit unter 1 s, wird „&lt;0:01“ statt „0:00“ angezeigt. Anlass war ein Screenshot mit
  „aktiv 0:00 · 78,9K DPS (aktiv)“: Ein Kantor hatte alle Treffer fast gleichzeitig gelandet.
- **Fähigkeitsnamen**, in dieser Reihenfolge:
  1. eigener Name per Doppelklick (`Dokumente\Aion2DpsMeter\skills.json`, der Pfad steht im Fenster)
  2. optional die **lokale Community-Liste** `Dokumente\Aion2DpsMeter\skills-community.json`, angezeigt mit „*“
     (nicht abgeglichen)
  3. „#Nummer“
  - Gibt es eine Nummer in der Liste nicht, gilt die Grundnummer (letzte vier Stellen 0).
  - Das Programm liest die Liste nur, schreibt sie nie und liefert sie **nicht** mit; sie steckt weder in der EXE
    noch im Repository.
- **Herkunft der Liste:** `data/skills/de.json` aus [cyberbadger6969/aion2-dps-meter](https://github.com/cyberbadger6969/aion2-dps-meter)
  (GPL-3.0), nur gelesen und **kein Code übernommen**. Laut deren `data/NOTICE.md` stammt sie aus
  taengu/A2Tools-DPS-Meter (GPL-3.0) und wurde dort aus Spieldaten (© NCSOFT) gewonnen. Wir selbst haben keine
  Spieldaten ausgelesen. Die Datei wurde einmalig für den Besitzer lokal abgelegt, nur weil sie dort fehlte.
- **Abgleich an eigenen Aufnahmen** (nur gelesen): Der Vergleichsmeter-Kampf mit 203,9K ist **nicht** aufgenommen,
  eine Zuordnung über die Summen war also nicht möglich. In den Aufnahmen `010703`, `093400` und `093659` gilt aber
  für die Bogenschützen-Fähigkeiten des Besitzers:
  - Alle **13** Namen aus dessen Vergleichsliste kommen vor: 9 über die genaue Nummer, 4 über die Grundnummer
    (z. B. 14800007 → Hunter's Soul).
  - Die Reihenfolge passt: Tempest Shot ist in allen drei Aufnahmen die stärkste Fähigkeit, Hunter's Soul die
    zweitstärkste, wie beim Vergleichsmeter.
  - Weiterhin offen: 14380007 (Liste: „Supporting Fire“, nicht in der Vergleichsliste) und die Theostone-Nummern
    30000171 und 30000191.
  - Das ist eine Plausibilitätsprüfung, kein Beweis für jede Nummer.
- Das Abgleich-Werkzeug „Name + Summe eintippen → Nummer vorschlagen“ ist **nicht** gebaut, weil die Liste den Zweck
  schon erfüllt und keine Aufnahme des Vergleichskampfs existiert.

| Prüfung (~11:10) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **282/282** (neu: aktive DPS ab 3 s, Community-Namen genau/Grundnummer, eigener Name vor Community, Liste nie geschrieben), App **9/9**, Npcap **23/23** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v11`, `artifacts/persoenlich-win-x64-v3` |
| Bytesuche | Community-Namen in keiner EXE; Bilder: persoenlich-v3 **8**, Endnutzer-v11 **0** |
| Fenster im Spiel | **nicht geprüft** (nichts gestartet) |

### Dunkles Design für Knöpfe, Schalter und Titelleisten (v12, Stand 13.10.2026, ~11:25)

- `DarkTheme` (nur Optik) gilt für Einstellungen, Spieler-Details, Kämpfe und Spielerliste sowie für die
  Umbenennen-Dialoge:
  - dunkle, abgerundete Knöpfe mit Hover (lila Rand)
  - Kontrollkästchen als Schiebeschalter
  - dunkle Titelleiste über `DwmSetWindowAttribute` („Immersive Dark Mode“; auf alten Windows-Versionen bleibt
    sie hell)
- Das Debug-Hauptfenster bleibt hell, die Live-Tabelle in den Standardfarben. Auswahllisten (ComboBox) haben noch
  das Windows-Design.
- Einstellungen → Kampf: Die Platzhalter „Zielmodus“, „Nur Gruppe erfassen“ und „Nur Bosse / Trainingspuppen“ sind
  ausgeblendet, bis sie an Aufnahmen belegt sind. Für „Nur Bosse / Trainingspuppen“ ist als Nächstes eine Aufnahme
  mit einer Trainingspuppe nötig.

| Prüfung (~11:25) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **282/282**, App **10/10** (neu: dunkle Stile laden und anwenden), Npcap **23/23** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v12` (Bilder 0), `artifacts/persoenlich-win-x64-v4` (Bilder 8) |
| Aussehen im Spiel | Von mir nicht geprüft (nichts gestartet). **Laut Besitzer (13.10.2026, 12:31) funktioniert alles:** Klassen-Icons, Fähigkeitsnamen, neues Aussehen von Einstellungen und Details, Event-Alarm und Tray-Symbol. |

### Flüssiges, kompaktes Overlay; „Nur ich“ als Standard (v13, Stand 13.10.2026, ~12:55)

- **Flüssiger:**
  - Das Overlay holt 10× pro Sekunde neue Werte (vorher 2×).
  - Zeilen werden nicht mehr jedes Mal neu gebaut, sondern bleiben je Spieler bestehen.
  - DPS, Schaden, Balkenbreite und Position gleiten in ca. 250 ms mit Abbremsen zum neuen Wert
    (`CompositionTarget.Rendering`, nur solange sich etwas bewegt). Zahlen zählen also hoch, und ein Platztausch
    verschiebt die Zeilen weich.
  - Ziffern sind gleich breit (tabellarisch), damit beim Hochzählen nichts zittert.
- **Kompakter, nach dem Vorbild-Screenshot des Besitzers:**
  - je Zeile Klassen-Icon, Name (+ ICH), **DPS „28,81K/s“** (unter 100K zwei Nachkommastellen), Gesamtschaden und
    Anteil
  - Fußleiste mit eigenem Charakter, **Deckkraft-Regler** (gespeichert beim Loslassen) und Sperre
  - Boss-HP, Enrage-Timer und PvE/PvP-Umschalter des Vorbilds sind **nicht** gebaut, weil sie in unseren Daten nicht
    belegt sind.
- **Anzeige-Modus:**
  - Standard ist **„Nur ich“**: Das Overlay zeigt nur den eigenen Charakter (bzw. den gemerkten „?“-Kandidaten).
  - Anteil und „+ n weitere Spieler“ beziehen sich weiter auf alle.
  - **„Alle Spieler anzeigen“** gibt es in Einstellungen → Anzeige oder über 👤/👥 im Overlay-Kopf.
  - Ist der eigene Charakter noch unbekannt, steht dort der Hinweis „einmal teleportieren, oder 👥“.
- **Gruppe:** Laut der öffentlichen Quelle (cyberbadger6969/aion2-dps-meter, nur gelesen, kein Code übernommen)
  steckt die Gruppenliste in Typ **02 97**. In **keiner** der 16 eigenen Aufnahmen kommt ein Rahmen dieses Typs vor
  (neuer Test `SearchesPartyRosterTypeInOwnRecording`). Die Bytefolge 02 97 taucht nur vereinzelt mitten in großen
  Rahmen auf, also zufällig. Vermutlich war der Besitzer bei allen Aufnahmen allein. Der Modus „Gruppe“ wird
  freigeschaltet, sobald eine Aufnahme in einer Gruppe das belegt.

| Prüfung (~12:55) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **284/284** (neu: „Nur ich“, eigener Charakter unbekannt, „28,81K/s“), App **10/10**, Npcap **24/24** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v13` (Bilder 0), `artifacts/persoenlich-win-x64-v5` (Bilder 8) |
| Animation und Aussehen im Spiel | **nicht geprüft** (nichts gestartet; die Animation ist nicht automatisch getestet) |

### Boss-HP-Leiste (v14, Stand 13.10.2026, ~15:00) – noch nicht im Spiel bestätigt

Grundlage sind Hypothesen aus cyberbadger6969/aion2-dps-meter (GPL-3.0). Ich habe sie nur gelesen und keinen Code
übernommen; `EnemyHpHypothesis` ist selbst geschrieben. Geprüft wurde an allen 16 eigenen Aufnahmen (neue
Npcap-Tests `ChecksRemainingHpHypothesisOnOwnRecording` und `ChecksSpawnMaxHpHypothesisOnOwnRecording`, beide nur
lesend).

- **00 8D = Rest-HP** (Ziel varint, Art varint mit 2 = Gegner, varint, varint, HP u64).
  - Beleg: In `232701` sank die HP zweier Gegner über den ganzen Kampf genau um den gemessenen Schaden (183.461 zu
    183.877, Verhältnis 1,00 und 0,97).
  - Dort fiel außerdem ein Gegner mit **75.002.019 Max-HP** bis auf 0.
- **Ehrliche Einschränkung:** Bei großen Kämpfen sinkt die HP 2–3× (einzeln bis 9×) stärker als der Schaden, den wir
  sehen. **Wir bekommen also nur einen Teil der Treffer anderer Spieler mit.** Auch die DPS- und Anteilswerte anderer
  Spieler sind in solchen Kämpfen daher unvollständig. Die HP-Leiste betrifft das nicht, weil sie direkt aus der
  HP-Meldung kommt.
- **41 36 (Erscheinen) enthält die Max-HP** als erstes „01 aktuell max“.
  - In allen Fällen mit HP-Meldung danach passte das Max zum bekannten Gegner-Maximum (z. B. 119.700 bzw. 63.000),
    und die gemeldete HP blieb darunter.
  - Steigt man mitten in einen Kampf ein, kennt man die Max-HP nicht. Dann zeigt die Leiste nur die Rest-HP, ohne
    Prozent.
- **Overlay:** eine rote Leiste unter dem Kopf mit „Gegner #Kennung *“, Prozent, Rest-/Max-HP und „Kill ~m:ss“.
  - Die Kill-Zeit wird aus der HP-Abnahme der letzten 10 s geschätzt, nicht aus unserem Schaden.
  - Ziel ist der Gegner, den der eigene Charakter in den letzten 10 s am meisten getroffen hat.
  - Einstellung: Einstellungen → Anzeige → „Boss-Leiste anzeigen“ (Standard an).
  - Gegnernamen und Enrage-Timer gibt es **nicht**: Der Name bräuchte die Gegner-Nummer aus 41 36 plus eine lokale
    Namensliste und ist nicht geprüft. Ein Enrage-Timer ist in den Daten nicht belegt.
- **Nachgespielt mit dem echten Decoder-Weg** (`232701`): „Gegner #27804: 63,03M HP · Kill ~0:46“, danach 55,60M
  (~0:41), 38,58M (~0:29), 26,31M (~0:17). Für normale Gegner zum Beispiel „49,5 % · 91,1K / 184,0K · Kill ~0:09“.

| Prüfung (~15:00) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **290/290** (neu: 00 8D/41 36 lesen, Prozent/Kill-Zeit, ohne Max nur HP, veraltete Meldung, Hauptziel), App **10/10**, Npcap **26/26** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v14` (Bilder 0), `artifacts/persoenlich-win-x64-v6` (Bilder 8) |
| Leiste im Spiel, Vergleich mit der Spiel-HP-Leiste | **nicht geprüft** – dafür braucht es eine Aufnahme mit Screenshot der Spiel-Leiste |

### Vollbreite Boss-Leiste, „Nur ich“ ohne Fremdhinweis (v15/v16, Stand 13.10.2026, ~15:20)

- **Boss-Leiste als richtige HP-Leiste über die ganze Breite:**
  - dunkelrote Bahn mit Rahmen für die verlorene HP
  - hellroter Verlauf für die Rest-HP, gleitet in 250 ms zum neuen Wert
  - Name und Werte stehen darauf
  - Anlass: Im ersten Screenshot des Besitzers (Trainingspuppe #19072, „37,3 % · 44,6K / 119,7K“) wirkte die
    Füllung wie ein Kästchen um den Namen. Dieselbe Puppe mit 119.700 Max-HP kommt in fast allen älteren Aufnahmen
    vor.
- **„Nur ich“:** Die Zeile „+ n weitere Spieler“ entfällt in diesem Modus ganz. Bei „Alle“ erscheint „+ n weitere“
  nur für Spieler über dem Zeilenlimit.
- v15 (`artifacts/endnutzer-win-x64-v15`, `persoenlich-win-x64-v7`) enthält nur die Leiste. **v16**
  (`endnutzer-win-x64-v16`, `persoenlich-win-x64-v8`) enthält beides. Die Ordner wurden nicht überschrieben.

| Prüfung (~15:20) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **290/290**, App **10/10**, Npcap **26/26** |
| Bytesuche Bilder | Endnutzer-v16 **0**, persoenlich-v8 **8** |
| Leiste im Spiel | Laut Screenshot des Besitzers erscheint sie an der Trainingspuppe. Der Abgleich mit der Spiel-HP-Leiste steht noch aus. |

### Gruppe erkannt, Boss-HP an einer Gruppen-Aufnahme belegt (v17, Stand 13.10.2026, ~15:50)

Die Aufnahme `aufnahme-20261013-172129.pcap` (12 min, 5er-Gruppe, Boss) habe ich nur lesend ausgewertet. Namen
erscheinen nur maskiert; die Datei bleibt beim Besitzer.

- **Gruppenliste 02 97 (Community-Hypothese):**
  - **12 Rahmen** des Typs, wiederholt gesendet (bei 13 s, 160 s, 166 s, 195 s … 728 s).
  - Laut Kopf ist die Gruppengröße **5**.
  - Alle **5** Spieler mit Namen (darunter der eigene Charakter) stehen als „Längenbyte + Name“ in der Liste. Es sind
    genau die 5 Spieler mit dem meisten Schaden (je 15–21 %).
  - Ein vollständiges Zerlegen Mitglied für Mitglied gelang nur für das erste Mitglied, weil die Felder dazwischen
    unterschiedlich lang sind. Deshalb sucht `PartyRoster.Contains` die Namen direkt; das ist ausreichend und
    robust.
- **Neuer Standard „Ich + Gruppe“:**
  - Im Modus ohne „Alle Spieler“ zeigt das Overlay dich und, sobald eine Gruppenliste kam, die Gruppenmitglieder.
    Dein eigener Charakter bleibt immer sichtbar.
  - Mit 👥 bzw. „Alle Spieler anzeigen“ siehst du alle.
  - Ein Austritt aus der Gruppe ist nicht belegt. Die Mitglieder gelten, bis eine neue Liste kommt.
- **Boss-HP in dieser Aufnahme:**
  - 00 8D: 7.816 von 8.399 Abgleichen genau (HP-Abnahme = gemessener Schaden).
  - Boss #16659 mit **15.504.000 Max-HP**: HP-Abnahme gesamt 28,7M, gemessener Schaden 27,6M, Verhältnis **1,04**
    (in der Gruppe sehen wir offenbar fast alle Treffer).
  - Gegner mit rund 567K–570K Max-HP: Verhältnis 0,99–1,00.
  - Nachgespielte Leiste: „97,1 % · 15,05M / 15,50M · Kill ~5:34“ … „80,9 % · 12,54M / 15,50M · Kill ~2:09“.
  - Eine Bestätigung mit einem Screenshot der Spiel-HP-Leiste fehlt weiterhin.
- **Bekannt, noch nicht behoben:**
  - Im Modus „Alle“ können Gegner selbst als Zeilen erscheinen. Beispiel: Der Boss #16659 macht mit Fertigkeiten
    der Nummernreihe 17… Schaden und wird deshalb als „Kleriker“ gezählt.
  - Ebenso Zeilen ohne Namen mit je 18 Treffern und 100 % Krit, wohl Effekte.
  - In „Ich + Gruppe“ stört das nicht.

| Prüfung (~15:50) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **293/293** (neu: Gruppenliste lesen, Namenssuche, „Ich + Gruppe“, eigene Zeile trotz Limit), App **10/10**, Npcap **27/27** |
| Nachspielen `172129` über den Live-Weg | Gruppe Größe 5, **5 Mitglieder erkannt** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v17` (Bilder 0), `artifacts/persoenlich-win-x64-v9` (Bilder 8) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Gegner raus aus der Spielerliste, Automatik-Ansicht, schlichte Fußzeile (v18, Stand 13.10.2026, ~16:05)

- **Gegner gefiltert** (`LiveCombatMeter.Enemies`, nur belegte Fälle). Sie zählen weder als Zeile noch für Anteile
  oder Gesamtschaden, auch nicht im Kampfverlauf. Als Gegner gilt:
  1. eine Kennung mit Gegner-HP-Meldung (00 8D, Art 2)
  2. ein Ziel von Treffern des eigenen Charakters oder eines Spielers mit Namen
  3. ein namenloser Verursacher, der Spieler mit Namen trifft
  - Spieler mit Namen sind nie Gegner.
  - Nachgespielt an `172129`: **133** Kennungen herausgenommen, darunter der Boss #16659, der vorher als „Kleriker“
    erschien. Die 5 Gruppenmitglieder kommen jetzt zusammen auf fast 100 %.
  - **Bewusst nicht versteckt:** namenlose Verursacher, die nur ein einzelnes fremdes Ziel treffen (z. B. „Magier,
    18 Treffer, 100 % Krit“). Wahrscheinlich sind das Spieler außerhalb der Gruppe, deren Namen nicht ankamen. Es ist
    unklar, also bleiben sie im Modus „Alle“ sichtbar.
- **Ansicht** (`PlayerView`):
  - **Automatik** (Standard): allein nur ich, in einer Gruppe ich + Gruppe, ohne Knopfdruck.
  - Der Knopf im Overlay-Kopf (Ⓐ / 👤 / 👥) wechselt per Linksklick zwischen „Alle“ und „Nur ich“, per Rechtsklick
    zurück zur Automatik. Der Tooltip zeigt den Zustand („Automatik: Gruppe“, „Nur ich“, „Alle Spieler“).
  - Außerdem einstellbar in Einstellungen → Anzeige → „Spieler im Overlay“.
  - Alte Einstellung „Alle Spieler anzeigen = an“ wird zu „Alle“.
- **Fußzeile:** Der Deckkraft-Regler ist entfernt, die Deckkraft gibt es nur noch in den Einstellungen. Übrig bleiben
  eigener Charakter und Schloss.

| Prüfung (~16:05) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **295/295** (neu: Gegnerfilter inkl. Anteilen, Ansicht/Übernahme alter Einstellung), App **10/10**, Npcap **27/27** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v18` (Bilder 0), `artifacts/persoenlich-win-x64-v10` (Bilder 8) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Lebendigeres Overlay (v19, Stand 13.10.2026, ~16:25)

Das ist nur Optik. Alles außer Glanz und Farbverlauf lässt sich abschalten unter Einstellungen → Anzeige →
„Effekte & Animationen“ (Standard an).

1. **Balken:** Farbverlauf in Klassenfarbe (oben heller, unten dunkler) mit leichtem Glanz auf der oberen Hälfte.
   Die eigene Zeile ist violett und hat eine dezente Leuchtkante.
2. **Aufblitzen:** Die DPS-Zahl wird 350 ms lang gelb, wenn der Schaden seit der letzten Aktualisierung (100 ms) um
   mehr als das 2,5-fache des Durchschnittstreffers wächst, also bei großen Treffern bzw. Krits.
3. **Kampf-Punkt und Kampfende-Karte:**
   - Der grüne Punkt pulsiert, solange gekämpft wird.
   - Beim Kampfende erscheint 8 s lang eine Karte, z. B. „🏁 Kampf beendet · 1:19 · 14,5K DPS · 22,3 % · Platz 1
     von 5 · Bestwert 16,2K (−10 %)“ oder „neuer Bestwert!“.
   - Der Bestwert ist die beste eigene DPS aus der lokalen Kampfliste, nur Kämpfe ab 10 s und ohne den gerade
     beendeten (`FightStatistics.BestOwnDps`).
4. **Boss-Leiste:**
   - Die rote Füllung folgt in 200 ms.
   - Ein heller Streifen zieht die verlorene HP nach 350 ms langsam nach (700 ms).
   - Unter 20 % HP pulsiert die Füllung.
5. **Glas-Look:**
   - Die Kopfzeile ist leicht durchscheinend, mit Verlauf und feinem Rand.
   - Das Overlay hat rundere Ecken (12 px) und einen weichen Schatten.
   - DPS und Kampfzeit stehen in der Windows-Systemschrift **Bahnschrift** (schmal, kräftig; mitgeliefert mit
     Windows, keine fremde Schriftdatei, Rückfall Segoe UI). Namen sind etwas dezenter.

Leistung: Animationen laufen nur, solange sich etwas bewegt. Der Puls läuft als WPF-Animation, und die
Zeilen-Effekte sind eingefrorene Pinsel. Mit „Effekte & Animationen“ aus fallen Schatten, Puls, Aufblitzen und
Nachziehen weg.

| Prüfung (~16:25) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **299/299** (neu: Kampfende-Text inkl. Bestwert, Bestwert ohne kurze/aktuelle Kämpfe, Effekte standardmäßig an), App **10/10**, Npcap **27/27** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v19` (Bilder 0), `artifacts/persoenlich-win-x64-v11` (Bilder 8) |
| Aussehen und Animationen im Spiel | **nicht geprüft** (nichts gestartet; die Effekte sind nicht automatisch getestet) |

### Nur eigene Kämpfe zählen, Boss-Leiste nur für eigene Ziele (v20, Stand 13.10.2026, ~16:45)

Anlass war ein Screenshot des Besitzers an den Trainingspuppen. Sein Charakter war noch nicht erkannt, trotzdem
zeigte das Overlay „Kampf 0:18“ und eine Boss-Leiste für Puppe #19974, die gerade andere Spieler schlugen.

- **Boss-Leiste:**
  - Ziel ist der Gegner mit dem meisten Schaden in den letzten 10 s, gezählt nur von dir (Ansicht „Nur ich“), von
    dir + Gruppe (Automatik) oder von allen (Ansicht „Alle“).
  - Ohne erkannten eigenen Charakter gibt es in Automatik und „Nur ich“ keine Leiste.
  - Außerdem werden Füllung und Nachzieh-Streifen jetzt über `ScaleTransform` auf den Prozentwert skaliert statt
    über Pixelbreiten. Im Screenshot passte die Füllung bei „100,0 %“ nicht zur Leistenbreite.
- **Kampfstatus, Kampfzeit, DPS und Kampfende-Karte** kommen in Automatik/„Nur ich“ nur noch von den gezeigten
  Spielern (ich + Gruppe):
  - Fremde starten keinen Kampf und halten ihn nicht am Laufen.
  - Meine DPS bleiben nach meinem letzten Treffer stehen (`LiveActorStatistics.LastHit`).
  - Ohne erkannten Charakter steht „Inaktiv“ da, mit nur dem Teleport-Hinweis.
  - Im Modus „Alle“ bleibt alles wie bisher.
- **Kampfverlauf:** Gespeichert wird nur noch, wenn du oder ein Gruppenmitglied Schaden gemacht hat
  (`FightStatistics.IsRelevant`).

| Prüfung (~16:45) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **304/304** (neu: Fremde halten den eigenen Kampf nicht offen, kein Kampf ohne eigenen Charakter, Speichern nur mit eigenen/Gruppen-Treffern, Hauptziel nach Quelle, kein Hauptziel ohne eigene Treffer), App **10/10**, Npcap **27/27** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v20` (Bilder 0), `artifacts/persoenlich-win-x64-v12` (Bilder 8) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Eigene Zeile in Klassenfarbe, Anteil passend zur Ansicht (v21, Stand 13.10.2026, ~16:55)

- **Eigene Zeile** in der Klassenfarbe statt violett, z. B. Bogenschütze grün. Hervorgehoben wird sie nur noch durch
  eine Leuchtkante in heller Klassenfarbe und das „ICH“-Schild.
- **Anteil %** bezieht sich auf die gezeigten Spieler: „Nur ich“ = 100 %, Automatik in der Gruppe = Anteil in der
  Gruppe, „Alle“ = Anteil an allen (ohne Gegner).
- Die **Kampfende-Karte** rechnet Anteil und „Platz x von n“ genauso, also nur unter den gezeigten Spielern.
- Das **Detailfenster** zeigt Anteile je Fähigkeit innerhalb des Spielers und ist davon nicht betroffen.

| Prüfung (~16:55) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **306/306** (neu: Anteil nur ich/Gruppe/alle, Kampfende-Karte unter gezeigten Spielern), App **10/10**, Npcap **27/27** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v21` (Bilder 0), `artifacts/persoenlich-win-x64-v13` (Bilder 8) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Weltboss-Timer (v22, Stand 13.10.2026, ~17:20)

- **Selbst eintragen** (Einstellungen → Event-Alarme → „Weltbosse“). Die Zeiten sind je Server verschieden.
  - Standard-Eintrag ist **„Unsterblicher Gartua“** mit Respawn-Abstand **12:00:00** (Angabe des Besitzers).
  - Bosse lassen sich hinzufügen, umbenennen und entfernen.
- **Zeit eintragen**, auf eine von drei Arten:
  - „Kommt wieder in“: die Restzeit wie im Spiel, z. B. „4:16:50“ oder „Verbleibende Zeit 4 Std. 16 Min. 50 Sek.“
    (`WorldBossTimes.ParseCountdown`)
  - „Kill um 17:42“ (heute oder gestern, `ParseKillTime`)
  - „Gerade getötet“
  - Bei einem Kill gilt: nächster Spawn = Kill + Abstand.
  - Ist die Zeit abgelaufen, steht dort „Boss sollte da sein“. Es wird **kein** weiterer Zyklus geraten.
- **Erinnerung** je Boss mit Vorlauf 1/5/10/15 Min.:
  - Ton plus Overlay-Zeile „⏰ Weltboss Gartua in 10 Min. (22:23)“, genau einmal je Termin.
  - Die Zeile „Nächstes: …“ im Overlay berücksichtigt auch Weltbosse.
- **Speicher:** `Dokumente\Aion2DpsMeter\weltbosse.json` (Kennung `Aion2DpsMeter-Weltbosse`). Eine fremde oder
  kaputte Datei wird nie überschrieben. Es gibt keinen Internetabruf.
- **Feldboss-Liste aus dem Spiel (01 91), gedeutet, aber noch nicht genutzt** (`FieldBossListHypothesis`,
  Community-Beschreibung, eigener Code):
  - In `093400`/`093659` steht je 3× die Liste für Karte **1110** (laut Community „Altgard“) mit **24 Plätzen**:
    21 leben (seit 12./13.10. 23:59 – 05:39), 3 tot mit Rückkehrzeit (z. B. Platz 22 um 10:28:40, also +54 min nach
    der Aufnahme).
  - In `172129` hat der einzige 01-91-Rahmen einen anderen Aufbau (48 B, Karte nicht im Kopf erkennbar). Er wird nicht
    gedeutet.
  - Welcher Platz welcher Boss ist (z. B. Gartua), ist **nicht** belegt. Deshalb füllt die Liste noch keine Zeiten.
    Dafür braucht es eine Aufnahme beim Öffnen der Boss-Übersicht plus einen Screenshot mit den Restzeiten.
- **Automatische Kill-Erkennung** ist **nicht** gebaut: Ein Gegner lässt sich in den Daten noch keinem Weltboss
  zuordnen (Name/Nummer).

| Prüfung (~17:20) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **320/320** (neu: Restzeit-Formate, Kill-Uhrzeit heute/gestern, Gartua 12 h, Erinnerung einmal, Datei inkl. Abstand, fremde Datei bleibt, Feldboss-Liste lesen), App **10/10**, Npcap **28/28** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v22` (Bilder 0), `artifacts/persoenlich-win-x64-v14` (Bilder 8) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Fehler „Gruppe im Dungeon nicht gezeigt“ (v23, Stand 13.10.2026, ~18:40)

- **Ursache, belegt:**
  - In `%LOCALAPPDATA%\Aion2DpsMeter\overlay.json` des Besitzers stand `"PlayerView": "Nur ich"` (nur gelesen,
    nicht verändert).
  - Bis v22 wechselte ein Linksklick auf den Overlay-Knopf zwischen „Alle“ und „Nur ich“ und **speicherte das
    dauerhaft**. Nach zwei Klicks (Automatik → Alle → Nur ich) blieb man unbemerkt in „Nur ich“, die Gruppe wurde nie
    gezeigt.
- **Behoben:**
  - Der Knopf schaltet nur noch **vorübergehend** zwischen der eingestellten Ansicht (Standard Automatik) und „Alle“
    um. Das wird nicht gespeichert; Rechtsklick führt ebenfalls zurück.
  - Dauerhaft wählt man die Ansicht nur in Einstellungen → Anzeige (`PlayerViewChosen`).
  - Alte, nur per Knopf gesetzte Ansichten fallen beim Laden auf die **Automatik** zurück. Damit startet auch die
    Datei des Besitzers wieder in der Automatik.
- **Zusätzlich ehrlich angezeigt:** Ist eine Gruppe gemeldet, aber nicht jeder Name bekannt, steht im Tooltip des
  Knopfs „Gruppe: x von y erkannt“. Das passiert z. B., wenn der Meter erst im Dungeon gestartet wurde und die
  Mitglieder schon im Blickfeld standen: Namen kommen nur bei 45 36 bzw. beim Teleport.
- **Geprüft:** An `172129` kommen Gruppenlisten nur als eigener Typ 02 97 vor (12×), eingebettet in anderen Rahmen
  **0×**; daran lag es also nicht. Eine Dungeon-Aufnahme gibt es nicht. Ob die Liste beim Dungeon-Eintritt neu
  kommt, ist **nicht geprüft**.

| Prüfung (~18:40) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **321/321** (neu: Knopf-Ansicht fällt auf Automatik zurück; in den Einstellungen gewählte Ansicht bleibt), App **10/10**, Npcap **28/28** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v23` (Bilder 0), `artifacts/persoenlich-win-x64-v15` (Bilder 8) |
| Im Dungeon | **nicht geprüft** (nichts gestartet, keine Dungeon-Aufnahme) |

### Boss-Namen und automatischer Gartua-Timer (v24, Stand 14.10.2026, ~09:20)

- **Gegner-Namen in der Boss-Leiste:**
  - Die Gegner-Nummer (NPC-Vorlage) steht laut Community-Beschreibung als u24 direkt vor der Markierung
    `00 (00|40) 02` im Erscheinen-Rahmen 41 36 (`EnemyHpHypothesis.ParseNpcCode`, eigener Code).
  - An den Aufnahmen gefunden in **119/123** (`093400`) und **1.375/1.387** (`172129`) Erscheinen-Rahmen.
  - Mit der Community-Gegnerliste ergeben sich stimmige Namen: „Trainingspuppe“ (2400032) an den Puppen;
    im Feuertempel „Kromedes Gier“, „Lavaklumpen (Feuertempel)“, „Funkengeist“ … Der Boss #16659 mit 15,5 Mio. HP ist
    „Kromedes Gier“ (Boss). Kromede stand auch im Screenshot des Vergleichsmeters.
  - Die Leiste zeigt jetzt z. B. „Kromedes Gier *“ bzw. „Trainingspuppe (Puppe) *“, ohne Namen weiter
    „Gegner #Kennung *“.
- **Gegnerliste:** `data/npcs/de.json` aus cyberbadger6969/aion2-dps-meter (GPL-3.0, nur gelesen; laut deren NOTICE
  aus Spieldaten © NCSOFT). Sie wurde einmalig lokal als `Dokumente\Aion2DpsMeter\npcs-community.json` abgelegt,
  nur gelesen (`NpcNames`). Sie ist **nicht** in der EXE und nicht im Repository (Bytesuche: 0).
- **Gartua automatisch aus der Boss-Übersicht des Spiels (01 91):**
  - Laut Gegnerliste ist „Unsterblicher Gartua“ die Nummer 2400800, also Block 2400 der Karte 1110 (Community:
    „Altgard“, Block 2400).
  - Die Liste hat 24 Plätze, und Block 2400 enthält genau 24 Bosse (ohne die 3 Trainingspuppen). Gartua ist der 21.
    davon → **Platz 21**.
  - In `093400` meldete Platz 21 „tot, zurück 13.10. 10:21:44“. Plus 12 h ergibt 22:21:44, was zur Restzeit aus dem
    Screenshot des Besitzers passt (~22:23 um ~18:06). Das ist **plausibel, aber nicht gleichzeitig mit Aufnahme und
    Screenshot bestätigt**.
  - Kommt die Liste der Karte 1110 (`WorldBossTimes.Apply`), übernimmt der Meter Gartuas Zeit, speichert sie und
    zeigt sie mit „(aus der Boss-Übersicht des Spiels*)“ an. Eigene Eingaben bleiben möglich und überschreiben sie
    (`FromGame = false`).
  - Ältere `weltbosse.json` (v22) bekommen Karte, Platz und Abstand von Gartua ergänzt.
- **Kill-Erkennung über 04 8D:** **nicht** gebaut. Die Liste des Spiels liefert die Rückkehrzeit direkt, sobald sie
  gesendet wird. Ohne die Liste gilt weiter „Gerade getötet“ bzw. die Kill-Uhrzeit + 12 h.

| Prüfung (~09:20) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **324/324** (neu: Gegner-Nummer lesen, Namensliste inkl. Puppe, Gartua aus Platz 21, Ergänzung alter Einträge), App **10/10**, Npcap **29/29** |
| Nachspielen `093400` | „Gartua laut Liste: 13.10. 10:21:44“ |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v24`, `artifacts/persoenlich-win-x64-v16` (Bilder 0/8, Gegnerliste in keiner EXE) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Kampfkraft neben dem Namen (v25, Stand 14.10.2026, ~09:40)

- **Herkunft** (`CombatPowerHypothesis`, nach der Beschreibung in cyberbadger6969/aion2-dps-meter bzw.
  A2Tools-DPS-Meter, eigener Code):
  - **Eigener Charakter:** Am Ende des Datensatzes 33 36 stehen zweimal u64 (aktuelle und höchste Kampfkraft).
  - **Gruppenmitglieder:** in der Gruppenliste 02 97 je Mitglied nach Name, Klasse, Stufe und GS, hinter der
    wiederholten Server-Nummer. Zusätzlich gibt es den Ausrüstungswert (GS).
  - Für **fremde** Spieler schickt das Spiel beides nicht. Sie bekommen keine Anzeige.
- **An Aufnahmen nachgespielt:**
  - eigene Kampfkraft 92.999 (`093400`, `010703`) bzw. 96.770 (`172129`, am Nachmittag; steigt also mit der Zeit)
  - Gruppe in `172129`: 95,6K–114,9K, GS 2.182–2.322
  - Der Wert der Gruppenliste für den eigenen Charakter (96.770) ist **gleich** dem aus 33 36. Die Größenordnung
    passt zum Screenshot des Besitzers („114,7K“).
  - Ein direkter Abgleich mit der Anzeige im Spiel steht noch aus.
- **Overlay:** Neben dem Namen stehen Symbol und Kurzzahl, z. B. „96,8K“. Abschaltbar unter Einstellungen → Anzeige →
  „Kampfkraft anzeigen“ (Standard an).
- **Symbol:** In der Endnutzer-EXE erscheint ein neutrales Zeichen „⚔“. Das Kampfkraft-Logo aus dem Screenshot des
  Besitzers (NCSOFT-Grafik) wurde lokal als `Dokumente\Aion2DpsMeter\Icons\kampfkraft.png` ausgeschnitten. Es wird
  nur in der Variante **persoenlich** eingebettet (optional, fehlt es, bricht der Bau nicht ab) und wird auch aus dem
  Icon-Ordner geladen.

| Prüfung (~09:40) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **326/326** (neu: Kampfkraft aus Gruppenliste und eigenem Datensatz, nur eigene/Gruppe, Zeile mit Kampfkraft), App **10/10**, Npcap **29/29** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v25` (Bilder 0), `artifacts/persoenlich-win-x64-v17` (Bilder 9 inkl. Kampfkraft-Symbol) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Fenster vor dem Spiel (v26, Stand 14.10.2026, ~09:35)

- **Problem:** Laut Rückmeldung des Besitzers lagen Einstellungen (und andere Fenster) hinter dem Spiel. Weil das
  Overlay nie den Fokus nimmt, erlaubt Windows unserem Programm nicht, neue Fenster nach vorn zu holen.
- **Lösung (nur normale WPF-Mittel, nichts am Spiel):** Alle Fenster mit `DarkTheme` sind jetzt „immer oben“
  (`Topmost`), werden beim Öffnen aktiviert und liegen damit über dem Spiel. Das betrifft Einstellungen,
  Spieler-Details, Kämpfe, Spielerliste und Umbenennen-Dialoge. Ebenso das Entwickler-Fenster und die Live-Tabelle.
- **Grenze:** Im **exklusiven Vollbild** des Spiels kann kein normales Fenster darüber liegen, auch das Overlay
  nicht. Dafür „Randloses Fenster“ im Spiel wählen. Geprüft werden kann das nur vom Besitzer.

| Prüfung (~09:35) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **326/326**, App **11/11** (neu: dunkle Fenster sind „immer oben“), Npcap **29/29** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v26` (Bilder 0), `artifacts/persoenlich-win-x64-v18` (Bilder 9) |
| Mit dem Spiel | **nicht geprüft** (nichts gestartet) |

### Boss-Kopf wie beim Vergleichsmeter; Berserk-Prüfung (v27, Stand 14.10.2026, ~09:50)

- **Boss-Kopf:** Der Aufbau ist dem Vorbild-Screenshot des Besitzers abgeschaut (nur Design, ohne Porträt):
  - großer Name, darunter der Status: „Kampf läuft · Kill ~0:46“, „Besiegt“ bzw. „Max-HP unbekannt“
  - rechts **TOP-TREFFER**: der höchste direkte Einzeltreffer der gezählten Spieler (ich/Gruppe/alle je nach
    Ansicht) auf diesen Gegner, aus unseren Schadensdaten (`LiveCombatMeter.TopHit`)
  - darunter die breite rote Leiste mit den aktuellen HP ausgeschrieben links („49.528.026 HP“) und dem Prozentwert
    rechts
  - Effekte wie gehabt abschaltbar
- **Berserk/Enrage, nur untersucht:** In den Aufnahmen gibt es **kein** belegtes Berserk-Feld. Gefunden wurde der
  Instanz-Timer **00 61** (Community-Hypothese: Karte u32, Art u8, Start u64 ms, Ende u64 ms). In `172129`
  (Karte 600022) kommt er zweimal:
  - Art 2: 17:15:24 → 18:15:24, genau **1 h**, wohl das Zeitlimit des Dungeons
  - Art 3: 17:33:37 → 17:43:37, genau **10 min**. Er beginnt 2 s nach dem Tod des Bosses „Kromedes Gier“
    (HP 0 um 17:33:35), ist also eine Frist **nach** dem Kill und kein Berserk-Timer.
  - Ein Berserk-Countdown wäre daher nur **manuell** möglich (Dauer je Boss, ab dem ersten Treffer), deutlich als
    „manuell“ gekennzeichnet. Eine Dungeon-Restzeit aus Art 2 wäre dagegen belegbar.

| Prüfung (~09:50) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **328/328** (neu: Boss-HP-/Status-Texte, Top-Treffer nur direkt, nur Quellen, nur Ziel), App **11/11**, Npcap **29/29** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v27` (Bilder 0), `artifacts/persoenlich-win-x64-v19` (Bilder 9) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Einstellungsfenster neu gestaltet (v28, Stand 14.10.2026, ~10:05)

Nur Optik; Funktionen und gespeicherte Einstellungen sind unverändert.

- **Navigation:**
  - Kopf „DPS Meter / Einstellungen“
  - Seiten mit Hover-Effekt; die gewählte Seite hat einen violetten Akzentstreifen und einen getönten Hintergrund
  - Fenster 880×640
- **Seiten:**
  - großer Seitentitel
  - Abschnittsüberschriften in Akzentfarbe mit feiner Linie
  - jede Einstellung als **Karte** (dunkel, abgerundet, Rand leuchtet beim Überfahren); Titel 14 pt, Beschreibung
    dezent
  - Hinweise als getönte Karte mit „ⓘ“
  - Event- und Weltboss-Karten im selben Stil
- **Bedienelemente:** Auswahllisten (ComboBox) und Eingabefelder sind jetzt ebenfalls dunkel. Die Liste klappt als
  dunkles Feld auf, mit Akzentrand bei Fokus bzw. beim Überfahren (`DarkTheme`). Regler bekommen Akzentfarben.
- **Neuer App-Test:** Das Einstellungsfenster wird aufgebaut und jede Seite einmal erzeugt, ohne angezeigt zu werden.

| Prüfung (~10:05) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **328/328**, App **12/12** (neu: alle Einstellungsseiten erzeugbar), Npcap **29/29** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v28` (Bilder 0), `artifacts/persoenlich-win-x64-v20` (Bilder 9) |
| Aussehen | **nicht im laufenden Programm geprüft** (nichts gestartet) |

### Manueller Berserk-Timer (v29, Stand 14.10.2026, ~10:20)

- **Warum manuell:** Das Spiel schickt in den Aufnahmen keine Berserk-Zeit (siehe v27). Deshalb trägt man sie selbst
  ein: Einstellungen → Kampf → „Berserk-Timer (manuell)“, mit Boss-Name wie in der Boss-Leiste und Dauer m:ss.
  Gespeichert wird das in `overlay.json` (`BerserkSeconds`).
- **Anzeige:** Der Countdown startet beim **ersten Treffer** der gezählten Spieler (ich/Gruppe/alle) auf den Boss
  (`LiveCombatMeter.FirstHitOn`). Er steht in der Statuszeile der Boss-Leiste: „Berserk in 4:12 (manuell)“, unter
  30 s orange, danach „BERSERK!“.

| Prüfung (~10:20) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **329/329** (neu: Countdown-Texte, erster Treffer je Quelle, Normalisierung der Zeiten), App **12/12**, Npcap **29/29** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v29` (Bilder 0), `artifacts/persoenlich-win-x64-v21` (Bilder 9) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Automatischer Berserk-Timer (v30, Stand 14.10.2026, ~10:45)

- **Fund:** Die erste Suche (v27) hatte nur 00 61 betrachtet. Eine gezielte Suche nach Zeitstempeln rund um den
  Kampfbeginn (`SearchesBerserkTimestampAroundBossPull`) fand **21 8D** mit einer Deadline. Laut Community-Beschreibung
  (cyberbadger6969/aion2-dps-meter, nur gelesen) ist das der Kampfzustand: Gegner varint, varint, Zustand
  (1 = Kampf, 0 = raus). Bei Bossen mit Zeitlimit folgt beim Kampfbeginn die Deadline (u64 ms).
  (`BattleStateHypothesis`, eigener Code)
- **Beleg `172129`:** 615 Kampfzustände, 180 Kampfbeginne, **3 mit Deadline**, alle für den Boss **#16659 „Kromedes
  Gier“** und jeweils genau **+5:00** (17:21:42, 17:26:03, 17:28:27; dazwischen wurde er offenbar zurückgesetzt).
  Kill um 17:33:35, 8 s nach der letzten Deadline. Für normale Gegner gibt es keine Deadline.
- **Anzeige:** Die Boss-Leiste zeigt automatisch „Berserk in 4:51“ (unter 30 s orange, danach „BERSERK!“), ohne
  „(manuell)“. Beim Verlassen des Kampfs wird die Deadline vergessen.
  - Nachgespielt mit dem echten Decoder-Weg: „Kromedes Gier … Kill ~5:34 · Berserk in 4:51“ … „Berserk in 4:02“.
  - Manuelle Einträge (v29) gelten nur noch für Bosse **ohne** diese Meldung.

| Prüfung (~10:45) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **330/330** (neu: 21 8D mit/ohne Deadline, Tracker merkt/vergisst, Countdown), App **12/12**, Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v30` (Bilder 0), `artifacts/persoenlich-win-x64-v22` (Bilder 9) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Gruppe am Weltboss nicht gezeigt (v31, Stand 14.10.2026, ~11:15)

- **Meldung:** Screenshot beim Weltboss „Geisterfürst Agro“ in einer 3er-Gruppe. Das Overlay zeigte nur die eigene
  Zeile. Die zweite Zeile mit dem eigenen Namen darunter ist die normale Fußzeile, **kein** Doppel.
- **Wahrscheinliche Ursache, nicht an einer Aufnahme belegt:** Die Gruppe wird nur aus der Gruppenliste 02 97 erkannt,
  die das Spiel bloß gelegentlich schickt (in `172129` kurz nach Aufnahmebeginn, dann im Abstand von Minuten). Wurde
  der Meter nach dem Gruppenbeitritt (neu) gestartet, fehlt sie, bis sie wieder kommt.
- **Robuster gemacht:**
  - Die zuletzt **echt** erkannte Gruppe (Größe + Namen) wird lokal in `%LOCALAPPDATA%\Aion2DpsMeter\gruppe.json`
    gemerkt (`PartyStore`, höchstens 6 h, nur eigene Datei wird ersetzt). Nach einem Neustart gilt sie sofort und wird
    durch die nächste echte Liste ersetzt.
  - Ist eine Gruppe gemeldet, aber nicht jedes Mitglied erkannt (Name noch unbekannt), steht das jetzt **sichtbar**
    im Overlay: „Gruppe: 1 von 3 erkannt – Namen fehlen noch“.
- **Zum sicheren Klären:** eine Aufnahme mit „Aufnahme speichern“, gestartet **vor** dem Gruppenbeitritt bzw. vor einer
  Neueinladung, dann gemeinsam einen Gegner angreifen.

| Prüfung (~11:15) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **332/332** (neu: gemerkte Gruppe bis zur echten Liste, Speicher 6 h, fremde Datei bleibt), App **12/12**, Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v31` (Bilder 0), `artifacts/persoenlich-win-x64-v23` (Bilder 9) |
| Am Weltboss | **nicht geprüft** (keine Aufnahme davon, nichts gestartet) |

### Boss-Leiste nur bei Bossen, ohne Top-Treffer (v32, Stand 14.10.2026, ~11:40)

- **TOP-TREFFER** ist auf Wunsch des Besitzers ausgeblendet. Der Wert wird nicht mehr angezeigt.
- **Boss-Leiste nur für Dungeon- und Welt-/Feldbosse** (`OverlayFormat.IsBoss`). Es zählt die erste zutreffende
  Regel:
  1. Der Gegner meldet ein Zeitlimit (21 8D): immer.
  2. Er steht in der Community-Gegnerliste: nur wenn als „Boss“ markiert und **keine Trainingspuppe** (in der Liste
     sind auch Puppen als Boss markiert, daher die Namensprüfung).
  3. Er steht nicht in der Liste: erst ab **5 Mio. Max-HP**. Unsichere Fälle bleiben ausgeblendet.
- **Geprüft an Listeneinträgen:** „Kromedes Gier“ (2300371) und „Geisterfürst Agro“ (2600068/368/468) sind Boss;
  „Trainingspuppe“ und „Lavaklumpen (Feuertempel)“ bekommen keine Leiste. Der Berserk-Timer bleibt.

| Prüfung (~11:40) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **333/333** (neu: Puppe nein, Kromedes Gier ja, Geisterfürst Agro ja, normaler Gegner nein, unbekannt groß/klein, Zeitlimit), App **12/12**, Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v32` (Bilder 0), `artifacts/persoenlich-win-x64-v24` (Bilder 9) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Schärferes Kampfkraft-Symbol (v33, Stand 14.10.2026, ~12:15)

- **Problem:** Das aus dem Screenshot ausgeschnittene Logo ist nur 22×22 Pixel groß. Selbst mit Freistellen
  (transparenter Hintergrund) blieb es matschig.
- **Lösung:** ein eigenes **Vektor-Symbol** (`ClassIcons.CombatPowerVector`), keine Spielgrafik und daher auch in der
  Endnutzer-EXE: türkise Raute mit Schimmer, vier kurze Strahlen und ein leuchtender Stein in der Mitte. Es ist auf
  einem 24er-Raster gezeichnet und wird verzerrungsfrei in Zeilenhöhe skaliert.
- Eine eigene Bildvorlage (`Icons\kampfkraft.png`) wird nur noch genutzt, wenn sie mindestens **40 px** breit ist, dann
  mit hochwertiger Skalierung. Eine schärfere Vorlage bekommt man z. B. per Screenshot des Symbols im Charakterfenster
  bei größerer UI-Skalierung.
- Vorher/Nachher-Vergleich: `artifacts\vergleich-kampfkraft-v33.png`.

| Prüfung (~12:15) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **333/333**, App **13/13** (neu: Vektor-Symbol zeichnen und als Vorschau-PNG rendern), Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v33`, `artifacts/persoenlich-win-x64-v25` |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Klassen-Symbol unten auch im Leerlauf (v34, Stand 14.10.2026, ~12:25)

- **Problem:** In der Fußzeile stand „? (eigener Name)“, weil die Klasse bisher nur aus den Fertigkeiten im **aktuellen** Kampf
  kam. Im Leerlauf war sie unbekannt.
- **Lösung:**
  - Die Klasse kommt jetzt aus allen bisherigen Treffern dieser Sitzung (`LiveCombatMeter.ClassNameOf`).
  - Zusätzlich wird sie beim gemerkten eigenen Charakter gespeichert (`eigener-charakter.json`, neues Feld
    `ClassPrefix`, an den Namen gebunden) und gilt nach einem Neustart sofort, auch nach neuem Einloggen.
  - „?“ erscheint nur noch, wenn wirklich nichts bekannt ist. Die Datei des Besitzers bekommt die Klasse beim ersten
    Treffer mit v34.
- Die Klasse direkt aus 33 36 bzw. 02 97 zu lesen, ist **nicht** umgesetzt: Die Zuordnung der Klassen-Nummer dort ist
  nicht an Aufnahmen belegt.

| Prüfung (~12:25) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **334/334** (neu: gemerkte Klasse vor dem ersten Treffer, auch nach neuem Login; Treffer haben Vorrang), App **13/13**, Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v34`, `artifacts/persoenlich-win-x64-v26` |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### „Nächstes“ erst kurz vorher, DPS-Kurve, Wochenstatistik, Farbthemen, Kampfende-Karte aus (v35–v37, Stand 14.10.2026, ~12:50)

- **v35 – „Nächstes …“** erscheint erst **10 Min.** vor dem Event bzw. Weltboss (`OverlaySettings.ShowNextEventNow`).
  Einstellbar unter Event-Alarme → „‚Nächstes‘ erst ab“: 5/10/15/30 Min. oder „immer“. Erinnerungen mit Ton bleiben
  unverändert.
- **DPS-Kurve** im Spieler-Detailfenster (`DpsChart`, `DpsTimeline`):
  - DPS über die Kampfzeit, geglättet über 5 s, in der Klassenfarbe
  - beim Überfahren Zeit und DPS; oben links der Spitzenwert
  - live sowie für gespeicherte Kämpfe ab v36 (neues Feld `DamagePerSecond`; ältere Kämpfe haben keine Kurve)
- **Wochenstatistik** (Kämpfe → „📊 Wochenstatistik“, `WeeklyStatsWindow`):
  - Kacheln mit Kämpfen, Boss-Kämpfen, Ø und bester eigener DPS der letzten 7 Tage
  - Kurve der eigenen Ø-DPS je Tag, Liste je Tag, beste Kämpfe je Boss
  - Der Boss wird ab v36 beim Speichern eines Kampfs eingetragen (`FightRecord.BossName`, aus der Boss-Leiste).
  - Alles nur lokal.
- **Farbthemen** Violett, Türkis, Gold, Schlicht (Einstellungen → Anzeige; `ThemeColors`): Akzentfarbe für
  Overlay-Rand, „ICH“-Schild, Kampfende-Karte, Einstellungen und Statistik; sofort wirksam. Klassenfarben bleiben.
- **Kampfende-Karte** ist jetzt standardmäßig **aus** und lässt sich unter Einstellungen → Anzeige einschalten.
  Andere wiederkehrende Einblendungen nach Kämpfen gibt es nicht. Bei einem Weltboss-Alarm erscheint eine Zeile.
- **Ehrlich:** v36 (`endnutzer-win-x64-v36`, `persoenlich-win-x64-v28`) wurde veröffentlicht, bevor alle App-Tests grün
  waren. Zwei Tests scheiterten, weil die dunklen Stile zwischen Test-Threads geteilt wurden; im Programm gibt es nur
  einen Oberflächen-Thread. Seit **v37** hat jeder Thread eigene Stile, und alle Tests sind grün. v37 ist zu verwenden,
  nicht v36.

| Prüfung (~12:50) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **337/337** (neu: „Nächstes“-Vorlauf, Schaden je Sekunde/Glättung, Wochenstatistik, beste je Boss, Kampfende-Karte aus, Themen), App **14/14** (neu: Statistik-Fenster und Kurve aufbauen), Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v37`, `artifacts/persoenlich-win-x64-v29` |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Automatisch ausblenden, manueller Berserk entfernt (v38/v39, Stand 14.10.2026, ~13:05)

- **v38 – Automatisch ausblenden** (Einstellungen → Anzeige, Standard **aus**; 10/30/60/120 s ohne Kampf):
  - Das Overlay-Panel wird unsichtbar und fängt keine Klicks ab. Beim nächsten Kampf (eigene bzw. Gruppen-Treffer)
    oder bei einem Alarm kommt es von selbst wieder (`OverlaySettings.ShouldAutoHide`).
  - Das Fenster bleibt bestehen, Tastenkürzel und Tray-Symbol wirken weiter. Einstellungen und Detailfenster sind
    unabhängig.
- **v39 – manueller Berserk-Timer entfernt** (Wunsch des Besitzers):
  - Der Abschnitt in Einstellungen → Kampf ist weg. Dort steht nur noch der Hinweis, dass der Timer automatisch läuft.
  - Alte gespeicherte manuelle Zeiten werden beim Laden ignoriert.
  - Der automatische Timer aus 21 8D bleibt unverändert.

| Prüfung (~13:05) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **338/338** (neu: Ausblenden aus/30 s/nicht wählbar; manuelle Berserk-Zeiten ignoriert), App **14/14**, Npcap **31/31** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v39`, `artifacts/persoenlich-win-x64-v31` (v38/v30 nur mit dem Ausblenden) |
| Im Spiel | **nicht geprüft** (nichts gestartet) |

### Tode statt Pet-Anteil (v40, Stand 14.10.2026, ~16:40)

- „Pet x %“ in den Overlay-Zeilen entfernt (Pet-Schaden zählt weiter beim Besitzer, Detailfenster unverändert).
- Neu: Totenkopf (selbst gezeichnet) mit der Anzahl Tode im aktuellen Kampf, nur wenn > 0.
- Erkennung, selbst an der Aufnahme 20261013-172129 ermittelt:
  - **1B 92 = Gruppen-HP**: Kennung varint, HP varint, Max-HP varint, weitere Felder, letztes Byte 01 lebt / 00 tot.
    Beim Wipe (~413–417 s) alle Mitglieder HP 0 + 00, 3–7 s später wieder mit HP und 01. Eigener Charakter ist nicht dabei.
  - Eigene Tode über 00 8D mit HP 0. Die Nullen der anderen in 00 8D liegen zeitgleich mit den 1B-92-Toden.
  - Zwei Meldungen innerhalb von 5 s zählen als ein Tod; nur Spieler mit Namen.
- Ergebnis in der Aufnahme: 6–9 Tode je Spieler, beim Besitzer zur Bestätigung angefragt.

| Prüfung (~16:40) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **341/341** (neu: 1B-92-Leser, Todeszähler, kein Pet-Label), App **14/14**, Npcap **32/32** (neu: Todessuche an der Aufnahme) |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v40`, `artifacts/persoenlich-win-x64-v32` (PNG-Signaturen 193 / 202, +9 nur persönlich) |
| Im Spiel | **nicht geprüft** |

### Kontrast / Lesbarkeit (v41, Stand 14.10.2026, ~16:55)

- Einstellungen → Anzeige → **„Kontrast / Lesbarkeit“**: Normal, **Hoch** (Standard) oder Sehr hoch. Wirkt sofort und wird gespeichert.
  - **Hoch:** hellere Nebentexte (#C8CDD6) und Namen (#F0F2F6), dunkler Rand um die Schrift, Hintergrund mindestens 75 % deckend.
  - **Sehr hoch:** fast weiße Schrift, Hintergrund mindestens 90 % deckend.
  - **Normal:** wie früher.
- Bei Hoch und Sehr hoch sind die Nebenzeilen nur noch 1 pt kleiner statt 2.
- Gruppenhinweis gekürzt auf „Gruppe 1/5 erkannt“. Die Erklärung steht im Tooltip, und die Zeile wird bei Platzmangel mit „…“ abgeschnitten.
- Kontrastverhältnis (WCAG 2, `OverlayContrast.Ratio`) geprüft mit Regler ganz unten:
  - Hintergründe Weiß, Schwarz, Rot, Grün und Sand.
  - Hoch und Sehr hoch erreichen ≥ 4,5:1, Sehr hoch erreicht ≥ 7:1.

| Prüfung (~16:55) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **348/348**, App **14/14**, Npcap **32/32** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v41`, `artifacts/persoenlich-win-x64-v33` (PNG 193 / 202) |
| Im Spiel | **nicht geprüft** |

### Gruppenhinweis nicht mehr dauerhaft (v42, Stand 14.10.2026, ~17:10)

- „Gruppe x/y erkannt“ erscheint nur, wenn Namen fehlen, und nur im eigenen Kampf bzw. bis 60 s danach
  (`OverlayFormat.ShowPartyHint`). Abschaltbar unter Einstellungen → Anzeige → „Gruppenhinweis anzeigen“ (Standard an).
- Ein Gruppen-Austritt ist in den Aufnahmen nicht erkennbar. In 172129 gibt es nur volle 02-97-Listen und einmal einen
  8-Byte-Rahmen ohne Gruppenkopf. Die gemerkte Gruppe (`gruppe.json`) verfällt deshalb jetzt nach 2 h statt 6 h.
- Zusätzlich wird die gemerkte Gruppe verworfen, sobald ein Kampf von mindestens 20 s endet, in dem kein gemerktes
  Mitglied dabei war (`ForgetRememberedPartyAfterSoloFight`).
- Eine in dieser Sitzung vom Spiel geschickte Gruppenliste bleibt unberührt.

| Prüfung (~17:10) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **351/351**, App **14/14**, Npcap **32/32** |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v42`, `artifacts/persoenlich-win-x64-v34` (PNG 193 / 202) |
| Im Spiel | **nicht geprüft** |

### Boss-Karte rot, Kampfkraft bei einem Gruppenmitglied (v43, Stand 14.10.2026, ~17:25)

- Der Boss-Bereich (Name, Status/Kill/Berserk, HP-Leiste) sitzt in einer eigenen Karte: dunkler Hintergrund, abgerundet,
  dünner roter Rahmen in fester Farbe. Inhalt unverändert.
- Kampfkraft aus der Gruppenliste (02 97): In der Aufnahme 20261014-164636 (5er-Gruppe) fehlte sie bei einem
  Mitglied. In dessen Kennung stand eine andere Server-Nummer (00 09) als im Feld nach dem Ausrüstungswert (05 09),
  deshalb fand die Suche den Anker nicht.
  - Neu: Fehlt der Anker, gilt direkt der Aufbau nach dem Ausrüstungswert (Server u16, u16, Byte, Kampfkraft u64).
  - Ergebnis: Alle 5 Mitglieder haben jetzt eine Kampfkraft. Die Werte der anderen vier passen zum Overlay-Screenshot.
  - Die Aufnahme 172129 liefert unverändert dieselben Werte.

| Prüfung (~17:25) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **352/352**, App **14/14**, Npcap **32/32** (mit beiden Aufnahmen) |
| `dotnet publish` | `artifacts/endnutzer-win-x64-v43`, `artifacts/persoenlich-win-x64-v35` (PNG 193 / 202) |
| Im Spiel | **nicht geprüft** |

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

**Update 12.10.2026, ~23:39:** Laut Nutzer hat er Smart App Control selbst ausgeschaltet.
Das Projekt selbst ändert weiterhin keine Sicherheitseinstellungen. Der Abschnitt unten beschreibt den
Stand davor.

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

**Entscheidung (12.10.2026):** Es wird **kein** Code-Signing-Zertifikat gekauft; Smart App
Control bleibt eingeschaltet. Gründe: laufende Kosten; der günstige Microsoft-Signaturdienst
(Azure Artifact Signing) steht Privatpersonen in Deutschland nicht zur Verfügung; und auch eine
Signatur garantiert nicht, dass Smart App Control eine neue, noch unbekannte Datei sofort
zulässt. Es werden weiterhin keine Umgehungen versucht. Entwickelt und geprüft wird über die
Tests; ob eine Datei nach einem Build blockiert wird, wird jedes Mal ehrlich dokumentiert.

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

**Nachprüfung (12.10.2026, 01:05):** erneuter Build (0 Fehler) und `dotnet test`:
Core.Tests **54 von 54 bestanden**, Capture.Npcap.Tests **10 von 10 bestanden**
(darin die pcap/pcapng-Tests). Der Live-Test lief mit, hat aber ohne gesetzte
Umgebungsvariable nichts aufgenommen. Diesmal keine Sperre durch Smart App Control.

**Fenster mit Netzwerkdiagnose (12.10.2026, 01:15):** Das Hauptfenster zeigt jetzt
Status, Npcap-Version, Adapterauswahl, Start/Stopp/Neu laden sowie Pakete, Datenmenge,
Zeitraum und Rate (alle 0,5 s aktualisiert). Build 0 Warnungen/0 Fehler;
Core.Tests **69/69**, Capture.Npcap.Tests **10/10** bestanden, keine Sperre.
Das Fenster selbst wurde **nicht** gestartet und ist daher ungeprüft.

**Startversuch durch den Nutzer (12.10.2026, 01:09):** fehlgeschlagen. Smart App Control
blockierte `Aion2DpsMeter.App.dll` (Ereignis 3077 um 01:09:47/48); die App beendete sich
sofort mit `FileLoadException … 0x800711C7`. Es erschien kein Fenster. Zweiter Versuch
um 01:10:34: gleiches Ergebnis.

**Startfehler-Log (12.10.2026, 01:20):** Unerwartete Fehler werden jetzt nach
`%LOCALAPPDATA%\Aion2DpsMeter\startfehler.log` geschrieben und als Meldung angezeigt.
Das hilft, wenn z. B. `Core.dll` oder SharpPcap blockiert ist – **nicht** aber, wenn
`Aion2DpsMeter.App.dll` selbst blockiert ist, denn dann läuft kein eigener Code.
Build 0/0; Tests: Core 69/69, Capture.Npcap 10/10, neues `Aion2DpsMeter.App.Tests` 4/4,
keine Sperre (auch die neu gebaute `App.dll` wurde in den Tests geladen).

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

### Update per Klick, neue Versionsnummer 0.0.1 (Stand 14.10.2026, ~17:40)

- **Versionsschema:** eine Nummer für alles (SemVer) in `Directory.Build.props`, Start bei **0.0.1**. Sie steht in der
  EXE, in den Einstellungen und im Tooltip des Overlay-Titels.
- **Update-Prüfung** (Einstellung „Auf Updates prüfen“, Standard an):
  - 10 s nach dem Start und danach alle 6 h liest der Meter `api.github.com/repos/unbreakablelp0-beep/aion-2-dpsmeter/releases`.
    Er sendet dabei nichts außer der Anfrage.
  - Gewählt wird das höchste gültige SemVer-Release. Entwürfe, Vorabversionen und alte Tags wie „v43“ werden ignoriert.
  - Ist es neuer, erscheint im Overlay eine goldene, anklickbare Zeile.
- **Installation per Klick:**
  1. Rückfrage mit „Neu in dieser Version“.
  2. Download nur über HTTPS von github.com bzw. den GitHub-Asset-Hosts und nur aus diesem Repository.
  3. Prüfungen: Größe, Programmkopf „MZ“ und **SHA-256** (aus GitHubs `digest` oder der Datei `SHA256SUMS` im Release).
  4. Austausch: alte EXE → `.old`, neue an ihren Platz, Neustart. Der nächste Start räumt die `.old` weg.
  - Ohne Prüfsumme oder in der Entwicklungsumgebung öffnet der Meter nur die Release-Seite.
  - Die eigene EXE mit eingebetteten Icons wird ebenfalls ersetzt. Die Icons kommen danach aus `Dokumente\Aion2DpsMeter\Icons`.
- **Release-Ablauf:**
  1. Version in `Directory.Build.props` erhöhen.
  2. `publish-release.ps1` ausführen; es erzeugt `artifacts\<Version>\release\Aion2DpsMeter-<Version>-win-x64.exe`
     und `SHA256SUMS`.
  3. Auf GitHub ein Release mit Tag `v<Version>` anlegen und beide Dateien anhängen.
  - Die eigene EXE mit Icons baut `publish-persoenlich.ps1 -Output artifacts\<Version>\eigene`. Sie wird nie veröffentlicht.

| Prüfung (~17:40) | Ergebnis |
|---|---|
| Voller `dotnet build` | **0 Warnungen, 0 Fehler** |
| Tests | Core **360/360** (neu: SemVer, Release-Auswahl, Hosts, Prüfsummen, Austausch), App **14/14**, Npcap **32/32** |
| Builds | `artifacts/0.0.1/release` (ohne Spielgrafiken, PNG 193), `artifacts/0.0.1/eigene` (mit Icons, PNG 202) |
| Im Spiel / echtes Update | **nicht geprüft** (es gibt noch kein 0.0.x-Release; nichts gestartet) |