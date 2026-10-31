# Layout execution ledger

Plan: [WPF layout plan](2026-10-31-mission-editor-layout-plan.md)

## Completed

- User authorized implementation on codex/mission-editor-layout-plan.
- Baseline build succeeded; existing suite 20/20 passed.
- Tasks 1–4 implemented: central theme, main layout, findings/reference tabs, source scrolling, findings drawer, graph palette/patterns, section editor.
- Task 5: build, core tests, actual desktop inspection and portable WPF smoke checks performed. No parser, core routing or persistence code changed.
- Independent static review completed. Findings fixed: severity badge clipping, unbounded detail headings, source/workspace minimum height, drawer restoration after shrinking window. A targeted drawer regression failed before the height fix and passed afterwards.
- Smoke checks cover 1280x800 and 1920x1080 logical window sizes, drawer collapse/restore/navigation, duplicate source sections, edit/undo/save/backup on temporary copy and text contrast across principal surfaces.
- Actual UI inspected with the 36,830-line fixture: graph, duplicate finding navigation, section editor, collapsed drawer, maximized and restored window. The final Computer Use capture retry failed twice with `foreground window did not report a process id`; no further UI input was attempted after recovery failed.

## Rulings

- Use the user-created feature branch in place; no additional worktree or branch.
- Verify cosmetic resources/layout through compilation, contrast measurement and desktop inspection; avoid tests mirroring XAML. Add portable WPF smoke checks for changed drawer/navigation behavior and original editing contract.
- Raise error foreground from #F07878 to #FF8B8B: original contrast on selection was 4.22:1; revised contrast is 5.13:1. This is a minor visual adjustment to the selected design for readability.
- Keep native title bars and operating-system file/message dialogs, as specified. No campaign functionality.
- Smoke checks are an explicit Windows desktop executable outside the solution test runner; run with `dotnet run --project tests/MissionEditor.App.SmokeChecks`. They briefly open a window and need an interactive Windows desktop.

## Limits / deferred minor

- Native 150% and 200% DPI were not exercised; user's display settings were not changed. Logical-size checks are not a substitute for those checks.
- Error table rows use readable normal text with a severity symbol; the cell style overrides the optional row text tint. No functional severity information is lost.
- Full manual keyboard/pan/zoom regression was not completed after the Computer Use helper failed; commands and handlers remain wired, with drawer/navigation covered by smoke checks.
