# Versioning, release workflow and About dialog

User scope: MinVer for all modules; version in title; About with module versions and references;
release.yml. User requested a more polished About layout and only the requested public references in the dialog.

- Central MinVer8, v tag prefix, initial0.1, preview.0. Shared repository version for all projects.
- New commits alter the computed development version at the next build; repeating a push does not.
- Actual GUI and Core assembly versions shown. Separately shipped CLI uses the shared repository
  build version and is labeled separately. Packaging compares actual GUI/Core/CLI product versions.
- Refined About uses existing icon, clear typography, compact module table, short revision,
  references and copyable full build information. Excluded reference removed from visible UI.
- Branch and PR builds use full history and upload artifacts. Tags trigger release workflow.
- Release packages are self-contained Windows x64 GUI/CLI ZIPs plus SHA256 sums.
- Manual branch run packages only; no automatic release tags or self-modifying version commits.

Validation: isolated temporary Git repository proved 0.1.0-preview.0 -> 0.1.0-preview.0.1 across
commits and exact1.2.3 at tagv1.2.3. Local publish/ZIP generation passed. Actionlint1.7.12 passed
both workflows. WPF smokechecks confirmed title version and all three module rows. Actual WPF
About rendering inspected: no clipped fields, compact version rows, all references and buttons
visible. Independent code review found no Important/Critical issues.

No release tag was created in the actual repository, and no GitHub release was published during
verification. Native multi-monitor/DPI checks remain outside the local validation.
