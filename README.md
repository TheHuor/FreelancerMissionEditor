# Freelancer Mission Editor

Werkzeug zum Ansehen, Prüfen und Bearbeiten von Missionsskripten für **Freelancer** (2003) und Mods
wie **Crossfire**: die Missions-INIs mit `[Trigger]`, `[MsnShip]`, `[MsnFormation]`, `[ObjList]`,
`[NNObjective]`, `[Dialog]` usw.

Der Editor liest die Datei so, wie das Spiel (`content.dll`) sie liest, zeigt den Ablauf der Trigger als
Graph und findet typische Skriptfehler: Verweise auf Trigger, die es nicht gibt, doppelte Namen, Zeilen,
die das Spiel still übergeht. Zu jedem Fund steht, was im Spiel passiert und wie man es behebt.
Änderungen werden verlustfrei gespeichert: Alles, was nicht geändert wurde, bleibt Byte für Byte gleich.

Entstanden beim Aufteilen der Crossfire-Kampagne (ein 36 000-Zeilen-Skript) in einzelne Kapitel.

## Funktionen

- **Laden ohne Verlust**: Text-INIs in Windows-1252 (oder UTF-8 mit BOM) sowie binäre BINI-v1-Dateien; Kommentare, Leerzeilen,
  Abstände und Zeilenenden bleiben erhalten.
- **Trigger-Liste** mit Filter, Fundsymbolen und dem Filter „Nur Trigger mit Funden“.
- **Trigger-Graph** um den gewählten Trigger: Schichten von links nach rechts, Kanten für
  `Act_ActTrig`, `Cnd_WatchTrigger`, `Act_Save` und wahlweise `Act_DeactTrig`; Tiefe einstellbar,
  verschieben durch Ziehen, zoomen mit dem Mausrad, Knoten unter der Maus heben ihre Kanten hervor.
- **Details**: Funde des Triggers mit Erklärung, Verweise in beide Richtungen (anklickbar),
  Quelltext-Vorschau mit Zeilennummern und markierten Fundzeilen; bei doppelten Definitionen beide.
- **Prüfliste** aller Funde; Doppelklick springt zur Stelle.
- **Bearbeiten**: Abschnitt im Textfenster ändern oder löschen (die Rückfrage sagt, was sich im Spiel
  ändert), danach wird sofort neu geprüft; Rückgängig (Strg+Z), Speichern (Strg+S) mit `.bak`-Sicherung
  beim ersten Speichern.
- **Kommandozeile** für Skripte und Stapelprüfungen.
- **MCP-Server**, über den KI-Agenten Missionen untersuchen und Änderungen als Entwurf vorschlagen.

## Voraussetzungen

- Windows (die Oberfläche ist WPF; Kern und Kommandozeile laufen auch anderswo)
- [.NET SDK 10](https://dotnet.microsoft.com/download)

## Bauen und starten

```
dotnet build MissionEditor.slnx
dotnet test MissionEditor.slnx
dotnet run --project src/MissionEditor.App -- <datei.ini> [trigger]
```

Der optionale zweite Parameter wählt beim Start einen Trigger aus. Dateien lassen sich auch über
„Öffnen…“ (Strg+O) laden oder ins Fenster ziehen.

### Bedienung

| Was | Wie |
|---|---|
| Trigger suchen | Strg+F, Text eingeben |
| Zum vorherigen Trigger | „◀ Zurück“ oder Alt+← |
| Graph verschieben / zoomen | auf freier Fläche ziehen / Mausrad (Umschalt+Mausrad scrollt waagerecht), „Einpassen“, „100 %“ |
| Fund ansehen | Doppelklick in der Prüfliste, Klick auf das Fundsymbol in der Liste, Doppelklick auf eine markierte Zeile in der Vorschau |
| Abschnitt ändern | „Bearbeiten“ über dem Abschnitt in der Vorschau, Übernehmen mit Strg+Enter |
| Abschnitt löschen | „Löschen“ über dem Abschnitt |
| Rückgängig / Speichern | Strg+Z / Strg+S |

Symbole: roter Kreis mit ✕ = Fehler, gelbes Dreieck mit ! = Warnung, grauer Kreis mit i = Hinweis.

### Kommandozeile

```
dotnet run --project src/MissionEditor.Cli -- check <datei.ini> [--min info|warning|error] [--hints]
dotnet run --project src/MissionEditor.Cli -- roundtrip <datei.ini>
dotnet run --project src/MissionEditor.Cli -- stats <datei.ini>
dotnet run --project src/MissionEditor.Cli -- graph <datei.ini> <trigger> [--back n] [--forward n] [--deact]
dotnet run --project src/MissionEditor.Cli -- hash <nickname>
```

- `check` gibt Funde als `datei:zeile: Schwere [prüfung] Meldung` aus, mit `--hints` samt Erklärung;
  Exitcode 1 bei Fehlern.
- `roundtrip` lädt und schreibt die Datei im Speicher und vergleicht die Bytes.
- `graph` gibt die Umgebung eines Triggers als Graphviz-DOT aus.
- `hash` berechnet die CreateID eines Nicknames (wie sie in Spielständen steht).

## Prüfungen

| Id | Findet |
|---|---|
| `unknown-trigger` | `Act_ActTrig`, `Act_DeactTrig`, `Cnd_WatchTrigger`, `Act_Save` auf einen Trigger, den es nicht gibt – mit Vorschlag ähnlicher Namen |
| `duplicate-nickname` | doppelte Namen bei Triggern, `[NPC]`, `[ObjList]`, `[Dialog]`, `[NNObjective]`, `[MsnShip]`/`[MsnFormation]`, `[MsnSolar]`, `[MsnLoot]`, `[MsnRandEnc]`; Namen, die sich nur in der Groß-/Kleinschreibung unterscheiden |
| `trigger-structure` | `[Trigger]` ohne `nickname`, ohne oder mit mehreren Bedingungen, Zeilen vor `nickname`, falsch geschriebene `Cnd_`/`Act_`-Schlüssel |

## Wie das Spiel die Datei liest

Die Regeln stammen aus einer Analyse von `content.dll` und `common.dll`:

- `;` beendet eine Zeile; Werte werden an `,` getrennt; `[;Name]` ist eine auskommentierte Sektion.
- Schlüssel und Sektionsnamen werden ohne Groß-/Kleinschreibung verglichen. Trigger-IDs sind die
  CreateID des Nicknames, ebenfalls ohne Groß-/Kleinschreibung.
- In einer `[Trigger]`-Sektion liest das Spiel erst ab `nickname`; alles davor wird übersprungen.
  Die letzte `Cnd_*` gilt (es gibt kein UND), ein Trigger ohne Bedingung bringt das Spiel beim
  Aktivieren zum Absturz, unbekannte `Cnd_`/`Act_`-Schlüssel werden still ignoriert.
- Doppelte Namen:

  | Sektion | Verhalten |
  |---|---|
  | `[Trigger]`, `[NPC]`, `[ObjList]`, `[Dialog]` | die erste Definition gilt, weitere werden beim Laden verworfen |
  | `[MsnShip]`/`[MsnFormation]` (eine gemeinsame Liste), `[MsnSolar]`, `[MsnLoot]` | `Act_Spawn*` sucht den Namen exakt und nimmt den ersten Treffer |
  | `[MsnRandEnc]` | `Act_EnableEnc`/`Act_DisableEnc` schalten alle Einträge mit dem Namen |
  | `[NNObjective]` | `Act_SetNNObj` setzt den Zustand bei allen, macht aber nur das erste Ziel sichtbar |

- `Act_ActTrig`/`Act_DeactTrig` auf einen unbekannten Trigger tun nichts, ohne Fehlermeldung.

Graph-Kanten zeigen in Ablaufrichtung: `Act_ActTrig`, `Act_DeactTrig` und `Act_Save` vom Trigger zum
Ziel, `Cnd_WatchTrigger` vom beobachteten Trigger zum Beobachter.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `src/MissionEditor.Core` | INI-Leser ohne Verlust (`IniDocument`), Modell (`MissionScript`, `Trigger`, Verweise), Trigger-Graph und Schichten-Anordnung, `FlHash.CreateId`, Prüfungen |
| `src/MissionEditor.Cli` | Kommandozeile |
| `src/MissionEditor.App` | WPF-Oberfläche |
| `src/MissionEditor.Mcp` | MCP-Server für externe KI-Agenten (stdio) |
| `tests/MissionEditor.Tests` | xUnit-Tests für den Kern |
| `tests/MissionEditor.Mcp.Tests` | xUnit-Tests für den MCP-Server, inklusive echtem Stdio-Client |

Neue Prüfungen implementieren `IMissionCheck` und werden in `MissionChecker.AllChecks` eingetragen.

## MCP-Server für KI-Agenten

`MissionEditor.Mcp` macht den Kern des Editors für KI-Agenten nutzbar, die das
[Model Context Protocol](https://modelcontextprotocol.io) sprechen (z. B. Claude Code, Claude Desktop,
Codex). Ein Agent kann damit Missionen untersuchen, prüfen, Triggerabläufe nachverfolgen und
Änderungen als Entwurf vorschlagen. Der Server läuft lokal über stdio; er hat keinen
Netzwerkzugang, braucht keine API-Schlüssel und enthält kein eigenes Sprachmodell.

### Sicherheit

- Der Server sieht nur das mit `--workspace` freigegebene Verzeichnis. Pfade außerhalb davon,
  symbolische Links und andere Dateitypen als `.ini` werden abgewiesen; Dateien über 8 MiB auch.
- Standardmäßig wird nur gelesen. Änderungen entstehen als Entwürfe im Arbeitsspeicher
  (höchstens acht pro Sitzung) mit Vorher/Nachher und den Befunden des Entwurfs.
- Erst mit `--allow-writes` darf ein Entwurf exportiert werden – und zwar nur in eine **neue**
  Datei. Bestehende Dateien und Originalmissionen werden nie überschrieben.
- Sektionsänderungen verlangen den SHA256 der gelesenen Fassung. Hat sich die Datei seitdem
  geändert, werden Entwurf und Export abgelehnt.
- BINI bleibt BINI; Zeichen, die die Kodierung der Datei nicht kennt, werden abgewiesen.

### Tools

| Tool | Zweck |
|---|---|
| `inspect_mission` | Format, SHA256, Anzahl Zeilen/Sektionen/Trigger/Objekte, Fehler und Warnungen |
| `check_mission` | Befunde mit Zeile, Erklärung und Abhilfe (paginiert) |
| `list_triggers` | Trigger mit Sektionsindex, Zeile, Anfangszustand, Bedingung; Duplikate markiert |
| `list_sections` | alle Sektionen mit Index, Name, nickname und Zeilenbereich |
| `read_mission_source` | bis zu 500 Quelltextzeilen |
| `get_trigger_context` | Umgebung eines Triggers mit Bedingungen, Aktionen und Verweisen |
| `suggest_missing_trigger` | ähnliche Namen und aufrufende Stellen für einen fehlenden Trigger |
| `get_mission_vocabulary` | die `Cnd_`/`Act_`-Namen, die das Spiel kennt |
| `prepare_section_edit` | Sektionsänderung als Entwurf |
| `prepare_new_mission` | vom Agenten geschriebene neue Mission als Entwurf |
| `read_mission_draft` / `discard_mission_draft` | Entwurf lesen oder verwerfen |
| `export_mission_draft` | Entwurf als neue Datei schreiben (nur mit `--allow-writes`) |

### Einrichten

Am einfachsten über den Editor: **„KI-Agent verbinden…“** in der Werkzeugleiste. Der Dialog
findet die `MissionEditor.Mcp.exe` (neben dem Editor, in einem daneben entpackten Release-Ordner
`mcp` oder in der Build-Ausgabe) und schlägt den Ordner der geöffneten Mission als Workspace vor.
„Prüfen“ startet den Server testweise; der Schalter „Export erlauben“ ergänzt `--allow-writes`.

Je Client (Claude Code, Claude Desktop, Codex) zeigt der Dialog, ob der Server dort schon
eingetragen ist. **„Hinzufügen“** trägt ihn direkt ein bzw. aktualisiert den Eintrag:

| Client | Datei |
|---|---|
| Claude Code (Kommandozeile und Desktop-App) | `%USERPROFILE%\.claude.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Codex | `%USERPROFILE%\.codex\config.toml` |

Nur der Eintrag `freelancer-missions` wird angelegt oder ersetzt, alles andere in der Datei bleibt.
Die vorige Fassung bleibt als `<datei>.mission-editor.bak` erhalten; ist die Datei kein gültiges
JSON, wird nichts geändert. Den Client am besten vorher schließen und danach neu starten.

Als Alternativen zeigt der Dialog den Befehl `claude mcp add …` (braucht die Claude-Code-
Kommandozeile; der Dialog sagt, ob sie installiert ist) und den Text zum Einfügen von Hand. Die
Anleitung dafür richtet sich nach dem Zustand der Datei: neu anlegen, `"mcpServers"` ergänzen oder
nur den Eintrag in ein vorhandenes `"mcpServers"` einfügen. Hat die geöffnete Mission
ungespeicherte Änderungen, weist der Dialog darauf hin, denn der Agent sieht nur den gespeicherten
Stand. Pfad und Export-Wahl merkt sich der Editor.

Von Hand bauen bzw. starten:

```
dotnet build src/MissionEditor.Mcp -c Release
dotnet run --project src/MissionEditor.Mcp -- --workspace <Missionsverzeichnis> [--allow-writes]
```

Ein Release enthält zusätzlich `MissionEditorMcp-<version>-win-x64.zip` mit
`MissionEditor.Mcp.exe`, das ohne installiertes .NET läuft.

Claude Code:

```
claude mcp add freelancer-missions -- <Pfad>\MissionEditor.Mcp.exe --workspace "D:\freelancer crossfire\DATA\MISSIONS"
```

Claude Desktop (`claude_desktop_config.json`) bzw. andere Clients mit derselben Struktur:

```json
{
  "mcpServers": {
    "freelancer-missions": {
      "command": "C:\\Tools\\MissionEditorMcp\\MissionEditor.Mcp.exe",
      "args": ["--workspace", "D:\\freelancer crossfire\\DATA\\MISSIONS"]
    }
  }
}
```

Codex (`~/.codex/config.toml`):

```toml
[mcp_servers.freelancer-missions]
command = "C:\\Tools\\MissionEditorMcp\\MissionEditor.Mcp.exe"
args = ["--workspace", "D:\\freelancer crossfire\\DATA\\MISSIONS"]
```

Für Exporte `"--allow-writes"` an die Argumente anhängen. Ohne installierte exe geht statt
`command` auch `dotnet` mit dem Pfad zur `MissionEditor.Mcp.dll` als erstem Argument.

Der Server arbeitet mit gespeicherten Dateien; ungespeicherte Änderungen im Editor sieht er nicht.
Namensvorschläge für fehlende Trigger beruhen nur auf Ähnlichkeit und sind kein Beleg für die
beabsichtigte Funktion.

## Mitarbeit

`master` ist geschützt: Änderungen kommen über Pull Requests, der Build mit Tests
(GitHub Actions, `.github/workflows/build.yml`) muss grün sein. Code-Kommentare auf Englisch mit
XML-Doku, Texte für Benutzer auf Deutsch; Formatierung nach `.editorconfig`.

## Geplant

- IDS-Texte (Titel, Ziele, Dialoge) aus den Ressourcen-DLLs anzeigen
- weitere Prüfungen: nie aktivierte Trigger, Schiffsnamen in Spawn-Aktionen, Mehrfach-Aktivierung von
  Spawns, Story-Fortschritt nur über Handelsstraßen, leere Zieltexte
- mehrere Dateien gemeinsam prüfen (z. B. alle Kapitel einer Kampagne)
- Test-Spielstände erzeugen

## Oberfläche und Layoutprüfung

Die WPF-Oberfläche verwendet ein dunkles, von Freelancer inspiriertes Theme. Rechts teilen sich
Funde und Verweise einen Registerbereich; darunter bleibt der horizontal verschiebbare Quelltext sichtbar.
Die Prüfergebnisse unten lassen sich einklappen. Ein Sprung zu einem Fund öffnet sie wieder.
Die Kantentypen im Graphen unterscheiden sich zusätzlich durch Linienmuster.

Zusätzliche WPF-Smokechecks für einen interaktiven Windows-Desktop:

```powershell
dotnet run --project tests/MissionEditor.App.SmokeChecks
```

Die Checks öffnen kurz ein Fenster, verwenden die Repository-Beispielmission und prüfen unter anderem
Fenstergrößen, Prüfleisten-Navigation, Kontraste sowie Bearbeiten/Rückgängig/Speichern auf einer
temporären Kopie. Die Fixture bleibt unverändert. Diese Checks sind nicht Teil von `dotnet test`.

### Binäre Missionsdateien (BINI)

BINI v1 wird anhand der Signatur automatisch erkannt. Öffnen, Bearbeiten, Löschen, Rückgängig,
Speichern und Speichern unter behalten das ursprüngliche Format bei: BINI bleibt BINI, Text bleibt
Text. Unveränderte BINI-Dateien werden byteidentisch geschrieben; nach Änderungen bleiben die
Datentypen und Werte unveränderter Parameter erhalten. Die Statusleiste kennzeichnet BINI.

Die Quelltextansicht einer BINI-Datei ist eine editierbare Darstellung mit synthetischen
Zeilennummern. Strings stehen in doppelten Anführungszeichen; Escape-Sequenzen entsprechen JSON
(z. B. `\n`, `\"`, `\\`). So bleiben Kommas und Semikolons innerhalb eines Strings erhalten.
Neue unquotierte Ganzzahlen beziehungsweise Dezimalzahlen werden als BINI-Zahlen gespeichert;
Zahlen außerhalb des unterstützten Wertebereichs bleiben Strings. Kommentare haben in BINI
keine Repräsentation und werden vor dem Übernehmen mit einer Meldung abgewiesen. Die Eingabe
bleibt im Bearbeitungsdialog erhalten. Die ursprüngliche `.bak`-Sicherung bleibt ebenfalls binär.

Eine unabhängig gepackte kleine BINI-Beispielmission liegt unter
`tests/Fixtures/Missions/bini-sample.ini`. Codec-Tests verwenden zusätzlich unabhängig erzeugte
Bytefolgen einschließlich beschädigter Daten; WPF-Smokechecks prüfen den tatsächlichen
Editor-Speicherpfad einschließlich Save As, Backup und Undo.

### Fenster und Grafik

Eine dunklere eigene Titelleiste integriert Icon und Fensterbuttons ins Theme. Native
Fensterfunktionen (Verschieben, Größenänderung und Maximieren) bleiben über WPF WindowChrome
verfügbar; Schließen verwendet weiterhin die bestehende Rückfrage für ungespeicherte Änderungen.
Der Graph zeigt ein dezentes Sternenfeld mit Nebel. Assets und ihre Erzeugung sind unter
`src/MissionEditor.App/Assets/README.md` dokumentiert.

## Versionierung und Releases

Alle Projekte (GUI, Core, CLI, MCP und Tests) erhalten ihre Version zentral über MinVer 8.0.0 in
`Directory.Build.props`. Die gemeinsame Repository-Version berücksichtigt jeden neuen Commit,
auch wenn nur Core oder CLI verändert wurden. Ohne Release-Tag entsteht beispielsweise
`0.1.0-preview.0.15`; der nächste Commit erhöht den Entwicklungsstand. Die vollständige
Assembly-Produktversion enthält zusätzlich die Git-Revision. Die numerische Assembly-/Dateiversion
folgt MinVers Standard; Entwicklungsversionen unterscheiden sich über die Produktversion.

Versionen werden beim Build berechnet, ohne zusätzliche Versionscommits oder Git-Hooks.
Erneutes Pushen oder Bauen desselben Commits ändert seine Version nicht. Die GUI zeigt die
semantische Version im Titel. Unter „Über…“ stehen GUI-, Core- und gemeinsame CLI-Build-Version,
Laufzeit und Referenzen; „Build-Infos kopieren“ übernimmt die vollständigen Versionsangaben.

`build.yml` läuft bei Branch-Pushes und Pull Requests, lädt die vollständige Git-Historie und
stellt versionierte Build-Artefakte bereit. Diese CI-Artefakte benötigen die passende .NET-Laufzeit.

Ein Release wird durch einen SemVer-Tag mit Präfix `v` ausgelöst:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` baut und testet den getaggten Commit, veröffentlicht GUI, CLI und MCP-Server für
Windows x64 mit enthaltener .NET-Laufzeit und erstellt drei ZIPs sowie SHA-256-Prüfsummen. Es prüft, dass
Tag und berechnete MinVer-Version übereinstimmen und alle ausgelieferten Module dieselbe
Produktversion haben. Bei Tags wie `v0.2.0-rc.1` wird das GitHub-Release als Vorabversion markiert.
Die benötigten Rechte kommen vom eingebauten `GITHUB_TOKEN`; ein eigener PAT ist nicht nötig.
Manuell auf einem Branch gestartet erzeugt der Workflow nur Artefakte, kein GitHub-Release.

Der Verpackungsschritt lässt sich lokal ausführen:

```powershell
./.github/scripts/Publish-Release.ps1
```

Die Ausgaben liegen im ignorierten Verzeichnis `artifacts/`. Für eine andere Ausgabestelle kann
`-OutputDirectory` angegeben werden. Die ZIPs enthalten jeweils den Inhalt des entsprechenden
GUI-, CLI- bzw. MCP-Verzeichnisses und können ohne vorherige .NET-Installation entpackt gestartet werden.
