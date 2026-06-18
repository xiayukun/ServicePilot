# Changelog

[中文](CHANGELOG.md)

This changelog only records user-visible changes in public releases.

## 1.0.0 - 2026-06-10

The first public release includes:

- Windows tray-first local development service management.
- Large numeric tray icon showing the count of running/starting services.
- Chinese/English UI that follows Windows language by default and can be switched from the tray context menu.
- Service manager and template manager GUI.
- Multi-step Batch, PowerShell, Python, and Node.js scripts.
- Separate startup steps and manual-only steps.
- Service preset variables and step variables.
- Variable injection as `SERVICEPILOT_VARIABLE`, plus `{{variable}}` / `{{变量}}` replacement.
- Full service templates without working directories.
- Template import/export for sharing `.servicepilot-template.json` files.
- Built-in general "Default developer actions" template with Git, npm, and common tool opener actions.
- Live log window with search, copy selected, copy all, horizontal scrolling, and bounded history.
- Windows Job Object process-group cleanup to reduce orphan npm/Vite child processes.
- AI/script-friendly CLI with `ai-help`, JSON queries, service/template CRUD, step execution, and step-variable maintenance.
- `doctor [--json]` configuration diagnostics for missing directories, empty steps, duplicate names, and duplicate variables.
- JSON output keeps Chinese text readable instead of escaping it by default.
- Configuration stored in `%APPDATA%/ServicePilot/config.json`.
- Variable last-use cache stored in `%APPDATA%/ServicePilot/variable-usage-cache.json`.
## 2.0.0 - 2026-06-18

- Refactored the model to `Action` / `Composite`.
- Moved active configuration to `%APPDATA%/ServicePilot/config.v2.json`; legacy `config.json` is preserved.
- Migrated service-level preset variables to action-level `StepVariables`.
- Service/template editors now support composite member orchestration.
- Chinese UI/docs now use the action terminology consistently, and action-kind controls display localized `Action` / `Composite` labels.
- The log window removed the separate Start button and now runs from the unified Run action menu; log tabs are created lazily per action and switch when an action enters Running.
- The log window coalesces non-error webpack progress output at the display layer to reduce UI stalls from high-frequency build logs.
- CLI `start` runs the first composite, and `step run` can run an action or a composite.
- Template import/export preserves composite member relationships.
