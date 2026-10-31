# BINI and editor identity — implementation notes

User scope: add BINI support, retaining binary format when saving; also add original app icon,
a subtle starfield/nebula graph background and a custom modern caption. User selected starfield
and later requested a darker title bar; final caption color is #060B12.

Implementation branch: codex/bini-support, created from clean master after the layout PR.

## Design

BINI v1 layout follows fl_decomp/src/include/common_ini.h and Common/common_ini.cpp:
12-byte header; section (uint16 name offset, uint16 entry count); entry (uint16 name offset,
byte parameter count); parameter (byte type + four-byte payload), ANSI zero-terminated string table.
Types 0/1/2/3 are byte/int32/float32/string. Strict bounds, terminator and count checks reject
malformed input. Name offsets are 16-bit; string-value offsets are 32-bit.

IniDocument owns format metadata. Original BINI bytes are cloned and returned unchanged when
unedited. Immutable edits preserve typed parameters on untouched lines and unchanged siblings.
Quoted JSON-style strings provide lossless editing of delimiters and escapes. Text parsing remains
unchanged. Unrepresentable input is rejected before closing the edit dialog; save exceptions are
caught in Save and Save As. The source fixture is never modified by integration tests.

The custom Window template uses WindowChrome with native SingleBorderWindow behavior underneath.
Buttons route through SystemCommands; closing still triggers the existing unsaved-changes guard.
No AllowTransparency or primary-monitor-only sizing workaround is used.

## Verification

- Baseline 20 tests passed.
- New BINI tests initially failed; codec implementation passes them.
- Reproduced and fixed: duplicate section removal changing original numeric types; large integer IDs
  being rounded into float32; editing one parameter changing untouched sibling types/payloads.
- 30 core tests passed after fixes, including typed BINI boolean semantics for repeatable.
- WPF smokechecks passed BINI graph loading, Save, Save As, original-byte backup and Undo,
  alongside existing window size, navigation and contrast checks.
- Desktop inspection confirmed icon, custom caption, graph texture and readable graph labels.
- Independent read-only code review found no critical issues; both reported type-retention and save
  exception handling concerns were addressed. Final targeted type regression passed.

## Limits

No runtime game-engine test was performed. The local Crossfire MISSIONS/EQUIPMENT files inspected
were text, so binary fixtures were independently packed from the engine structures. Native
150%/200% DPI and multi-monitor Snap interactions still need manual verification; no OS display
settings were changed. Final color is a user-approved darkening of the earlier inspected caption.
