Chinese release notes: [release-notes-v3.0.0.md](https://github.com/xiayukun/ServicePilot/blob/main/docs/release-notes-v3.0.0.md) | English: this file

ServicePilot 3.0.0 brings a **FluentWindow modern UI overhaul**: all management windows migrated to WPF-UI FluentWindow with TitleBar, system accent color selection effects, and unified dark theme.

## Highlights

- **FluentWindow UI overhaul**: All management windows (AI Help, Log, Service Manager/Editor, Template Manager/Editor, Template Select, Variable Input) migrated to WPF-UI `FluentWindow` with `ExtendsContentIntoTitleBar`, enabling modern title bars with min/max/close.
- **Unified dark theme**: Tray menu items now show system-accent-color hover highlight with 3px left selection bar; list item selection shows accent left border; all text controls use consistent dark foreground color.
- **Log window toolbar refactor**: Button styles upgraded to WPF-UI Primary/Danger appearance with consistent spacing and rounded corners.
- **AI Help window migration**: Added TitleBar, dark background adaptation, removed legacy title bar.
- **ServiceCommandProcessor enhancement**: New CLI template operations and expanded capabilities.
- **ServiceManager UI alignment**: List item selection styles and button layouts aligned with FluentWindow design system.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- The self-contained `ServicePilot.exe` from the release page does not require a separate .NET runtime.

## Security Notes

- ServicePilot executes user-configured local scripts. AI help content includes the local exe path and should only be sent to trusted personal AI assistants, not posted publicly.
- For automated testing, set `SERVICEPILOT_CONFIG_DIR` to avoid modifying the real `%APPDATA%\ServicePilot` configuration.
