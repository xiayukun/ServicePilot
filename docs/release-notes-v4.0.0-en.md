Chinese release notes: [release-notes-v4.0.0.md](https://github.com/xiayukun/ServicePilot/blob/main/docs/release-notes-v4.0.0.md) | English: this file

ServicePilot 4.0.0 is a major feature release: a programmable **log merge script engine** with VSCode-style log folding, a right-side color overview for navigation, external config-file hot-reload, and a fully refreshed brand icon.

## Highlights

- **Log Merge Script engine**: Step/template actions support a custom C# merge function that folds multiple log lines into a single colored summary. Script inputs now include `PreviousResult`, `PreviousWasCollapsed`, `InCollapseGroup`, and the result adds a `State` dictionary for carrying cross-line state (runtime only, not persisted). The service/template editor gained a merge-function code box pre-filled with a documented template. A `merge-script test` CLI command is available.
- **VSCode-style log folding**: Raw lines are kept while expandable fold groups are created; collapsed groups show only the summary. Searching a match inside a fold auto-expands it. A "Fold all / Expand all" summary toggle was added. Fold placeholder text is white, with a ~100px color block (using the first folded line's color) between the plus marker and the text.
- **Right-side color overview navigation**: A static clickable color overview aggregates per-line colors by priority (Error > Warning > Custom > System > Normal) for quick navigation; it is folding-aware and reflects only visible logs.
- **External config hot-reload**: Editing `%APPDATA%\ServicePilot\config.v2.json` directly is detected and hot-reloaded by the tray without overwriting external edits, preserving running services' runtime state.
- **Scrollable menus + system accent color**: Tray menus at all levels scroll when items overflow and share consistent styling; the service-manager selection bar now uses the Windows system accent color.
- **New app icon**: A more refined transparent teal squircle icon, applied to the exe, taskbar, and every window title bar, removing the old icon's white halo.
- **Log window title bar**: The full title now lives in the title bar; `ai-help` restores full CLI help.

## Download

- `ServicePilot.exe`

## Requirements

- Windows
- The self-contained `ServicePilot.exe` from the release page does not require a separate .NET runtime.

## Security Notes

- ServicePilot executes user-configured local scripts. Merge scripts are also user-provided C# code running inside the app process, so only use merge scripts you trust.
- AI help content includes the local exe path and should only be sent to trusted personal AI assistants, not posted publicly.
- For automated testing, set `SERVICEPILOT_CONFIG_DIR` to avoid modifying the real `%APPDATA%\ServicePilot` configuration.
