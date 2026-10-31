# Mission Editor

Werkzeug zum Ansehen und Prüfen von Freelancer-/Crossfire-Missionsskripten (Missions-INIs mit
`[Trigger]`, `[MsnShip]`, `[NNObjective]` usw.). C#/.NET 10, WPF.

## Projekte

| Projekt | Inhalt |
|---|---|
| `src/MissionEditor.Core` | INI-Leser ohne Verlust (`IniDocument`), Modell (`MissionScript`, `Trigger`, Verweise), Trigger-Graph und Anordnung, `FlHash.CreateId`, Prüfungen |
| `src/MissionEditor.Cli` | Kommandozeile: `check`, `roundtrip`, `stats`, `graph` (Graphviz-DOT), `hash` |
| `src/MissionEditor.App` | Oberfläche: Trigger-Liste, gezeichneter Graph um den gewählten Trigger, Details, Fundliste |
| `tests/MissionEditor.Tests` | xUnit-Tests |

## Bauen und starten

```
dotnet build Tools/MissionEditor/MissionEditor.slnx
dotnet test Tools/MissionEditor/MissionEditor.slnx
dotnet run --project Tools/MissionEditor/src/MissionEditor.App -- <datei.ini> [trigger]
dotnet run --project Tools/MissionEditor/src/MissionEditor.Cli -- check <datei.ini>
```

Im Editor: Datei öffnen (Strg+O oder hineinziehen), Trigger filtern (Strg+F), Knoten oder Verweise
anklicken, Zurück mit Alt+←, Zoom mit Strg+Mausrad, Doppelklick auf einen Fund springt zum Trigger.

## Wie die Datei gelesen wird

Nach dem Decompilat von content.dll/common.dll (`fl_decomp`, `MissionScript.cpp`, `common_ini.cpp`):

- `;` beendet eine Zeile, Werte werden an `,` getrennt, `[;Name]` ist eine auskommentierte Sektion.
- Schlüssel und Sektionsnamen ohne Groß-/Kleinschreibung; Trigger-IDs = CreateID (ebenso).
- In `[Trigger]` zählt nur, was nach `nickname` steht; die letzte `Cnd_*` gilt; unbekannte
  `Cnd_`/`Act_`-Schlüssel werden still ignoriert; ein zweiter Trigger mit derselben ID wird ignoriert.
- Gespeichert wird Byte für Byte wie gelesen (Windows-1252, Zeilenenden, Kommentare, Leerzeichen).

Graph-Kanten zeigen in Ablaufrichtung: `Act_ActTrig`/`Act_DeactTrig`/`Act_Save` vom Trigger zum Ziel,
`Cnd_WatchTrigger` vom beobachteten Trigger zum Beobachter.

## Prüfungen

| Id | Inhalt |
|---|---|
| `unknown-trigger` | ActTrig/DeactTrig/WatchTrigger/Save auf nicht definierten Trigger, mit Namensvorschlag |
| `duplicate-nickname` | doppelte Trigger (ID-gleich), doppelte Schiffe/Formationen (gemeinsame Liste, Schreibweise), doppelte Nicknames anderer Sektionen |
| `trigger-structure` | Trigger ohne nickname, ohne/mit mehreren Bedingungen, Zeilen vor nickname, unbekannte Schlüssel |

## Nächste Schritte

IDS-Texte (LoadString aus den Ressourcen-DLLs), weitere Prüfungen (nie aktivierte Trigger,
Schiffsnamen-Schreibweise in Spawn-Aktionen, Mehrfach-Aktivierung von Spawns, Story-Fortschritt nur
über Handelsstraßen, leere Zieltexte), Bearbeiten von Triggern, Test-Spielstände, BINI-Dateien.
