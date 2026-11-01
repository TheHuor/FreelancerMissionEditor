# Resource browser UI implementation

Status: implemented; integrated WPF build passes (0 warnings, 0 errors).

Owned files:
- `src/MissionEditor.App/Resources/ResourceBrowserWindow.xaml`
- `src/MissionEditor.App/Resources/ResourceBrowserWindow.xaml.cs`
- `src/MissionEditor.App/Resources/WindowGeometry.cs`

Public API (namespace `MissionEditor.App`): parameterless `ResourceBrowserWindow`; `RefreshContext(MissionScript?, GameData?, string?) : bool`; `NavigateToId(int) : bool`; `TryLeave(string) : bool`; `ResourceSaved`, `GameFolderRequested` events; `UsageRequested : Action<int>` absolute mission-line event. Host owns the singleton window and source synchronization.

Implemented: current live mission usages; installation resource enumeration in background with progress, cancellation and generation invalidation; catalog-backed rows and preindexed usages avoid repeated resource reads; large-catalog search/filter/sort runs in background; distinct IDs, real DLL slot/path filter, category/type filter and text/ID search; wrapped selectable preview and copy; technical identity/language metadata and variant choice; exact usage lines and indirect provenance; string editing, secure lossless infocard TEXT segment editing with paragraph labels, full RDL/XML mode, preview, independent native TextBox undo; validated apply/discard drafts, file-level original save and full DLL-copy export; actual-language draft identities; guarded navigation/closing/context changes/reloading; error retention; backup path in technical details; explicit exported-but-original-unsaved state.

Lifecycle fixes: same-ID link activation preserves unsaved editor and selected language; identity changes clear every old detail/control; filters keep the current editor even when it falls outside results; disappearing mission references retain detail with current-mission no-usage message; failed direct saves preserve draft/selection. First editor mutation captures source fingerprint once. If stale selected bytes already conflict, a nonmatching conflict fingerprint prevents original overwrite while preserving explicit Save As. Apply validates before constructing a retained-snapshot draft; it does not recapture externally changed source state.

Geometry: persisted normal bounds/dividers, nearest native monitor work area keeps valid secondary-monitor positioning and handles removed displays. `MISSION_EDITOR_WINDOW_SETTINGS_DIR` redirects settings for smoke-check isolation. Native monitor bounds converted using WPF's current DPI.

Validation: `dotnet build src/MissionEditor.App/MissionEditor.App.csproj --no-restore` successful, 0 warnings/errors. Root owns full solution tests and WPF smoke/render validation. No game DLL was modified by this task.

Review attention: mixed-DPI multi-monitor restoration deserves manual validation on actual hardware. Original writes are synchronous while the writer performs verified working-copy replacement; catalog enumeration and large searches are asynchronous. UI details can be accessed by XAML names for smoke tests.

Visual review follow-up: inspected the rendered 840×760 browser. The theme's selected ComboBox content renders ToString rather than DisplayMemberPath; both choice records now return their label. Reordered DockPanel children and bounded language selection to 200 DIP, preserving wrapped resolution status and ID heading beside their right-docked controls. Build after these corrections passes with 0 warnings/errors.

Shared validation follow-up: Apply uses `ResourceDraft.Validate` without fingerprint recapture, including rejection of empty String resources (deletion is outside scope). Core Create now captures current hash even for stale selected bytes; direct save checks original raw bytes and hash, whereas Save As allows explicit recovery. The browser retains its first-edit hash through Apply. Missing-DLL entries avoid native variant enumeration. Refresh tracks the retained detail ID even after a mission reference disappears, so later edits/undo still refresh its usages.
