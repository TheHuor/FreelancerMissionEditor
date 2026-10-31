# MCP server implementation plan

Branch: codex/mcp-server, based on codex/bini-support / PR2. Previous changes remain separately reviewable; do not merge PR2 implicitly.

User authorization: implement external-agent MCP integration, no embedded/local model.

Architecture: separate net10.0 stdio executable using official C# SDK2.3.0 and MissionEditor.Core. Shared central MinVer. No HTTP endpoint, AI keys, model downloads or WPF dependency. Operates on saved files inside an explicitly configured workspace; unsaved editor buffers are outside this phase.

Tasks:
- [x] Add isolated workspace service and tests for inspection, checks, source/context, missing-trigger evidence and suggestions.
- [x] Add in-memory edit/new-mission drafts with preview and validation. Read-only by default; --allow-writes permits exporting drafts to new files only, never overwriting sources. SHA256 guards reject stale saved sources. Preserve BINI on edited copies.
- [x] Expose bounded/paged MCP tools using stdio; all logging on stderr. Wire CLI options and clear error results.
- [x] End-to-end MCP client test: initialize, list tools and invoke inspection; test writing boundaries, duplicate paths, invalid data and format preservation.
- [x] Document build/start and client configuration; package/version the new module; build/test/review and push only this branch.

Missing-trigger suggestions are lexical candidates with calling references, not claims about the intended story. All checker findings retain explanations and limitations. New mission drafts are text INIs supplied by the agent, not automatically invented by the server.

## Stand (2026-10-31, von Claude Code übernommen)

- Alle Aufgaben erledigt; 14 MCP-Tests (inklusive echtem Stdio-Client, beide Protokollversionen) und 30 Core-Tests grün.
- Die drei zuletzt offenen Tests wurden im Kern behoben: Kodierungsprüfung in `ReplaceSection` (jetzt
  `InvalidDataException`, die auch die GUI abfängt), BINI-Expansionsgrenze (16 × Dateigröße, mind. 1 MiB;
  alle 1241 Vanilla-BINIs laden weiter), Levenshtein im Diagonalband.
- Ergänzt: `list_sections` (Sektionsindex für Nicht-Trigger-Sektionen), Beschreibungen aller Parameter
  (vom Protokolltest geprüft), Serveranweisungen, README-Abschnitt mit Client-Konfiguration.
- Lokal geprüft: `Publish-Release.ps1` packt den MCP-Server als eigenständiges ZIP mit gleicher Version.
