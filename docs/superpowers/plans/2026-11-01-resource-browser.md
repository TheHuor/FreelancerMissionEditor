# Resource Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Implement the approved resource browser, source windows and safe existing-resource editing.

**Architecture:** Core owns structured mission references, resource identities, native enumeration and verified writes. WPF owns filtering, drafts, navigation and reusable non-modal windows. Existing DescribeIds/Get callers remain compatible.

**Tech Stack:** .NET 10, WPF, Windows resource APIs, xUnit.

**Spec:** docs/superpowers/specs/2026-11-01-resource-browser-design.md

## Global Constraints

- Separate reusable non-modal source and resource windows; compact source stays on the right and findings below.
- Never modify installed game DLLs during tests. Use temporary complete DLL fixtures.
- Preserve actual resource language, neighboring strings, raw infocard structure and other resources.
- Save via verified working copy, conflict check and unique backup; Save As leaves original and drafts active.
- Explicit Save / Discard / Cancel protects navigation, closing and source changes; failed saves retain selection and draft.
- Only current mission usage is claimed. Index registered DLL resources, never brute force IDS.
- No push, PR creation or merge. Branch codex/resource-browser starts at b7acf0d.

## Review Focus

- Two IDs in a popup line retain distinct roles and navigation.
- Same ID across languages/installations never aliases drafts or cached data.
- External modification and file locks leave original bytes and drafts intact.
- Mission undo and installation switch discard stale index results and refresh usages.
- Multi-segment or unsupported RDL is never silently flattened on write.

## Task 1: Core resource and reference services

Files: src/MissionEditor.Core/GameData/{GameText,GameData}.cs; new focused resource/reference/writer files; tests/MissionEditor.Tests/GameData.
Interfaces: expose structured resource resolution including global ID, slot, DLL path, local ID, type, language, raw bytes/text, status; cancellable enumeration; explicit cache reload; structured references with role/category/source section/line/parameter and indirect provenance; draft/write service keyed by actual identity.

- [x] Add failing tests for multiple known IDS positions, unknown numbers, missing installation, nickname provenance, long Unicode strings and paragraphs.
- [x] Implement reference extraction and object definition records; retain Get/DescribeIds wrappers.
- [x] Add temporary resource fixtures testing enumeration, languages and strings longer than 4096 characters.
- [x] Implement resource-table reading with scoped released handles, actual language and cancellable enumeration.
- [x] Add tests for string-block neighbors, multiple edits, RDL validation/segments, Save As, backup, conflict and locked targets.
- [x] Implement safe writer and lossless RDL editing helpers; verify complete resource tables before replacement.
- [x] Run Core tests and record results.

## Task 2: Browser and editor lifecycle

Files: new src/MissionEditor.App/Resources/*, Settings window geometry helper; smoke checks.
Consumes Task 1 public models/services. Produces ResourceBrowserWindow with explicit NavigateToId, RefreshContext and TryLeave methods and usage navigation event.

- [x] Implement mission list, shared filters, numeric ordering, selectable wrapped text, identity/status, copying and current mission usage.
- [x] Implement asynchronous installation enumeration with progress/cancellation and generation checks.
- [x] Implement drafts, string and RDL editors, preview, language selection, apply/discard/save/save-as and guarded transitions.
- [x] Persist geometry with screen clamping and split positions; support keyboard commands and dark theme.
- [x] Add smoke checks for navigation guards, missing resolutions and window construction.

## Task 3: Source window and main-editor integration

Files: MainWindow.xaml/.cs, MainViewModel partials, SectionLineItem, new reusable source control/window.
Consumes structured references and Task 2 browser navigation.

- [x] Add per-ID inline source links and separate bounded hint rows independent of horizontal code scrolling.
- [x] Add toolbar entry, singleton windows, synchronized exact usage navigation and preserved geometry.
- [x] Guard mission/installation/reload/close before changes; refresh references after edits/undo/save.
- [x] Verify existing bottom findings and right splitter behavior.

## Task 4: Acceptance and review

- [x] Run dotnet test MissionEditor.slnx --no-restore and app smoke checks.
- [x] Render WPF previews at minimum/normal width and scale settings; inspect binding errors and keyboard access.
- [x] Independent whole-change review against spec and fix material findings.
- [x] Record validation, limitations and local branch state; no publication.

## Progress

- Initial state verified clean at b7acf0d. Created codex/resource-browser in the authorized workspace; keeping this checkout avoids moving outside writable roots.
- Baseline: 45 Core + 18 MCP tests passed.
- Core implementation and reviews complete: 65 Core + 18 MCP tests pass. Native writes use only temporary copies of our own DLL in all tests.
- Browser and source integration complete. WPF checks cover separate per-ID links (including leading zeros), exact non-trigger navigation, same-ID editor retention, Save/Discard/Cancel, locks, backup, original/copy distinction, external resource changes before Apply, actual language variants, background reload and all-variant search.
- Reviewer findings resolved: comment-sensitive RDL mapping, unsupported empty-string deletion, NPC individual_name chain, original provenance navigation, stale selection/filter variants and asynchronous reload overwriting newer edits.
- WPF preview artifacts: artifacts/resource-browser-preview (ignored build artifacts), normal/minimum windows and 125/150/200 percent rendering. Minimum-height browser keeps a usable text area and collapses usage when needed.
- Restored accidental duplicate method headers in SourceLineView while retaining the user's comments; restored the existing MainWindow trigger labels and ListBoxItem base style as well as new resource entry points.
- Limitation: physical mixed-DPI monitor switching has not been exercised on real multiple-monitor hardware; scaling previews do not substitute for that check.
- Local feature branch only; no push, PR creation or merge.
