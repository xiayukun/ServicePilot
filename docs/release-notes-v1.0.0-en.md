Chinese release notes: [docs/release-notes-v1.0.0.md](release-notes-v1.0.0.md)

Initial public release baseline.

## Highlights

- Tray menu for service management.
- Numeric tray icon showing currently running/starting service count.
- Follows Windows language by default, with Chinese / English switching from the tray context menu.
- Multi-step script configuration with Batch, PowerShell, Python, and Node.js.
- Startup steps and manual-only steps, with service preset variables and step variables.
- Full service templates that reuse name, description, steps, and variables while preserving the target working directory.
- Templates can be exported/imported as `.servicepilot-template.json` files for sharing.
- Built-in general "Default developer actions" template with Git, npm, and common tool opener actions.
- Log window with search, selected-copy, copy-all, horizontal scrolling, and bounded history.
- JSON-friendly CLI with `ai-help` and `doctor --json` for AI agents and automation scripts.
- Windows Job Object cleanup to reduce orphan npm/Vite child processes and port leaks.
- JSON configuration persisted to `%APPDATA%`.
- Release package is a self-contained single-file `ServicePilot.exe`.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- No separate .NET runtime install is required when downloading `ServicePilot.exe`.
