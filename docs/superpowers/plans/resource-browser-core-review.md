# Core resource review

Scope: Task 1, resource reading/writing, RDL and mission/nickname reference model. Independent static review of the current working files and the Core review package. Existing implementer evidence: 13 focused tests passed; no duplicate test run performed by this reviewer.

## Findings

1. **P2 — simplified RDL editor can change comment contents rather than the actual TEXT element.** `InfocardDocument.cs:20-23` matches XML TEXT elements to regex matches by count only. `<RDL><!-- <TEXT>fake</TEXT> --><TEXT/></RDL>` has one real TEXT and one regex match in the comment; CanEditSegments becomes true. Replacing its segment edits the comment, leaves actual text empty and still validates. Mixed self-closing elements/comments, processing instructions or quoted `>` attributes require source-position-aware matching or disabling simplified editing. Regression tests were being added during review; remediation still in progress at the last inspection.

2. **P2 — clearing a string silently removes the selected ID from future resolution.** `ResourceEncoding.cs:37-42` permits empty text. The writer emits the zero-length entry, but `GameText.cs:123` skips every empty string. A successful save therefore makes the ID unresolved and removes it from the installation catalog, so the user cannot edit it back. The approved scope excludes resource deletion. Reject an empty edited string with a clear message or explicitly support its continued editing without enumerating string-table padding as new resources.

3. **P2 — mission ship names were read from the wrong section (fixed during review, statically verified).** The original implementation read only `MsnShip.individual_name`; the repository's real mission defines `individual_name` on the linked NPC (e.g. `king`). Current `MissionResourceIndex` follows MsnShip.NPC to that definition, retains source provenance and indexes its name even without GameData. Regression coverage is being added by the implementer.

## Integration check requested

`ResourceDraft.Create` rejects stale selected resource bytes. If the selected resource changes externally before the current editor content is applied, the UI's Save As path calls Create again and may prevent exporting the unsaved content too. The existing conflict-copy test creates a draft first and then appends unrelated bytes, so it does not exercise this case. Test an external change to the selected resource before Apply; keep direct-save conflict protection while allowing the advertised copy recovery path.

## Positive observations

- Targeted reads use data/image resource handles and release them in finally blocks; no game DLL code executes.
- Explicit identities include actual language, type, local ID and source path; targeted language enumeration avoids guessing a save language.
- Full working-copy updates retain other resources, and verification compares the complete resource type/name/language map before replacement.
- Multiple edits in one string block preserve neighbors; original save checks complete-file fingerprints and makes a unique backup; copy export retains the original.
- Enumeration and ID caches have separate short locks and generation invalidation, with cancellation inside native enumeration and decoding.

Verdict at report time: **changes requested** for findings 1–2 and the stale-editor copy recovery check. Finding 3 is statically resolved. Final acceptance remains subject to regression tests and the parent integration/visual validation.

## Resolution after review

The RDL simplified editor now rejects ambiguous comment/CDATA/processing-instruction spans, with exact reported regressions covered. Empty strings are rejected as unsupported resource deletion. NPC individual_name resolution works without an installation, and stale selected resources can be exported while direct saves still check original bytes and file fingerprints. All 65 Core and 18 MCP tests pass. WPF checks cover the externally changed selected-resource recovery path as well.
