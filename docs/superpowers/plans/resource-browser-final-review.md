# Whole-change review

Reviewed current Core resource read/write/reference implementation, browser lifecycle and source/main integration against the approved design and implementation plan. Read-only review; no tests repeated. Current file contents were checked in addition to the provided diff because review fixes were landing concurrently.

## Findings

1. **P1 — reload completion can silently erase edits made while the installation index runs.** `src/MissionEditor.App/Resources/ResourceBrowserWindow.xaml.cs:570` (`OnReload`, especially lines 576–577) awaits `IndexInstallation()` and then calls `SelectRow` unconditionally. The browser remains editable during that await. If a user starts editing the currently displayed ID, the index completion correctly checks `_editorDirty` in `IndexInstallation`, but the unconditional continuation in `OnReload` subsequently calls `ShowEntry`, resetting the editor and dirty flag without a decision. An old reload can also resume after a context switch. Preserve the live editor/selection and verify the context/generation after the await; an indexing result must never replace a newly edited buffer. Add a deterministic delayed-index regression.

2. **P2 — catalog text/type filters omit existing resource variants.** `src/MissionEditor.App/Resources/ResourceBrowserWindow.xaml.cs:187` chooses `_catalogByIds[ids][0]` for the single ID row. `ApplyFilters` at lines 237–240 then searches only that variant's text and type. The catalog already contains every language/type variant, so a text present only in another language cannot be found. An ID that has both a String and an Infocard is excluded from the Infocard filter if its first variant is String. Evaluate all actual variants for matching while keeping one row per global ID, and ensure the selected detail variant respects the resource-type filter. The existing two-language fixture can cover non-primary-language search; a colliding string/infocard local ID covers the type case.

## Confirmed previous corrections

- Direct numeric links use numeric comparison, preserving leading-zero and explicit-plus tokens.
- Source link templates use ContentPresenter and do not rely on a private MainWindow resource.
- Definition navigation uses structured file/line provenance and the live document for the active mission.
- Native reads are scoped and coordinated with writes; direct saves validate the source fingerprint and original resource bytes; working-copy verification covers all resources.
- The commented-RDL segment mapping and unsupported empty-string deletion cases now have explicit guards.

## Validation boundaries

This review did not rerun previously reported passing tests. Mixed-DPI physical-monitor restoration still requires a real hardware check; reading the native rectangle conversion cannot substitute for that acceptance test.

## Resolution

Both findings were corrected after review. Reload refreshes the selected entry before starting asynchronous work; the index owns the only completion path with generation and dirty-editor guards. Catalog rows retain all actual variants and filter/search matches those variants, selecting a matching type on navigation. WPF regression checks exercise a deliberately blocked second DLL during reload while typing a new draft, non-primary-language text search across two DLL slots, and a String/Infocard local-ID collision.
