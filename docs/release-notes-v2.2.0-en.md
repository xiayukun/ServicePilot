Chinese release notes: [release-notes-v2.2.0.md](release-notes-v2.2.0.md)

ServicePilot 2.2.0 introduces **step-level incremental editing CLI**: AI agents and scripts can now add, edit, remove, and reorder individual action steps without recreating the entire service.

## Highlights

- **`step add`**: Add a new action to a service, with position control (`--position end|N|after:STEP|before:STEP`) and optional composite membership (`--into-composite COMPOSITE`).
- **`step edit`**: Modify an action's name, script type, script content, variable toggle, or open-log-on-run behavior.
- **`step remove`**: Remove an action; auto-cleans composite member references.
- **`step move`**: Reorder actions by numeric position, or relative to another step (`after:STEP`, `before:STEP`).
- **`AiHelpContentService`** and `ai-help` CLI updated with the new command examples for better AI discoverability.
- After running `step add/edit/remove/move` through the running tray pipe, the tray menu and open manager/log windows refresh immediately.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- The self-contained `ServicePilot.exe` from the release page does not require a separate .NET runtime installation.

## Safety Notes

- ServicePilot executes user-configured local scripts. AI help content includes the local absolute exe path; share it only with a trusted personal AI assistant, not in public web pages, issues, or logs.
- Set `SERVICEPILOT_CONFIG_DIR` for automation tests so real `%APPDATA%\ServicePilot` configuration is not modified.

## Pre-release Checks

- [ ] `dotnet build ServicePilot.sln`
- [ ] `ServicePilot.exe ai-help` and `ServicePilot.exe doctor --json` pass
- [ ] With a running tray instance, verify UI refresh after `step add/edit/remove/move`
- [ ] Upload `ServicePilot.exe` to GitHub Release