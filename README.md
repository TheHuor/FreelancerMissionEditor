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
| Ablauf durchspielen | „▶ Durchspielen“: aktive Trigger von Hand auslösen, `Cnd_True` löst sofort aus; das Protokoll zeigt Aktivierungen, Abschaltungen, Spawns, Ziele, Speicherpunkte und das Missionsende. Bleibt nichts aktiv ohne Ende, sagt das Fenster, dass die Mission hier stehen bleibt. Bedingungen selbst werden nicht ausgewertet |

Symbole: roter Kreis mit ✕ = Fehler, gelbes Dreieck mit ! = Warnung, grauer Kreis mit i = Hinweis.

### Spieldaten

Nicknames (Prüfung `unknown-game-nickname`) und Texte kommen aus einer Freelancer-Installation – dem Ordner
mit `EXE\freelancer.ini`. Gelesen werden die Dateien, die dort unter `[Data]` (universe, groups, ships,
solar, loadouts, equipment) und `[Resources]` stehen; das passiert beim ersten Prüfen und bleibt bis zu
einer Änderung an `freelancer.ini` im Speicher. Welche Installation gilt, steht rechts in der Statusleiste
(„Spieldaten: … (53 Systeme, 197 Basen, …)“). Über **„Spielordner…“** daneben:

- **Automatisch** (Standard): die Installation, in der die geöffnete Mission liegt – der Editor geht vom
  Ordner der Datei aufwärts bis zu einem Ordner mit `EXE\freelancer.ini`.
- **Fester Ordner**: für mehrere Spielversionen nebeneinander oder Missionen außerhalb des Spielordners.
  Zuletzt benutzte Ordner stehen zur Auswahl. Die Wahl gilt auch für den MCP-Server, wenn er über
  „KI-Agent verbinden…“ eingetragen wird (`--game <Ordner>`), und die Kommandozeile kennt `check … --game <Ordner>`.

Ohne Installation entfallen diese Prüfung und die Texte; alles andere funktioniert wie gewohnt.

Mit Spieldaten zeigt die Vorschau hinter Zeilen mit Text-IDs grau den
Text dazu – Namen (`individual_name`, `string_id`), Ziele (`[NNObjective] type = ids, …` bzw.
`rep_inst`), `Act_SetTitle`, `Act_SetOffer`, `Act_NNIds`, den Sprecher von `Act_EtherComm` und Popups.
Die Texte kommen aus `resources.dll` und den DLLs unter `[Resources]` in `freelancer.ini`
(Index = IDS >> 16), in der Sprache der Installation. Der MCP-Server liefert sie in
`read_mission_source` als `note` mit.

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
| `unknown-trigger` | `Act_ActTrig`, `Act_DeactTrig`, `Cnd_WatchTrigger`, `Act_Save` auf einen Trigger, den es nicht gibt – mit Vorschlag ähnlicher Namen. Ist der Name eine `[ObjList]` o. Ä., sagt die Meldung das; ein vertippter `Act_DeactTrig`, dessen gemeinter Trigger weiter spawnt oder wiederholt, ist ein Fehler |
| `duplicate-nickname` | doppelte Namen bei Triggern, `[NPC]`, `[ObjList]`, `[Dialog]`, `[NNObjective]`, `[MsnShip]`/`[MsnFormation]`, `[MsnSolar]`, `[MsnLoot]`, `[MsnRandEnc]`; Namen, die sich nur in der Groß-/Kleinschreibung unterscheiden |
| `trigger-structure` | `[Trigger]` ohne `nickname`, ohne oder mit mehreren Bedingungen, Zeilen vor `nickname`, falsch geschriebene `Cnd_`/`Act_`-Schlüssel |
| `unreachable-trigger` | Trigger, die nie aktiv werden (nicht `InitState = ACTIVE`, kein `Act_ActTrig`/`Act_Save` aus einem laufenden Trigger). Hinweis; Warnung, wenn ein laufender Trigger per `Cnd_WatchTrigger` auf ihn wartet und deshalb hängt. Fehlt jeder Starttrigger, gibt es eine Warnung |
| `unknown-object` | `Act_SpawnShip`/`SpawnShipRel`/`SpawnFormation`/`SpawnSolar`/`SpawnLoot` auf einen Namen, den es nicht gibt oder nur in anderer Schreibweise (exakter Vergleich wie im Spiel); unbekannte `[ObjList]` (`Act_GiveObjList`, Spawn, `init_objectives`; `no_ol` zählt nicht), `[Dialog]` (`Act_StartDialog`) und `[NNObjective]` (`Act_SetNNObj`/`SetNNState`/`SetNNHidden`) |
| `impossible-condition` | `Cnd_Destroyed` auf ein Schiff, eine Formation, eine Station oder ein Label dieser Datei, das nie gespawnt wird – der Trigger löst nie aus |
| `unused-object` | Schiffe, Formationen, Stationen und Beute, die nie gespawnt werden, nie vergebene Befehlslisten, nie gestartete Dialoge, nie gesetzte Ziele (Hinweis) |
| `mission-end` | kein `Act_ChangeState = SUCCEED` (Hinweis) oder nur in Triggern, die nie aktiv werden (Warnung) |
| `destroyed-counter` | `Cnd_Destroyed = X, N` zählt erst ab der Aktivierung (die Engine setzt den Zähler dann auf 0): gemeldet, wenn ein Trigger die Ziele spawnt und erst ein, zwei Trigger später den Zähltrigger aktiviert – frühe Abschüsse zählen nie (Hinweis, wenn die Ziele bis dahin getarnt sind); außerdem N größer als die Zahl der gespawnten Objekte |
| `vulnerable-escort` | ein Trigger wartet auf ein Missionsschiff (`Cnd_DistVec`, `Cnd_DistShip` ohne Spieler, `Cnd_TLEntered`/`Exited`, `Cnd_LaunchComplete`), aber weder `Act_Invulnerable` (beim Spawn oder danach) noch ein `Cnd_Destroyed`-Trigger in diesem Abschnitt fängt seinen Tod ab |
| `save-point` | `Act_Save` im Weltraum, dessen Speicherziel keine Startposition setzt (`Act_SetInitialPlayerPos`); `Act_NNPath`/`Act_PopUpDialog` in `Cnd_True`-Triggern, die beim Laden sofort laufen und dann verloren gehen |
| `cancelled-activation` | `Act_ActTrig = X` und danach `Act_DeactTrig = X` im selben Trigger – `Act_DeactTrig` löscht auch die vorgemerkte Aktivierung, X wird nie aktiv |
| `unknown-game-nickname` | nur mit Spieldaten (siehe „Spieldaten“: fest eingestellter Spielordner oder die Installation, in der die Mission liegt): Fraktionen (`affiliation`, `faction`, `Act_SetRep`), Systeme und Basen (`[MsnSolar]`, `Cnd_BaseEnter`/`Exit`, `Act_ForceLand`), Stationstypen, Loadouts, Schiffstypen (`Act_SetShipAndLoadout`), Beute-Archetypen und `npc_ship_arch` (eigene `npc_ship_file`, dann `missions\npcships.ini`), die das Spiel nicht kennt. Die Dateien kommen aus `[Data]` in `freelancer.ini` |

Funde in Triggern, die nie aktiv werden, sind nur Hinweise – dort wirkt ohnehin nichts. Gegen die
14 Storymissionen des Originalspiels geprüft: Sie enthalten viel toten Code (z. B. eine ungenutzte
Musik-Bibliothek in m10), aber auch echte Fehler, etwa `dock_with_Bw08_01` in m07 (die Liste heißt
`dock_with_Bw09_02`) oder `Cnd_Destroyed` auf die nie gespawnten Schiffe `AlphaWing_3/4` in m13.

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
| `tests/MissionEditor.App.SmokeChecks` | Separat ausführbare WPF-Smoke-Checks; werden mit der Solution gebaut |

Die fachliche Ordneraufteilung und die Prüfkommandos beschreibt
[Projektstruktur](docs/project-structure.md).

Neue Prüfungen implementieren `IMissionCheck` und werden in `MissionChecker.AllChecks` eingetragen.

## MCP-Server für KI-Agenten

`MissionEditor.Mcp` macht den Kern des Editors für KI-Agenten nutzbar, die das
[Model Context Protocol](https://modelcontextprotocol.io) sprechen (z. B. Claude Code, Claude Desktop,
Codex). Ein Agent kann damit Missionen untersuchen, prüfen, Triggerabläufe nachverfolgen und
Änderungen als Entwurf vorschlagen. Der Server läuft lokal über stdio; er hat keinen
Netzwerkzugang, braucht keine API-Schlüssel und enthält kein eigenes Sprachmodell.

### Sicherheit

- Missionen werden mit ihrem **absoluten Pfad** angegeben. Ohne weitere Option darf der Server jede
  lokale `.ini`-Datei lesen – passend für mehrere Missionen und Spielinstallationen.
- Wer ihn einschränken will, gibt einen oder mehrere Ordner mit `--allow-root <Ordner>` an. Dann
  gelten nur `.ini`-Dateien darin; relative Pfade beziehen sich auf den ersten Ordner, symbolische
  Links innerhalb der Ordner werden abgewiesen. Die Fehlermeldung nennt den aufgelösten Pfad und die
  erlaubten Ordner. (`--workspace <Ordner>` ist die ältere Schreibweise dafür.)
- Andere Dateitypen als `.ini` und Dateien über 8 MiB werden immer abgewiesen.
- Standardmäßig wird nur gelesen. Änderungen entstehen als Entwürfe im Arbeitsspeicher
  (höchstens acht pro Sitzung) mit Vorher/Nachher und den Befunden des Entwurfs.
- Erst mit `--allow-writes` darf ein Entwurf exportiert werden – und zwar nur in eine **neue**
  Datei in einem vorhandenen Ordner. Bestehende Dateien und Originalmissionen werden nie
  überschrieben. Der Server weist den Agenten an, vor dem Export zu fragen; Claude Code und Codex
  fragen ohnehin vor jedem Werkzeugaufruf, den du nicht dauerhaft erlaubt hast.
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
| `list_game_nicknames` | gültige Systeme, Basen, Fraktionen, Schiffs-/Stationstypen, Loadouts, Ausrüstung und NPC-Schiffe der Spielinstallation |
| `prepare_section_edit` | Sektionsänderung als Entwurf |
| `prepare_new_mission` | vom Agenten geschriebene neue Mission als Entwurf |
| `read_mission_draft` / `discard_mission_draft` | Entwurf lesen oder verwerfen |
| `export_mission_draft` | Entwurf als neue Datei schreiben (nur mit `--allow-writes`) |

### Einrichten

Am einfachsten über den Editor: **„KI-Agent verbinden…“** in der Werkzeugleiste. Der Dialog
findet die `MissionEditor.Mcp.exe` (neben dem Editor, in einem daneben entpackten Release-Ordner
`mcp` oder in der Build-Ausgabe). Das Feld „Freigegebener Ordner“ bleibt normalerweise leer: Dann
kann der Agent jede Mission über ihren vollständigen Pfad lesen. Ein Ordner dort ergänzt
`--allow-root`. „Prüfen“ startet den Server testweise; der Schalter „Export erlauben“ ergänzt
`--allow-writes`.

Je Client (Claude Code, Claude Desktop, Codex) zeigt der Dialog, ob der Server dort schon
eingetragen ist. **„Hinzufügen“** trägt ihn direkt ein bzw. aktualisiert den Eintrag:

| Client | Datei |
|---|---|
| Claude Code (Kommandozeile und Desktop-App) | `%USERPROFILE%\.claude.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json`; bei der Store-Version (MSIX) zusätzlich deren Kopie unter `%LOCALAPPDATA%\Packages\AnthropicPBC.Claude_*\LocalCache\Roaming\Claude\`, sobald es sie gibt |
| Codex | `%USERPROFILE%\.codex\config.toml` |

Nur der Eintrag `freelancer-missions` wird angelegt oder ersetzt, alles andere in der Datei bleibt.
Die vorige Fassung bleibt als `<datei>.mission-editor.bak` erhalten; ist die Datei kein gültiges
JSON, wird nichts geändert. Den Client am besten vorher schließen und danach neu starten.

Als Alternativen zeigt der Dialog den Befehl `claude mcp add …` (braucht die Claude-Code-
Kommandozeile; der Dialog sagt, ob sie installiert ist) und den Text zum Einfügen von Hand. Die
Anleitung dafür richtet sich nach dem Zustand der Datei: neu anlegen, `"mcpServers"` ergänzen oder
nur den Eintrag in ein vorhandenes `"mcpServers"` einfügen. Hat die geöffnete Mission
ungespeicherte Änderungen, weist der Dialog darauf hin, denn der Agent sieht nur den gespeicherten
Stand.

Maßgeblich sind allein die Einträge in diesen Client-Dateien; dort stehen Serverpfad, Ordner und
Export-Freigabe. Zusätzlich merkt sich der Editor die letzten Eingaben des Dialogs in
`%LOCALAPPDATA%\FreelancerMissionEditor\agent-connection.json` – nur als Vorschlag für das nächste
Öffnen, der Server liest diese Datei nicht. Wer etwas ändern will, öffnet den Dialog erneut und
klickt „Aktualisieren“; das passt alle Dateien des Clients an. Danach den Client neu starten bzw. in
Claude Code `/mcp reconnect freelancer-missions` ausführen.

### „Frag KI“ direkt im Editor

**„Frag KI“** an einem Fund (Details) bzw. per Rechtsklick in der Prüfliste fragt ein Sprachmodell und
zeigt die Antwort im Editor: Erklärung, Annahmen und einen Korrekturvorschlag mit Vorher/Nachher. Der
Vorschlag wird vor der Anzeige geprüft – er muss eine der mitgeschickten Sektionen ersetzen, und der
Editor zeigt, ob der Fund verschwindet und ob neue Funde entstehen. „Übernehmen“ setzt ihn ein
(Strg+Z macht es rückgängig, gespeichert wird erst mit Strg+S); vorher lässt er sich noch bearbeiten.

Mitgeschickt werden der Fund mit der Erklärung des Editors, die betroffene Sektion, eine Bezugsstelle,
benachbarte Trigger und bei fehlenden Triggern die ähnlich benannten – nicht die ganze Datei.

Unter „KI-Agent ▾ → KI-Einstellungen…“ wählst du den Weg zum Modell:

| Anbieter | Voraussetzung | Kosten |
|---|---|---|
| Anthropic-API (Standard, Modell `claude-opus-5-5`) | eigener API-Schlüssel aus der Anthropic Console; wird mit Windows-DPAPI verschlüsselt für deinen Benutzer gespeichert (alternativ `ANTHROPIC_API_KEY`) | pro Anfrage, meist wenige Cent |
| Claude Code (lokal) | installierte Kommandozeile `claude` | über dein Abo |
| Codex (lokal) | installierte Kommandozeile `codex` | über dein Abo |

Die API-Anfrage nutzt strukturierte Ausgabe (die Antwort ist garantiert JSON nach festem Schema) und
den serverseitigen Fallback bei abgelehnten Anfragen. Die Kommandozeilen laufen ohne Schreibrechte und
bekommen den Auftrag über die Standardeingabe.

**„KI-Agent ▾ → Frag KI: Mission erklären“** bzw. **„… Lücken und Brüche“** fragt dasselbe Modell nach der
ganzen Mission und zeigt die Antwort als Text im Editor. Statt der Datei geht ein Auszug hinaus: alle Trigger
mit Zeile, Startzustand, Bedingung und Aktionen, Ziele und NPCs mit ihren Texten und die Funde des Editors
(höchstens 150 000 Zeichen; größere Missionen werden gekürzt). Gefragt wird der aktuelle Stand im Editor.

### Entwürfe des Agenten übernehmen

- Ändert ein anderes Programm – etwa der Agent – die geöffnete Datei, erscheint oben ein Hinweis mit
  „Neu laden“. Eigene Speichervorgänge lösen ihn nicht aus.
- **„KI-Agent ▾ → Mit Datei vergleichen…“** vergleicht die Mission abschnittsweise mit einer anderen Datei,
  z. B. einem mit `export_mission_draft` geschriebenen Entwurf. Abschnitte werden über Name, `nickname` und
  Vorkommen zugeordnet; das Fenster zeigt Vorher/Nachher und wie viele Fehler und Warnungen danach bleiben.
  „Ausgewählte übernehmen“ setzt die angehakten Abschnitte in einem Schritt ein (Strg+Z macht es rückgängig).

### Aufträge aus dem Editor

Alternativ arbeitest du im Chat von Claude oder Codex; der Editor liefert fertige Aufträge dafür.
„Kopieren“ legt sie in die Zwischenablage, im Agenten mit Strg+V einfügen:

| Wo | Auftrag |
|---|---|
| Rechtsklick in der Prüfliste → „An KI-Agent übergeben“ | Fund erklären und eine Korrektur als Entwurf vorschlagen; bei unbekannten Trigger-Zielen: prüfen, ob es ein Tippfehler ist oder ein Trigger fehlt, und ihn im Stil der Nachbarn entwerfen |
| „KI-Agent ▾ → Mission erklären lassen“ | Ablauf und Geschichte in Spielreihenfolge erklären, ab den aktiven Starttriggern, mit den tragenden Triggern und Zeilen; nur lesen |
| „KI-Agent ▾ → Auf Lücken und Brüche prüfen lassen“ | über die Prüfungen des Editors hinaus suchen: nie aktivierte Trigger, Sackgassen, unmögliche Bedingungen, zu früh deaktivierte Trigger, offene Ziele, fehlende Pfade für Scheitern; nach Schwere, belegt oder vermutet |
| „KI-Agent ▾ → Alle Funde erklären lassen“ | Funde zusammenfassen, je Art erklären, Reihenfolge zum Beheben vorschlagen |
| „KI-Agent ▾ → Neue Mission mit KI…“ | aus einer Beschreibung eine Mission schreiben, mit dem Server prüfen und erst nach Zustimmung exportieren |

Die Aufträge nennen die Datei mit vollständigem Pfad, die Meldung samt Erklärung des Editors und die
passenden Tools, und sie verlangen Entwürfe statt direkter Änderungen. Ist der Server auf einen Ordner
beschränkt, der die Mission nicht enthält, oder hat sie ungespeicherte Änderungen, sagt die
Statusleiste das beim Kopieren.

Von Hand bauen bzw. starten:

```
dotnet build src/MissionEditor.Mcp -c Release
dotnet run --project src/MissionEditor.Mcp -- [--allow-root <Ordner>]... [--game <Ordner>] [--allow-writes]
```

Das App-ZIP enthält den MCP-Server im Unterordner `mcp` und die Kommandozeile im
Unterordner `cli`. Beide laufen ohne installiertes .NET. Der Editor findet den mitgelieferten
MCP-Server automatisch. Ein zusätzlicher Download ist nicht nötig. Für die Nutzung ohne
Editor gibt es außerdem separate MCP- und CLI-ZIPs.

Claude Code (für alle Projekte des Benutzers, ohne Ordnerbeschränkung):

```
claude mcp add --scope user freelancer-missions -- "C:\Tools\MissionEditorMcp\MissionEditor.Mcp.exe"
```

Claude Desktop (`claude_desktop_config.json`) bzw. andere Clients mit derselben Struktur:

```json
{
  "mcpServers": {
    "freelancer-missions": {
      "command": "C:\\Tools\\MissionEditorMcp\\MissionEditor.Mcp.exe",
      "args": []
    }
  }
}
```

Codex (`~/.codex/config.toml`):

```toml
[mcp_servers.freelancer-missions]
command = "C:\\Tools\\MissionEditorMcp\\MissionEditor.Mcp.exe"
args = []
```

Für Exporte `"--allow-writes"` in die Argumente aufnehmen, für eine bestimmte Spielinstallation
`"--game", "D:\\Freelancer"`, für eine Beschränkung
`"--allow-root", "D:\\freelancer crossfire\\DATA\\MISSIONS"` (mehrfach möglich). Ohne installierte
exe geht statt `command` auch `dotnet` mit dem Pfad zur `MissionEditor.Mcp.dll` als erstem Argument.
Im Chat dann einfach den Pfad nennen, z. B. „Prüfe D:\freelancer crossfire\DATA\MISSIONS\M13\m13.ini“.

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
`-OutputDirectory` angegeben werden. Das App-ZIP enthält die GUI sowie CLI und MCP in den
Unterordnern `cli` und `mcp`; ein Download genügt. Die separaten CLI- und MCP-ZIPs enthalten
jeweils nur das entsprechende Werkzeug. Jedes Werkzeug wird als einzelne Self-contained-EXE
einschließlich der .NET-Laufzeit ausgeliefert; lose Laufzeit-DLLs sind nicht nötig. Das App-ZIP
enthält also drei EXEs: die App sowie `cli/MissionEditor.Cli.exe` und `mcp/MissionEditor.Mcp.exe`.
Beim Start entpackt .NET die eingebetteten Dateien automatisch in seinen temporären Cache.
Alle Pakete laufen ohne vorherige .NET-Installation.
