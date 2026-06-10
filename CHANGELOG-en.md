# Changelog

[中文](CHANGELOG.md)

ServicePilot has not been publicly released yet. Internal pre-launch hardening is not kept as user-facing change history.

## 1.0.0 - To Be Released

The first public release will include:

- Windows tray-first local development service management.
- Large numeric tray icon showing the count of running/starting services.
- Chinese/English UI that follows Windows language by default and can be switched from the tray context menu.
- Service manager and template manager GUI.
- Multi-step Batch, PowerShell, Python, and Node.js scripts.
- Separate startup steps and manual-only steps.
- Service preset variables and step variables.
- Variable injection as `SERVICEPILOT_VARIABLE`, plus `{{variable}}` / `{{变量}}` replacement.
- Full service templates without working directories.
- Live log window with search, copy selected, copy all, horizontal scrolling, and bounded history.
- Windows Job Object process-group cleanup to reduce orphan npm/Vite child processes.
- AI/script-friendly CLI with `ai-help`, JSON queries, service/template CRUD, step execution, and step-variable maintenance.
- `doctor [--json]` configuration diagnostics for missing directories, empty steps, duplicate names, and duplicate variables.
- JSON output keeps Chinese text readable instead of escaping it by default.
- Configuration stored in `%APPDATA%/ServicePilot/config.json`.
- Variable last-use cache stored in `%APPDATA%/ServicePilot/variable-usage-cache.json`.

Pre-release validation still needs:

- More real-world development service start/stop/restart testing.
- Release package and GitHub Release download flow.
- README screenshots and release notes.
- Chinese and English screenshots prepared from `docs/screenshot-guide.md`.
