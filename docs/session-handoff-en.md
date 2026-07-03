# Session Handoff

Last updated: 2026-07-03

Chinese counterpart: [session-handoff.md](session-handoff.md)

## Current State

ServicePilot is a .NET 8 Windows tray-first developer service manager. The current product direction is tray menus, WPF management windows, log windows, and CLI automation. The desktop floating mode is intentionally removed.

The current mainline is ServicePilot 2.1:

- Project version properties should stay at `2.1.0` (`ServicePilot/ServicePilot.csproj`).
- Active config file: `%APPDATA%\ServicePilot\config.v2.json`.
- Legacy `%APPDATA%\ServicePilot\config.json` is read only as the v1 migration source. Do not delete or overwrite it.
- `SERVICEPILOT_CONFIG_DIR` is used for isolated tests so real user config is not touched.
- Runtime config details, private service names, local machine paths, backup filenames, customer project names, database/API addresses, and similar machine-specific details must not be written into committed docs.
- Local private handoff notes belong in `LOCAL_NOTES.private.md` at the repository root. That file is ignored by `.gitignore` and must not be committed.

## 2.0 Model

ServicePilot 2.0 uses the `Action` / `Composite` model:

- `Action` is a runnable command with script type, content, action-local variables, variable usage flag, and optional open-log behavior.
- `Composite` is an ordered action workflow. It stores member action ids and has no command content.
- A `Composite` cannot contain another `Composite`.
- Editor save validation should enforce: non-empty action command, existing composite members, at least one valid action member, and at most one variable-enabled member action per composite.
- `start SERVICE` runs the service's first `Composite`.
- `step run SERVICE ACTION_OR_COMPOSITE` can run either a single `Action` or a selected `Composite`.
- `RunOnStart` and service-level `PresetVariables` are legacy migration fields only and should not drive new UI behavior.

## Variables And AI Usage

- Action-local variables are stored in `ScriptStep.StepVariables`.
- When `UseVariable=true`, the selected variable is injected as `SERVICEPILOT_VARIABLE` and replaces `{{variable}}` / `{{变量}}` in script content.
- When `UseVariable=false`, the action runs directly and should not show a variable submenu.
- Recent variable and recent service ordering is cached in `%APPDATA%\ServicePilot\variable-usage-cache.json`; it is not source-of-truth config.
- `ai-help` is the AI/script entrypoint. Future CLI changes must let agents inspect state first through `doctor --json`, `list --json`, `status --json`, `step list --json`, and `logs --json`.
- The tray context menu provides `Copy help for AI`; `Views/AiHelpWindow` displays the current absolute `ServicePilot.exe` path, recommended first commands, and a copyable prompt.
- `AiHelpContentService` is the shared content service for `ServicePilot.exe ai-help` and the tray AI help prompt. Future AI guidance updates should start there.
- Public docs, repository profile text, and release copy should direct GitHub download users to launch the exe first and copy help for AI from the tray, so agents do not have to guess the downloaded exe location.
- AI-facing CLI output should stay structured, readable, and explicit about failures. Do not require agents to parse UI labels.

## UI State

- User-facing Chinese terminology should use "动作" and "组合动作"; do not call normal operations "步骤".
- Action-kind dropdowns display "动作 / 组合动作" in Chinese and "Action / Composite" in English.
- The log window no longer has a standalone Start button. The Run Action menu runs the first composite, a selected composite, or a single action.
- Log window tabs are created lazily: no default All tab and no default Service tab. When an action enters `Running`, the log window activates that action tab even if the tab already exists. System logs without an action name create the Service tab only when such logs actually exist.
- Continuous output must not repeatedly steal the user's active tab just because new log lines arrive; tab switching is driven by action runtime state.
- The log window should keep search, copy, horizontal scrolling, and auto-scroll. Each visible tab renders at most the latest 5,000 lines and batches high-frequency output so webpack/Vite progress logs cannot freeze the UI.
- The log window coalesces non-error `[webpack.Progress] NN% ...` lines into one visible line with a text progress bar. This is display-layer compaction only; raw buffer and CLI JSON logs should remain intact.
- Tray tooltip/status text should show only active count, total count, and failed count. Do not include service names or variable values there.
- Tray and manager service lists are sorted by recent use first without mutating persisted `SortOrder`.
- After CLI configuration changes are routed through the running tray instance, `App.RefreshAfterCommand` classifies the command and refreshes the tray menu, open service manager, open template manager, and related log windows.

## Packaging And Release

- Normal build check: `rtk dotnet build ServicePilot.sln`.
- Single-file publish command: `rtk dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist`.
- `Release` publish defaults should produce a single `ServicePilot.exe`.
- If the running exe locks `dist`, publish to `dist-staged` first.
- After successfully producing an exe, follow the local private copy target in `LOCAL_NOTES.private.md` when that file exists. Do not copy that target path into committed docs.
- Current user instruction: produce local exe builds for testing only. Do not commit, tag, or publish a GitHub Release unless explicitly asked.
- 2.1.0 public release-note drafts live in `docs/release-notes-v2.1.0.md` / `docs/release-notes-v2.1.0-en.md`; GitHub Release pages already show the title, so the notes body should not add a duplicate top-level heading.

## Documentation Rules

- Chinese is the primary documentation language. English counterparts use `-en.md`.
- When user-visible behavior changes, update `AGENTS.md`, this handoff, the English handoff, and related README / user guide / ai-usage / changelog files.
- Current user-facing docs for new users should say actions/composites; keep step/步骤 only in historical release notes or compatibility CLI names.
- Sensitive details must not be written into README files, user guides, handoff docs, AGENTS, release notes, or issue/PR templates.
- If local deployment targets, private services, customer projects, or screenshot source details must be remembered, write them to `LOCAL_NOTES.private.md`.

## Verification Suggestions

Run at least this after functional changes:

```text
rtk dotnet build ServicePilot.sln
```

For config migration or CLI work, verify with an isolated directory:

```text
set SERVICEPILOT_CONFIG_DIR=<temporary-test-dir>
ServicePilot.exe doctor --json
ServicePilot.exe ai-help
ServicePilot.exe list --json
ServicePilot.exe step list SERVICE --json
```

For runtime behavior, also verify:

- The first `Composite` can run.
- A selected `Composite` can run.
- A single `Action` can run.
- `UseVariable=false` actions do not show variable menus.
- Adding a variable stores it on the action and updates recent-use sorting.
- `Stop` stops all running content for that service.
