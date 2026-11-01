# Definition section editing

The user selected editing the defining section instead of opening the whole source INI. Replace the read-only definition viewer with the existing themed section editor. Name and infocard references from the same definition share one compact `Definition bearbeiten · filename.ini` link.

- [x] External INI snapshot service: verify definition location/IDS, permit only one replacement section, preserve original format/encoding and other sections, reject unsupported characters, detect external changes, verify working copy and create unique backup before replacement.
- [x] Current mission definitions use the live document, ordinary unsaved mission edits and mission undo. They do not write directly to disk.
- [x] External definition dialog explicitly says Speichern and identifies the full destination. Failed saves retain the editor contents. Successful saves invalidate installation metadata and refresh resource hints.
- [x] Remove duplicate origin buttons and expose one small link per definition in both source views.
- [x] Tests use only temporary INIs; validate source conflicts, bounded replacement, exact backup and preserved neighbors. WPF tests execute both external Save and current-mission Apply/Undo.

Validation: 68 Core and 18 MCP tests pass, all WPF smoke checks pass, full solution build has zero warnings/errors. The separate Groß öffnen action was retained; the screenshot was interpreted as feedback about repeated origin buttons, and the related optional clarification had not received an answer at implementation time.
