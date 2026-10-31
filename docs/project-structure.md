# Projektstruktur

Die Solution enthält vier Produktivprojekte und drei Prüfprojekte. Die
Projektgrenzen folgen den Ausführungsumgebungen; innerhalb der Projekte
gruppieren Ordner zusammengehörige Funktionen.

## Produktivcode

| Projekt | Aufgabe |
| --- | --- |
| `MissionEditor.Core` | Gemeinsame Missionsverarbeitung ohne WPF-Abhängigkeit |
| `MissionEditor.App` | Windows-Oberfläche und lokale Integrationen |
| `MissionEditor.Cli` | Kommandozeileneinstieg für die Missionsprüfung |
| `MissionEditor.Mcp` | MCP-Protokoll, Werkzeuge und Zugriff auf Missionsdateien |

### App

| Ordner | Inhalt |
| --- | --- |
| `Editor` | Hauptfenster, Sektionseditor, Vergleich und Programminformationen |
| `Simulation` | Oberfläche zum schrittweisen Durchspielen einer Mission |
| `Ai` | KI-Beratung, Aufträge, Anbieter und KI-Einstellungen |
| `Agents` | Verbindung zu externen Agenten, Client-Konfiguration und Prompts |
| `Settings` | Auswahl und Speicherung des Freelancer-Spielordners |
| `ViewModels` | WPF-Bindings, Befehle und einzelne Darstellungsmodelle |
| `Infrastructure` | Programmsuche und gemeinsame Fensterbefehle |
| `Controls` | Graphdarstellung und zugehörige Anzeigeoptionen |
| `Themes` | Gemeinsame WPF-Stile |
| `Assets` | Symbole, Bilder und deren Herkunft |

`App.xaml`, der Anwendungseinstieg und Assembly-Metadaten bleiben im
Projektverzeichnis. XAML und zugehöriger Code liegen jeweils zusammen.
Ressourcen aus `Assets` werden in verschiebbaren Fenstern über
assemblybezogene Ressourcenpfade angesprochen.

### Core

| Ordner | Inhalt |
| --- | --- |
| `Model` | Missionen, Trigger, Objekte und Engine-Vokabular |
| `GameData` | Installationsdaten und Texte aus Ressourcen-DLLs |
| `Simulation` | Zustände und Schritte beim Durchspielen einer Mission |
| `Ini` | INI/BINI, Dokumentbearbeitung und Sektionsvergleich |
| `Checks` | Befunde und Prüfregeln |
| `Graph` | Triggerverbindungen und deren Layout |
| `Hashing` | Freelancer-Hashberechnung |

Die bestehenden Namespaces bleiben zur Kompatibilität erhalten. Die Ordner
ordnen die Dateien nach Aufgaben; sie ändern die öffentlichen Typnamen nicht.
Neue Dateien gehören zum fachlich zuständigen Ordner.

## Prüfungen

- `MissionEditor.Tests`: Core-Tests in `Ini`, `Checks`, `Model`, `Simulation`
  und `GameData`.
- `MissionEditor.Mcp.Tests`: Workspace- und Protokolltests.
- `MissionEditor.App.SmokeChecks`: ausführbares WPF-Prüfprogramm; wird mit
  der Solution gebaut, aber separat gestartet.
- `Fixtures`: gemeinsame Missionsdateien für die Prüfungen.

```powershell
dotnet build MissionEditor.slnx
dotnet test MissionEditor.slnx --no-build
dotnet run --project tests/MissionEditor.App.SmokeChecks --no-build
```

Die WPF-Smoke-Checks öffnen Testfenster und benötigen eine interaktive
Windows-Sitzung. `dotnet test` führt sie nicht aus.
