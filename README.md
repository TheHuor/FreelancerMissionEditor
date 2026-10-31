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
anklicken, Zurück mit Alt+←. Graph: auf freier Fläche ziehen verschiebt, Mausrad zoomt an der
Mausposition, Umschalt+Mausrad scrollt waagerecht, „Einpassen“ zeigt alles; ein Knoten unter der Maus
hebt seine Kanten hervor. Doppelklick auf einen Fund zeigt die Stelle im Quelltext (bei Duplikaten mit
der ersten Definition, auch für [NNObjective], [ObjList] usw.); das ⚠ in der Liste (rot bei
Fehlern) zeigt den Fund in der Prüfliste, „Nur Trigger mit Funden“ filtert die Liste, die Funde des
gewählten Triggers stehen oben in den Details.

Bearbeiten: Jeder Abschnitt in der Quelltext-Vorschau hat „Bearbeiten“ (Textfenster, Übernehmen mit
Strg+Enter) und „Löschen“ (die Rückfrage sagt, was sich im Spiel ändert). Danach wird sofort neu geprüft.
Strg+Z macht Änderungen rückgängig, Strg+S speichert; beim ersten Speichern einer Datei bleibt das Original
als `<datei>.bak` erhalten. Alle nicht geänderten Zeilen bleiben Byte für Byte gleich. Ungespeicherte
Änderungen zeigt ein `*` im Titel; vor Schließen, Öffnen und Neu laden fragt der Editor nach.

Symbole: roter Kreis mit ✕ = Fehler, gelbes Dreieck mit ! = Warnung, grauer Kreis mit i = Hinweis;
Trigger mit Funden sind in der Liste rosa (Fehler) bzw. gelb (Warnung) hinterlegt.

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
