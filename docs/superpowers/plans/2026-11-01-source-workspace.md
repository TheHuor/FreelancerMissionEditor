# Source workspace implementation plan

> Use superpowers:executing-plans to execute these tasks in this session.

Goal: make uninterrupted section source the primary middle workspace, with an optional graph mode and a contextual right inspector. Approved in chat after the revised Product Design mock on 2026-11-01.

Architecture: keep the existing main view model/document/navigation and graph controls. Share physical source rows, selected source row and selected resource between main and detached source views. A reusable context inspector provides explicit resource and definition editing actions; inline tokens only select context.

Constraints: retain dark theme, bottom findings, separate non-modal source/resource windows, missing/error IDS navigation, exact parameter roles, copying original code only, undo/reload and all safe resource writing behavior. No installed DLL changes. No push/merge.

## Tasks

- [x] Add MainViewModel source state: SourceLines excludes synthetic captions; SelectedSourceLine/SelectedResourceLink drive SourceContextLinks, SourceContextText, SourceContextOrigin and SourceDefinitions. IsGraphView defaults false. Remap context after document changes.
- [x] Make SourceLineView render exactly one uniform-height physical code row with line number, a small context marker and individually selectable ID/nickname tokens. Remove injected resource/error/definition rows and per-line scrollbars. Add ResourceSelectedEvent separately from explicit ResourceRequestedEvent.
- [x] Add SourceContextView reusable inspector. Show all roles for selected line, wrapped selectable text, status/provenance, errors and explicit resource/definition actions. Main source/graph panels occupy the same middle grid cell with a mode switch. Right tabs become Kontext/Funde/Verweise. Detached source uses the same source rows/inspector. Preserve source and graph scroll positions across mode changes.
- [x] Update finding/usage navigation and section edit/delete actions. Verify normal/minimum width, distinct IDs and nickname context, missing IDS, role distinction, copy, view switching, detached synchronization, undo and bindings. Render and inspect WPF previews, run full tests/build, review and commit locally.

Review focus: selection clearing and remapping after undo; popup title/text at separate parameter positions; multiple object-name/type roles sharing IDs; stale detail after installation changes; graph viewport work running while hidden.

## Progress

Initial status clean on codex/resource-browser. Implement in the authorized checkout and retain all existing commits.

Implementation complete. Source rows are physical lines with one 24-DIP row, global scrolling and individually selectable IDs/known nicknames. Main source and graph controls are retained while toggling visibility; pending graph recentering is deferred until visible. Right inspector and detached source share source/role selection and explicit resource/definition editor actions. Section edit remains on the toolbar, delete in the source context menu.

Validation: 71 Core + 18 MCP tests pass. WPF smoke checks pass, including actual inline token selection without opening a resource window, explicit selected-ID editor opening, mode switches retaining scroll/selection, populated normal/minimum layouts, inspector actions reachable by scrolling, detached view, edit/undo and exact NNObjective line/role restoration on resource reload. Binding trace is empty. Previews are in artifacts/source-workspace-preview.

Independent review found two P2 issues: inaccessible inspector actions at small heights and lost exact non-trigger context during resource reload. Both were fixed and the scoped recheck reports no open material findings. A regression reproduced the reload-selection failure before the correction.

Known platform validation boundary: rendered layouts cover 1120x700, 1280x800 and 1920x1080; physical monitor/DPI switching is not newly exercised. No installed game DLL was written, and no push/merge is performed.
