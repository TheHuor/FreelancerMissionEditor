# Integration review — resource browser Task 3

Scope: plan/spec, integration diff, SourceLineView, SourceWindow, MainWindow.Resources, MainViewModel.Resources and ResourceLinkItem. Static review only; no tests rerun while Core implementation is in progress. No browser-draft/native-writer review.

## Findings

1. **P1 — Large source window cannot resolve the private LinkButton style.** Original `SourceLineView.cs:59` calls `FindResource("LinkButton")`; that key exists only in `MainWindow.xaml` Window.Resources. A separate owned Window does not inherit its owner's resource dictionary, so resource rows in SourceWindow throw. Parent reports this fixed with a dedicated template; verify in the eventual source-window smoke run.

2. **P1 — Existing LinkButton template stringifies new TextBlock Content.** Original `SourceLineView.cs:55-59` supplies TextBlock Content to a template whose root is `<TextBlock Text="{TemplateBinding Content}">`. This loses the intended content presenter and truncation behavior. Parent reports a dedicated ContentPresenter template fixes this; verify actual labels and bounded width visually.

3. **P2 — Valid noncanonical numeric IDs are not inline links.** `SourceLineView.cs:85-86` requires raw token equality with `Ids.ToString()`. Core recognizes `Act_PopUpDialog = 065537, +65538` through int.TryParse, but neither numeric token becomes a Hyperlink. Only their hint rows remain clickable, contrary to the requirement that numeric IDs in both source views are links. Compare parsed numeric value at the known parameter position while retaining the original token as Run text. Test actual Hyperlink inlines, not only ResourceLinks.Count.

4. **P2 — Resource refresh resets a non-trigger source location.** `MainViewModel.Resources.cs:14-18` re-analyzes after every resource save. Analyze remembers only SelectedTrigger.Name, so after navigating a browser usage to NNObjective/MsnShip, saving the resource selects the first trigger and replaces the shown source section. Preserve the currently focused section and exact usage line during resource-only hint refresh. Reproduce with ShowSourceLine on a non-trigger, then RefreshResourceHints; DetailsTitle and source row should stay there.

5. **P2 — Indirect definition source INI is not reachable separately.** `ResourceLinkItem.cs:13-15` relegates provenance to a tooltip, and SourceLineView has only the resource-ID action. No source-file action/event is exposed for the definition INI. The approved object-resolution section explicitly requires the origin INI to remain separately reachable; all current clicks lead to the resource DLL. Add an explicit definition-source navigation/copy/open action using structured provenance, without trying to parse the display text.

## Verdict

Task integration needs the three remaining P2 changes and verification of the two reported P1 fixes. Existing right splitter/bottom findings, singleton source/browser instances, pre-transition main-window guards and live reference rebuild hooks are present. Tests have not been run by this reviewer.

## Resolution after review

All five findings were corrected. Source links use a local ContentPresenter template, compare parsed numeric values while preserving source spelling, retain non-trigger sections on resource refresh, and expose a structured definition-file action. The definition viewer uses the live document for the current mission and handles optional definition line numbers. WPF checks exercise actual hyperlink events for plus/leading-zero IDs and non-trigger context preservation.
