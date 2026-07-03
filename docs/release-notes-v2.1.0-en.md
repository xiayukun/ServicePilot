Chinese release notes: [release-notes-v2.1.0.md](release-notes-v2.1.0.md)

ServicePilot 2.1.0 improves discoverability for handing local service management to AI agents and refreshes the running UI after CLI configuration changes.

## Highlights

- Added `Copy help for AI` to the tray context menu. It opens a copyable window with the current absolute `ServicePilot.exe` path, recommended first commands, and a prompt users can paste into an AI assistant.
- Added `AiHelpContentService` so `ServicePilot.exe ai-help` and the tray AI help window share one content source, reducing drift across README, CLI, and UI guidance.
- After CLI configuration changes are routed through the running tray instance, ServicePilot classifies the command and refreshes the tray menu, service manager, template manager, and related log windows without requiring an app restart.
- README, user guide, AI usage guide, and screenshot guide now direct users to download and launch ServicePilot first, copy AI help from the tray, then let the AI inspect real state and create personalized services, templates, actions, and variables.
- Version properties are updated to `2.1.0`.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- The self-contained `ServicePilot.exe` from the release page does not require a separate .NET runtime installation.

## Safety Notes

- ServicePilot executes user-configured local scripts. The new AI help content includes the local absolute exe path; share it only with a trusted personal AI assistant, not in public web pages, issues, or logs.
- Set `SERVICEPILOT_CONFIG_DIR` for automation tests so real `%APPDATA%\\ServicePilot` configuration is not modified.

## Pre-release Checks

- [ ] `rtk dotnet build ServicePilot.sln`
- [ ] `ServicePilot.exe ai-help` and `ServicePilot.exe doctor --json` pass under a temporary config directory
- [ ] With a running tray instance, verify UI refresh after CLI `service add/edit/remove`, `step variable-*`, and `template add/edit/remove/import/apply/save-from-service`
- [ ] The tray AI help window supports text selection plus `Copy all` / `Copy commands`
- [ ] Upload `ServicePilot.exe` to GitHub Release
