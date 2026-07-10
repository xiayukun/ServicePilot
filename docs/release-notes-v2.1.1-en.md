Chinese release notes: [release-notes-v2.1.1.md](release-notes-v2.1.1.md)

ServicePilot 2.1.1 fixes the AI prompts (`ai-help` and the tray context menu "Copy help for AI") to explicitly state that **all configuration changes via the tray pipe take effect immediately — no restart is needed**, reducing redundant AI behavior of stopping and restarting the tray instance.

## Highlights

- `AiHelpContentService` Chinese and English AI prompts now include a clear statement that configuration changes routed through the tray pipe take effect immediately without restarting the tray instance, eliminating the redundant AI habit of always `stop` + restart.
- Version properties updated to `2.1.1`.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- The self-contained `ServicePilot.exe` from the release page does not require a separate .NET runtime installation.

## Safety Notes

- ServicePilot executes user-configured local scripts. AI help content includes the local absolute exe path; share it only with a trusted personal AI assistant, not in public web pages, issues, or logs.
- Set `SERVICEPILOT_CONFIG_DIR` for automation tests so real `%APPDATA%\ServicePilot` configuration is not modified.

## Pre-release Checks

- [x] `dotnet build ServicePilot.sln`
- [x] `ServicePilot.exe ai-help` and `ServicePilot.exe doctor --json` pass
- [x] With a running tray instance, verify UI refresh after CLI configuration changes
- [x] Upload `ServicePilot.exe` to GitHub Release