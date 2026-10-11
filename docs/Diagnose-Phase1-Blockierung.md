# Diagnosebericht: Blockierung in Phase 1

Datum: 12.10.2026. Es wurden keine Sicherheitseinstellungen geändert und keine Umgehung versucht.

## Ergebnis in einem Satz

Windows **Smart App Control** blockiert genau **eine** Datei: die selbst gebaute,
nicht signierte Bibliothek `Aion2DpsMeter.Core.dll`. Alle anderen Dateien werden geladen.

## 1. Welche Datei wird blockiert?

| Datei | Signiert? | Blockiert? |
|---|---|---|
| `Aion2DpsMeter.Core.dll` (in `src/.../App/bin` und `tests/.../bin`) | nein | **ja** |
| `Aion2DpsMeter.App.exe` (App-EXE) | nein | nein – startet |
| `Aion2DpsMeter.App.dll` | nein | nein – wird geladen |
| `Aion2DpsMeter.Core.Tests.dll` (Testdatei) | nein | nein – wird geladen |
| `testhost.exe`, `xunit.core.dll` (Microsoft/xUnit) | ja (gültig) | nein |

Blockiert wird also weder die Testdatei noch die App-EXE, sondern die Core-Bibliothek,
sobald ein Programm (App-EXE oder `testhost.exe`) sie laden will.

## 2. Ereignisprotokoll

Protokoll: *Anwendungs- und Dienstprotokolle → Microsoft → Windows → CodeIntegrity → Operational*

- **Ereignis-ID 3077** (Sperre):
  „Code Integrity determined that a process (…\testhost.exe) attempted to load
  …\tests\Aion2DpsMeter.Core.Tests\bin\Debug\net10.0\Aion2DpsMeter.Core.dll that did not meet
  the Enterprise signing level requirements or violated code integrity policy
  (Policy ID:{0283ac0f-fff1-49ae-ada1-8a933130cad6}).“
  - PolicyName = `VerifiedAndReputableDesktop` (= Smart App Control), PolicyID = 27555.1000.240208
  - Requested Signing Level = 2, Validated Signing Level = 1 (Datei ist nicht signiert)
  - Gleiches Ereignis für Prozess `Aion2DpsMeter.App.exe` → `…\App\bin\Debug\net10.0-windows\Aion2DpsMeter.Core.dll`
- **Ereignis-ID 3033**: gleicher Text, „did not meet the Enterprise signing level requirements“.
- **Ereignis-ID 3118** („Smart App Control Block Details“) für `Aion2DpsMeter.Core.dll`:
  - Beim ersten Versuch fragte Windows die Microsoft-Cloud nach dem Ruf der Datei
    (`DefenderMadeCloudCall = true`); Ergebnis: nicht vertrauenswürdig
    (`DefenderTrust = -16777216`, kein Virenfund, `DefenderThreatName` leer).
  - Danach wird dieses Ergebnis zwischengespeichert (`DefenderMadeCloudCall = false`).
- Status: `SmartAppControlState = On`. Echtzeitschutz und Cloud-Schutz (MAPS) sind an.

Bedeutung: Kein Virus. Die Datei ist neu, unsigniert und Microsoft unbekannt –
genau das lässt Smart App Control nicht zu.

## 3. Einzeln geprüft

| Schritt | Ergebnis |
|---|---|
| `dotnet build --no-incremental` | **erfolgreich**, 0 Warnungen, 0 Fehler, Exitcode 0 |
| `dotnet test` | **fehlgeschlagen**, 1 von 1 Test. `System.IO.FileLoadException … Aion2DpsMeter.Core.dll. Eine Anwendungssteuerungsrichtlinie hat diese Datei blockiert. (0x800711C7)` |
| `dotnet run --project src/Aion2DpsMeter.App` | **Absturz** beim Öffnen des Fensters: `XamlParseException` → innen dieselbe `FileLoadException` für `Aion2DpsMeter.Core.dll` (MainWindow.xaml.cs, Zeile 11) |

## 4. Test ohne Ausführen der blockierten Datei?

- `dotnet test --list-tests` funktioniert (Exitcode 0) und findet den Test
  `CaptureStatusTests.NotConfigured_HasExpectedStateAndGermanMessage`.
  Das zeigt: Testprojekt und Testumgebung sind in Ordnung.
- Ein echter Testlauf ist **nicht** möglich, ohne `Aion2DpsMeter.Core.dll` zu laden –
  der Test prüft ja genau Code aus dieser Datei.
- Die Build-Ausgabe ist tatsächlich fehlerfrei (siehe oben).

## Nachtrag (12.10.2026, 00:12) – nach Einbau der DPS-Engine

Nach dem Neubau haben die Dateien neue Inhalte und damit neue Hashwerte. Ergebnis:

- Die neue `Aion2DpsMeter.Core.dll` (SHA256 `8AB49AC2…7B0`) wurde **nicht** blockiert:
  `dotnet test` lief vollständig, **13 von 13 Tests bestanden**, kein Sperreintrag.
- Stattdessen wurde jetzt die neue `Aion2DpsMeter.App.dll` blockiert
  (Ereignisse 3077/3033/3118 um 00:12:14). Die App stürzt beim Start mit
  `FileLoadException … Aion2DpsMeter.App.dll … Anwendungssteuerungsrichtlinie (0x800711C7)` ab.

Folgerung: Smart App Control entscheidet **je Datei-Hash** (Cloud-Bewertung). Welche
der unsignierten Dateien blockiert wird, kann sich nach jedem Build ändern und ist
nicht vorhersagbar. Ob Tests bzw. App laufen, muss deshalb nach jedem Build neu
geprüft werden.

## Fazit

Der Code und der Build sind in Ordnung. Die Ursache ist allein Smart App Control,
das die selbst gebaute, unsignierte `Aion2DpsMeter.Core.dll` blockiert.
Warum die ebenfalls unsignierten Dateien `App.exe`, `App.dll` und `Core.Tests.dll`
nicht blockiert werden, lässt sich aus dem Protokoll nicht sicher sagen – es gibt
für sie schlicht keinen Sperreintrag.
