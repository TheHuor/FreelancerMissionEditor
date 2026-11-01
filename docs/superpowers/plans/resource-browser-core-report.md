# Core resource services implementation report

Status: complete for Task 1, including review corrections. No commits or branch changes performed by this worker.

## Public interfaces

All in `MissionEditor.Core.Model`:

- `ResourceIdentity` includes full DLL path, slot, local ID, kind and actual LANGID. `ResourceEntry` separates plain reading text, original RDL and bytes from status/cause.
- `GameText.Resolve(int, ushort? language = null, ResourceKind? kind = null)` and `Variants(int)` read only the relevant string block and RT_HTML resource. Existing `Get(int)` remains compatible. `DllPaths`, cancellable `Enumerate(...)`, `Reload()` support browser indexing/lifecycle.
- `IdsReference` retains role/category/file/section/line/parameter/source text and optional indirect provenance plus separate `DefinitionPath`/`DefinitionLineNumber`.
- `MissionResourceIndex.Build(...)`, `Parameters(...)`, `ObjectTargets(...)` never parse presentation strings. Known object positions resolve definitions, system/base INIs, NPC names and ship type chains. Mission NPC name resolution works without game resources.
- `GameData.Definition(...)`, `ResolveMissionShipType(...)`, `Invalidate(root)` support structured origins and cache changes. NPC definition files are cached per installation.
- `InfocardDocument.Parse`, `Validate`, `PlainText`, `TextSegments`, `CanEditSegments`, `WithTextSegments` provide secure RDL parsing and bounded text edits; ambiguous comment/CDATA/PI/attribute spans disable simplified editing while full RDL remains available.
- `ResourceDraft.Create`, `ResourceDraft.Validate`, `ResourceWriter.Save(drafts, destinationPath?)`, `ResourceSaveResult` provide immutable edits, first-draft file fingerprints, exact original-byte comparison, verified full-file copies, unique backups and atomic replacement.

## Safety and concurrency

Native data/image handles never execute DLL code, are scoped and always released. Shared per-file reader/writer coordination lets targeted reads and indexing run together while preventing our outstanding reads during replacement. UI cache locks do not span native enumeration; generations prevent repopulating caches after reload.

Writer updates only existing type/name/language entries, preserving string-block neighbors. The reopened working copy's entire resource table, including unrelated types/names/languages, is checked against expected bytes before replacement. Direct saves compare file fingerprints and original selected resource bytes; either kind of conflict preserves the original. Save As applies the draft to a complete current source DLL copy even after a source conflict, leaving original and draft active. Read locks, working-copy failures and invalid content don't mutate drafts.

An empty edited string is rejected because zero-length string-table entries become absent entries and deletion is excluded by scope. Oversized strings, embedded nulls and invalid Unicode are rejected. Infocard byte order, BOM and terminators are retained; simplified edits preserve surrounding source tags/attributes/whitespace.

## Validation

- Baseline before feature: 45 Core / 18 MCP passing (parent).
- Failing tests observed before missing reference/native writer implementation.
- Regression failures reproduced for NPC-name fallback, RDL comment/PI/quoted-attribute source mapping, and stale selected-resource Save As recovery, then fixed.
- Latest `dotnet test MissionEditor.slnx --no-restore`: **65 Core + 18 MCP passed**, zero failures/skips, no build warnings.
- 20 new Core cases cover popup roles, parameter recognition, non-IDS numbers, missing installation, source provenance, base/system definitions, NPC name/type separation, long Unicode with whitespace/paragraphs, actual languages, enumeration cancellation, multi-edit neighbor preservation, exact backup, full-copy Save As, RDL preservation/security, external conflicts, file locks, empty/null rejection and stale-selected-resource copy recovery.
- All native write tests use `tests/Fixtures/ResourceDllFixture.cs`: disposable temporary copies of our own Core assembly. No installed game DLL touched. The fixture has no xUnit dependency and is linked by WPF smoke checks too.

API reference checked against Microsoft's UpdateResourceW and Using Resources documentation:
https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-updateresourcew
https://learn.microsoft.com/en-us/windows/win32/menurc/using-resources

## Practical scope

Numeric IDS uses and explicitly mapped base/system/ship/object parameter positions are supported; arbitrary unknown nickname positions are intentionally not guessed. Index usage is the current in-memory mission only. XML is a secure readable/editor representation rather than game-rendered RDL. Filesystem path aliases (junctions/hardlinks) are not canonicalized beyond full paths and case-insensitive Windows path comparison.
