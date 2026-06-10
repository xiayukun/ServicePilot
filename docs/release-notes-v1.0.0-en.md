# ServicePilot 1.0.0

Chinese release notes: [docs/release-notes-v1.0.0.md](release-notes-v1.0.0.md)

Initial public release baseline.

## Highlights

- Tray menu for service management.
- Numeric tray icon showing currently running/starting service count.
- Follows Windows language by default, with Chinese / English switching from the tray context menu.
- Multi-step script configuration with Batch, PowerShell, Python, and Node.js.
- Startup steps and manual-only steps, with service preset variables and step variables.
- Full service templates that reuse name, description, steps, and variables while preserving the target working directory.
- Log window with search, selected-copy, copy-all, horizontal scrolling, and bounded history.
- JSON-friendly CLI with `ai-help` and `doctor --json` for AI agents and automation scripts.
- Windows Job Object cleanup to reduce orphan npm/Vite child processes and port leaks.
- JSON configuration persisted to `%APPDATA%`.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- .NET 8.0 runtime, or download the self-contained executable.
