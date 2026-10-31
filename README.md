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

- **Laden ohne Verlust**: Text-INIs in Windows-1252 (oder UTF-8 mit BOM); Kommentare, Leerzeilen,
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
| `tests/MissionEditor.Tests` | xUnit-Tests |

Neue Prüfungen implementieren `IMissionCheck` und werden in `MissionChecker.AllChecks` eingetragen.

## Mitarbeit

`master` ist geschützt: Änderungen kommen über Pull Requests, der Build mit Tests
(GitHub Actions, `.github/workflows/build.yml`) muss grün sein. Code-Kommentare auf Englisch mit
XML-Doku, Texte für Benutzer auf Deutsch; Formatierung nach `.editorconfig`.

## Geplant

- IDS-Texte (Titel, Ziele, Dialoge) aus den Ressourcen-DLLs anzeigen
- weitere Prüfungen: nie aktivierte Trigger, Schiffsnamen in Spawn-Aktionen, Mehrfach-Aktivierung von
  Spawns, Story-Fortschritt nur über Handelsstraßen, leere Zieltexte
- mehrere Dateien gemeinsam prüfen (z. B. alle Kapitel einer Kampagne)
- Test-Spielstände erzeugen, binäre INIs (BINI) lesen

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
