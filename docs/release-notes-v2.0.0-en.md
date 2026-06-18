English release notes: this file

ServicePilot 2.0.0 is a configuration-model refactor release.

## Highlights

- Added the `Action` / `Composite` model: actions run commands, composites orchestrate actions.
- Moved active configuration to `config.v2.json`; legacy `config.json` is preserved.
- Migrated legacy service-level preset variables to action-level variables.
- Service and template editors now support composite member selection and ordering.
- Chinese UI/docs now consistently use action terminology, and action-kind controls display localized Action / Composite labels.
- The log window removed the separate Start button and now runs from the unified Run action menu; tabs are created lazily per action and switch when an action enters Running.
- The log window coalesces non-error webpack progress output to reduce UI stalls from high-frequency build logs.
- CLI `start SERVICE` runs the first composite, and `step run` can run an action or a composite.
- Template import/export preserves composite member relationships.

## Verification

- `rtk dotnet build ServicePilot.sln`
- Isolated empty-config `doctor --json`
- v1 `config.json` auto-migration to `config.v2.json`
- CLI `service add --step ...` auto-creates the `启动` composite action
