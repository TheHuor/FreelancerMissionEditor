# Infocard raw-data diagnosis and repair

The reported IDS 501870 exists in sol.dll with language 1033, but its UTF-16 resource is 483 bytes long and ends in an incomplete extra byte. The installed DLL was inspected read-only; all write validation uses disposable fixtures.

- [x] Preserve resource identity, language and original bytes when a single infocard cannot be decoded. Report DecodeError for that entry rather than marking the whole DLL unreadable or aborting its catalog.
- [x] Trim UTF-16 terminators only at aligned code-unit boundaries. Produce explicitly marked repair text using U+FFFD for invalid bytes; never use this fallback silently as saved content.
- [x] Expose read-only original bytes in a Rohdaten (Hex) tab, showing at most 65536 bytes for responsiveness and allowing export of the full original resource. Raw export cannot overwrite registered resource DLLs.
- [x] Open failed infocards in the editable RDL/XML tab with their actual language. Disable simplified segment editing until the source is repaired.
- [x] Reject remaining decoder replacement characters and invalid XML on repair. Capture file fingerprints independently of invalid editor content. Save via the existing verified working copy/backup mechanism; preserve other resources and languages.
- [x] Test odd-length UTF-16 and invalid UTF-8, full raw-byte retention, copy recovery, direct repair, another language at the same ID and unchanged neighbors. WPF test drives the actual repair/editor/save path.

Validation: 70 Core and 18 MCP tests pass. WPF smoke checks include the raw-data/XML repair workflow. Read-only verification of the actual sol.dll entry now returns DecodeError, actual language 1033, all 483 original bytes, available repair text and CanEdit=true.
