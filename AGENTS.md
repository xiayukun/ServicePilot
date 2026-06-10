@C:\Users\11467\.codex\RTK.md

--- project-doc ---

# ServicePilot Agent Notes

@C:\Users\11467\.codex\RTK.md

## Project State

ServicePilot is a Windows tray-first developer service manager built with .NET 8, WPF dialogs, and WinForms `NotifyIcon`.

Treat the codebase as actively hardening for public release. Inspect the current workspace before changing behavior, build after changes, and update this file whenever architecture, workflows, commands, or release rules change.

Current repository facts:

- Workspace path: `C:\git\其他\ServicePilot`
- Solution: `ServicePilot.sln`
- App project: `ServicePilot\ServicePilot.csproj`
- Main startup: `ServicePilot\App.xaml.cs`
- Runtime config path: `%APPDATA%\ServicePilot\config.json`
- Preset variable usage cache path: `%APPDATA%\ServicePilot\variable-usage-cache.json`
- Test-only config override: set `SERVICEPILOT_CONFIG_DIR` before launching the exe.
- Runtime target: `net8.0-windows`
- Public release version: `1.0.0`.
- `OutputType` is `Exe` so CLI calls are synchronous and capture-friendly. No-argument tray startup calls `FreeConsole()`.
- `Release` publish defaults are in `ServicePilot\ServicePilot.csproj`: `win-x64`, self-contained, compressed single-file, no debug symbols. The normal package command is `rtk dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist`, and `dist` should contain only `ServicePilot.exe`.
- This directory is currently a Git repository on branch `main`. Still check `git status` before edits because user screenshots/assets may be untracked.
- Process-runner design references are summarized in `docs/process-runner-research.md` and `docs/process-runner-research-en.md`.
- Competitive code research is summarized in `docs/competitive-research.md` and `docs/competitive-research-en.md`.
- AI/automation usage is documented in `docs/ai-usage.md` and `docs/ai-usage-en.md`.
- GitHub launch metadata is documented in `docs/github-launch-checklist.md`, `docs/github-launch-checklist-en.md`, `docs/repository-profile.md`, and `docs/repository-profile-en.md`.
- Screenshot planning is documented in `docs/screenshot-guide.md` and `docs/screenshot-guide-en.md`.
- The complete user guide lives in `docs/user-guide.md` and `docs/user-guide-en.md`; keep README concise and link to the guide for details.
- The current user runtime config includes Java services/templates for `leniu-tengyun-core` and `leniu-tengyun`; both Java service/template definitions include Notepad opener steps for root `pom.xml`. The API service/template also includes a Notepad opener for `bootstrap-dev.yml` and a database-url mutation step that both use source-path-first and `target\classes` fallback lookup. These Java file-opener steps belong at the very end of the step list.

## Required Workflow

Before making changes:

1. Read this `AGENTS.md`.
2. Read `docs/session-handoff.md` and `docs/session-handoff-en.md` if present.
3. Check whether the directory is a Git repository.
4. Inspect relevant code instead of relying on memory.
5. Do not revert user changes or generated artifacts unless the user explicitly asks.

When using shell commands, follow `RTK.md`: prefix executable commands with `rtk`. For PowerShell built-ins, use `rtk powershell.exe -NoProfile -Command "..."`.

Before finishing a coding turn:

1. Run `rtk dotnet build ServicePilot.sln`.
2. Update this file if architecture, commands, behavior, or maintenance rules changed.
3. Update `docs/session-handoff.md` and `docs/session-handoff-en.md`.
4. Keep Chinese and English Markdown documents aligned when user-facing docs change.

## Product Direction

Primary behavior:

- No floating desktop panel.
- The app runs as a taskbar notification-area tray tool.
- Management happens from the tray menu, WPF dialogs, log windows, and CLI arguments.
- CLI arguments are intended for AI/script automation and should cover visible tray operations plus maintenance operations.
- `ai-help` is the AI entrypoint. Keep it concise, current, and safe: agents should inspect `list/status/service/step/template/logs --json` before acting.
- `doctor [--json]` is the preflight configuration diagnostic entrypoint. Keep it offline-capable and useful for AI before edits/startup.
- The tray icon is generated dynamically as a large number only. It shows the count of `Running`/`Starting` services and displays `0` when none are active.
- The tray tooltip and disabled status line show only a short count summary: active/running count, total services, and failed count. Do not include service names or variables there because long values make the tray menu unusable.
- UI language defaults to the Windows UI language. Users can switch between `auto`, `zh-CN`, and `en-US` from the tray context menu.

## Architecture

Startup and app lifetime:

- `App.xaml` uses `ShutdownMode="OnExplicitShutdown"`.
- `App.xaml.cs` handles startup, tray menu creation, single-instance mutex, command pipe server, log buffers, and clean shutdown.
- No-argument startup creates the tray instance.
- Argument startup runs command-line mode through `CommandLineHost` and does not create the tray UI.
- `CommandPipeServer` hosts the named pipe `ServicePilot.Command.v1`.
- `ConfigService` stores config in `%APPDATA%\ServicePilot` by default. If the Roaming config/cache file does not exist, it copies legacy `config.json` and `variable-usage-cache.json` from the exe directory or current directory without deleting the legacy files.

Service model:

- `ServiceConfig` stores persistent service definitions.
- `ScriptStep` stores ordered script steps.
- `ServiceConfig.PresetVariables` stores optional strings selectable at start/restart/step-run time.
- `ScriptStep.UseVariable` controls whether the selected service variable applies to that step. Old configs default to `true`; when `false`, the step does not receive `SERVICEPILOT_VARIABLE`, does not replace variable placeholders, and execute-step menus run it directly without a variable submenu.
- `ScriptStep.RunOnStart` controls whether normal service startup runs the step. Old configs default to `true`; when `false`, the step is skipped during normal startup but remains manually runnable from execute-step menus.
- `ScriptStep.OpenLogOnRun` controls whether the service log window opens automatically when that step enters `Running`. It is optional and defaults to `false` for old configs.
- Enable `OpenLogOnRun` for manual diagnostic/progress steps such as Git operations, dependency install, build/publish/package commands, Java/Maven/.NET/npm/Python checks, and CLI/doctor/check commands. Keep normal service startup steps (`RunOnStart=true`) off by default so starting a service does not automatically pop logs. Keep pure `打开：...` / `Open ...` tool-launcher steps off unless the user explicitly wants logs for them.
- `ScriptStep.StepVariables` stores per-step variables for `RunOnStart=false` manual steps. Startup steps use `ServiceConfig.PresetVariables`; manual-only steps use their own `StepVariables`.
- `ScriptStep.Order` remains a zero-based persisted execution order. UI display is separate: startup steps are numbered from `1` within the `启动执行` group, while `不启动执行` steps show no number.
- `PresetVariableUsageStore` stores last-use ordering in `variable-usage-cache.json` under the same directory as config. It tracks both preset/step variable ordering and recent service usage. It is a cache, not source-of-truth configuration.
- `ServiceTemplate` stores a full service template except working directory: name, description, script steps, preset variables, and timestamps.
- `AppConfig.ServiceTemplates` stores user-managed full service templates.
- `TemplateExchangeService` exports/imports shareable `.servicepilot-template.json` files. Import creates fresh template/step ids and auto-renames duplicates instead of overwriting existing templates.
- Applying a service template preserves the target service name when it is already non-empty. The template name is used only for an empty target name; steps and preset variables are still replaced.
- `AppConfig.Settings.Language` stores the UI language preference: `auto`, `zh-CN`, or `en-US`. Missing or unknown values are treated as `auto`.
- `AppConfig.Settings.BuiltInTemplatesSeeded` records whether first-run built-in templates have already been added.
- `ServiceTemplateService.CreateBuiltInTemplates()` is the single place to change the editable default developer template. No-argument tray startup seeds it once when `BuiltInTemplatesSeeded=false`; deleting it later should not cause it to be recreated every launch.
- The current built-in `默认开发动作模板` is a 20-step generic developer toolbox: Git pull, safe/force branch checkout with rough `1.0.0`/`2.0.0` branch variables, safe/force tag checkout, npm install/build, and common app openers including Explorer, CMD, PowerShell, Windows Terminal, Git Bash, VS Code, Cursor, Visual Studio, IntelliJ IDEA, WebStorm, Rider, Notepad++, and Postman.
- Built-in template generation uses the same `OpenLogOnRun` heuristic: manual Git/npm/build-style action steps may pop logs; normal startup-flow and pure opener steps do not by default.
- Steps that open GUI apps or terminals must not use plain `Start-Process` from a normal PowerShell child process. ServicePilot assigns processes to a kill-on-close Job Object, so direct child apps can be closed when the step ends. Use the detached `.lnk` + `explorer.exe` pattern from `ServiceTemplateService.DetachedOpenHeader()` / existing working services; Explorer folder opens may use COM `Shell.Application.Open`.
- `ServiceStartOptions.Variable` carries one selected preset variable for a run.
- `ServiceStartOptions.OnlyStepId` runs only one selected step.
- `ServiceRuntimeState.StepStates` stores in-memory per-step runtime state. It is not persisted to config.
- The old subservice design has been removed. Do not reintroduce service-local subservices unless the user explicitly asks.
- The old keep-running checkbox has been removed. A final command that keeps running naturally keeps the service running.

Execution path:

- `ProcessManager` owns runtime states and service lifecycle.
- `App.RebuildTrayMenu` must read `ServiceRuntimeState.State` for live menu state; `ServiceItemViewModel.State` can lag by one dispatcher tick.
- Executor cleanup in `ProcessManager` must only remove the executor/cancellation token instance it created, so short completed steps can be run again immediately without losing the new executor.
- `ScriptExecutor` runs ordered steps whose `RunOnStart` is `true`, or a single selected step when `OnlyStepId` is set.
- `ScriptExecutor.StepStateChanged` updates step states: `NotRun`, `Running`, `Succeeded`, `Failed`, `Skipped`, and `Cancelled`.
- `App.OnProcessStepStateChanged` rebuilds the tray menu and opens the log window for a step whose persisted `OpenLogOnRun` is `true`.
- Single-step execution uses a separate runtime path and is allowed while the service is running unless that same step is already `Running`.
- When a single step is executed while the service is otherwise stopped, `ProcessManager.RunStep` promotes the service to `Running` for the duration of that step, then `CompleteIdleServiceState` moves it to `Completed`, `StartFailed`, or `Stopped` after all main and standalone executors are gone.
- `CompleteIdleServiceState` must ignore stale step failures from before the current run by using `ServiceRuntimeState.StartTime` as the relevance boundary. This prevents old manual-step failures from poisoning later successful step runs.
- `ScriptDefinitionService.CreateRunnableStep` clones a step and replaces `{{variable}}` / `{{变量}}` with the selected variable only when `ScriptStep.UseVariable` is `true`.
- `ProcessRunner` injects the selected variable as environment variable `SERVICEPILOT_VARIABLE` only for steps whose `UseVariable` is `true`.
- Batch steps run through `cmd.exe /d /s /c` with a `chcp 65001` prefix.
- Output is read as raw bytes per line, decoded as strict UTF-8 first, then the current OEM code page as fallback.
- Do not treat all stderr output as errors. Many dev tools write progress to stderr. `ProcessRunner` classifies known benign stderr, such as webpack progress, as `Info`; final failures still come from nonzero exit codes and explicit system error logs.
- Do not write Batch temp files with a UTF-8 BOM; `cmd.exe` treats the BOM as part of the first command on this system.
- PowerShell, Python, and Node steps use temporary script files.
- PowerShell temporary scripts must be written as UTF-8 with BOM because Windows PowerShell 5 reads UTF-8 without BOM as the local ANSI code page and can corrupt Chinese string literals into parse errors. Batch temporary scripts must remain UTF-8 without BOM because `cmd.exe` treats a BOM as part of the first command on this system.
- Every launched process is assigned to a Windows Job Object configured with kill-on-close. This is required for `npm run dev` / Vite, whose final `node.exe` can outlive `cmd.exe`/`npm` and otherwise keep ports like 3000 open.
- A step must exit with `0` before the next step runs.
- Steps with `RunOnStart=false` are marked `Skipped` for a normal service startup and are grouped under `不启动执行` in execute-step menus.
- Empty script steps are ignored at runtime for backward compatibility with older configs; the GUI prevents saving new empty steps.
- Starting any main service step process marks the service as `Running`; a short service still becomes `Completed` after all selected steps exit successfully.
- A final step that exits successfully becomes `Completed`; a failed final step becomes `StartFailed`.
- Volta shim failures with exit code `126` and Volta-specific output are retried narrowly before marking startup failed.
- Stop cancels the executor, closes the Windows Job Object, and then falls back to process-tree kill.
- `ProcessRunner` treats `Win32Exception (6)` / invalid process handles as already exited during stop/dispose.
- `CommandPipeServer.Dispose` is idempotent because exit paths can call it more than once.
- CLI handlers that save config must stay async all the way through. Do not block async config writes with `.GetAwaiter().GetResult()` on the WPF startup path; it can deadlock command mode and leave `ServicePilot.exe` processes behind.
- JSON CLI output uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so Chinese diagnostics remain directly readable.

Tray and dialogs:

- Tray menu is built in `App.RebuildTrayMenu`.
- Per-service tray submenu includes start, execute step, stop, restart, view logs, edit, delete, and save as template.
- Start and restart become variable submenus when the service has preset variables. Without variables they are direct actions.
- Variable submenus are sorted by `PresetVariableUsageStore`; selecting an existing variable records usage but does not reorder `ServiceConfig.PresetVariables`.
- Variable submenus end with `新增`. The dialog defaults to the most recently used variable, saves a new variable into the service if needed, records usage, and immediately executes the selected action.
- The tray service list is sorted by recent service usage first, then by persisted `SortOrder`/name. Starting, stopping, restarting, running a step, viewing logs, editing, deleting, saving as template, and equivalent tray-routed CLI actions should call `PresetVariableUsageStore.RememberService`.
- CLI `--variable` records usage in the cache when routed through the tray instance, but it does not automatically add the value to `ServiceConfig.PresetVariables`; use service edit/add commands for persistent preset list maintenance.
- CLI `step run SERVICE STEP --variable VALUE` records variable usage against the service for startup steps and against the step id for manual-only steps.
- Template step variables are also CLI-manageable via `template step-variables`, `template step-variable-add`, `template step-variable-remove`, and `template step-variable-clear`.
- Tray service menus and execute-step menus should not prefix every item with verbose state text. Use colored status dots only for attention-worthy states: green for running/starting, red for failed/error, orange for stopping/cancelled. Stopped/not-run/succeeded items normally have no dot; detailed state can remain in tooltip text.
- Execute step menus group steps into `启动执行` and `不启动执行`. Startup steps use service preset variables; manual-only steps use `ScriptStep.StepVariables` and the usage-cache key is the step id. Manual-only steps with `UseVariable=true` still show a variable submenu with `新增` even when the step variable list is empty. Steps with `UseVariable=false` run directly. A step with `Running` state is disabled, but other steps remain executable even while the service is running.
- Service add/edit dialog: `Views\ServiceConfigDialog.xaml(.cs)`. It supports applying one full service template, saving an edited service draft as a template, editing steps, and editing preset variables.
- Service and template step editors include `使用变量`, `启动执行`, and `弹出日志` checkboxes next to script type. The left variable box shows service `预设变量` for startup steps and switches to per-step `手动执行变量` for manual-only steps.
- Service manager: `Views\ServiceManagerWindow.xaml(.cs)` supports service add/edit/delete/start/execute-step/stop/restart/logs/save-as-template. Start/restart use variable menus when presets exist. Its service grid binds to a sorted snapshot from `PresetVariableUsageStore.SortServices`, so the most recently used service is shown at the top without mutating `ServiceConfig.SortOrder`.
- `ServiceManagerWindow` buttons must be enabled from the selected row's live `RuntimeState`: start only for stopped/error/start-failed/completed, stop for running/starting/stopping or running steps, and restart except while starting/stopping.
- WPF `MenuItem.Header` treats underscores as access-key markers. When displaying user data such as variables or step labels in service manager or log-window context menus, wrap the string in a `TextBlock` rather than assigning it directly as `Header`.
- Template manager: `Views\TemplateManagerWindow.xaml(.cs)` supports full service template CRUD plus import/export of shareable template JSON files.
- Template editor: `Views\ServiceTemplateDialog.xaml(.cs)`.
- Log window: `Views\LogWindow.xaml(.cs)` receives a `ServiceItemViewModel`, `ProcessManager`, `PresetVariableUsageStore`, preset-variable save callback, step-variable save callback, and service edit callback. It offers variable-aware start, execute-step, and restart controls, edit, stop, bounded in-memory logs, search, copy, and horizontal scrolling for long lines. It subscribes to service/step state changes and must disable Start while the service is running/starting or any step is running. Opening a log window must use `LoadLogs()` for buffered history and throttled auto-scroll; do not replay cached logs by calling `AddLog()` in a loop.
- Log window title text should be clipped with ellipsis and must never push action buttons off-screen. Service names are user data and less important than keeping controls clickable.
- Log buffers are capped at 20,000 entries in both `App.OnServiceOutput` and `LogWindow`; keep any future increase bounded.
- `Views\PresetVariableInputDialog.xaml(.cs)` is the small input dialog used by tray and service manager variable `新增`.
- Log window action buttons use `LogActionButtonStyle`; disabled button foreground must stay black for readability against WPF's disabled button background.
- Runtime/system failure logs trigger a throttled tray balloon from `App.OnServiceOutput`. Do not notify on every stderr line; notify on final/system failures so retry noise does not spam the user.
- UI text that appears in tray menus, WPF management windows, log windows, template windows, and variable dialogs should use `LocalizationService.Current` rather than hard-coded Chinese/English. User data such as service names, step names, variables, and script content must not be translated.

Command-line / AI control:

- `CommandLineHost` handles argument mode and first tries to send commands to the running tray instance.
- When `SERVICEPILOT_CONFIG_DIR` is set, `CommandLineHost` skips the global tray command pipe by default to protect isolated tests from touching the user's real config. Set `SERVICEPILOT_ALLOW_TRAY_PIPE=1` only for intentional pipe routing.
- Start/stop/restart/step-run/log/shutdown commands require the tray instance to be running.
- Offline commands such as `help`, `config-path`, `list`, `status`, `add`, and template/service config edits can operate from config where possible.
- For CLI tests, set `SERVICEPILOT_CONFIG_DIR` to avoid modifying the user's real `%APPDATA%\ServicePilot\config.json`.

Supported commands:

```text
ServicePilot.exe help
ServicePilot.exe version
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor [--json]
ServicePilot.exe list [--json]
ServicePilot.exe status [all|SERVICE] [--json]
ServicePilot.exe start SERVICE [--variable VALUE]
ServicePilot.exe stop all|SERVICE
ServicePilot.exe restart all|SERVICE [--variable VALUE]
ServicePilot.exe logs SERVICE [--tail N] [--json]
ServicePilot.exe service list|get|add|edit|remove|start|stop|restart|logs ...
ServicePilot.exe step list SERVICE [--json]
ServicePilot.exe step run SERVICE STEP [--variable VALUE]
ServicePilot.exe step variables SERVICE STEP [--json]
ServicePilot.exe step variable-add SERVICE STEP --variable VALUE
ServicePilot.exe step variable-remove SERVICE STEP --variable VALUE
ServicePilot.exe step variable-clear SERVICE STEP
ServicePilot.exe add --name NAME --dir DIR --step "Name|Batch|command" [--preset VALUE]
ServicePilot.exe remove SERVICE
ServicePilot.exe template list|get|add|edit|remove|apply|save-from-service ...
ServicePilot.exe template export TEMPLATE --file FILE
ServicePilot.exe template import --file FILE
ServicePilot.exe template step-variables TEMPLATE STEP [--json]
ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-clear TEMPLATE STEP
ServicePilot.exe templates [--json]
ServicePilot.exe template create --template auto|node|dotnet|python --dir DIR [--name NAME] [--autostart]
ServicePilot.exe shutdown
```

`SERVICE`, `STEP`, and `TEMPLATE` can be names or GUIDs. `STEP` can also be numeric: `1..N` selects the startup-step display number, while `0` remains a legacy internal-order escape hatch. Script types are `Batch`, `PowerShell`, `Python`, and `Node`. CLI step specs can be `Name|Type|command`, `Name|Type|UseVariable|command`, `Name|Type|UseVariable|RunOnStart|command`, or `Name|Type|UseVariable|RunOnStart|OpenLogOnRun|command`; omitted booleans default to `true` except `OpenLogOnRun`, which defaults to `false`. `--content` commands can set `--open-log-on-run`. `step list --json` and `status --json` expose both persisted `Order` and display-oriented metadata, including `OpenLogOnRun`. `start all` has been removed and should not be reintroduced unless the user explicitly asks.

## Safety Rules

- Never silently delete or overwrite user service configuration.
- Preserve config files and logs when handling errors.
- Any destructive CLI command, such as `remove`, must target an explicit service name or id.
- Do not write secrets, tokens, private paths beyond necessary examples, or machine-specific credentials into docs.
- Long-running process operations must not block the UI thread.
- Keep errors visible in logs or command output.

## Documentation Rules

- Chinese is the primary documentation language. Default Markdown files should be Chinese when the content can reasonably be localized.
- English counterparts use `-en.md`, for example `README-en.md`, `CHANGELOG-en.md`, `docs/ai-usage-en.md`.
- Chinese docs should link to their English counterpart near the top. English docs should link back to the Chinese default.
- User-facing English docs with Chinese counterparts must be updated together.
- Keep `README.md` and `README-en.md` equivalent.
- Keep `CHANGELOG.md` and `CHANGELOG-en.md` equivalent.
- Keep screenshot guidance in `docs/screenshot-guide.md` and `docs/screenshot-guide-en.md` aligned when the visible UI changes.
- Keep README focused on positioning, download, screenshots, quick start, core capabilities, config path, and doc links. Move full CLI/service model/comparison/research detail to `docs/user-guide.md` and `docs/user-guide-en.md`.
- Keep `.github` community files aligned too: default Issue/PR templates are Chinese, English alternatives use `-en.md`, and cross-links must point to the counterpart rather than themselves.
- The project is now using public release wording. Keep user-facing docs focused on current capabilities and release notes, and keep detailed engineering history in AGENTS/session handoff rather than the landing page.
- Update release notes when publishing a real version.
- GitHub Release pages already show the release title. When uploading release notes with `gh release create/edit --notes-file`, the notes body should not start with a duplicate `# ServicePilot X.Y.Z` heading.
- Update this `AGENTS.md` after every meaningful architecture or workflow change so future AI sessions can resume safely.
- Update session handoff docs at the end of substantial work.

## GitHub / CI Rules

- `.github/workflows/build.yml` is the public confidence check. It should restore, build, publish the Windows exe, run CLI smoke tests with `SERVICEPILOT_CONFIG_DIR` set to a temporary directory, and upload `ServicePilot.exe`.
- CI and local release packaging rely on the `Release` publish defaults in `ServicePilot.csproj`; do not reintroduce framework-dependent multi-file `dist` output unless the user explicitly asks.
- Keep CI command-mode smoke tests offline-safe. Do not let CI or isolated local tests route to the user's real tray instance unless `SERVICEPILOT_ALLOW_TRAY_PIPE=1` is intentionally set.
- Dependabot covers GitHub Actions and NuGet dependencies.
- `.gitignore` must keep local build outputs and staged release folders out of Git, including `dist/`, `dist-staged/`, `bin/`, `obj/`, and `TestResults/`.
- Do not reintroduce desktop/floating-window checks into PR templates; ServicePilot is tray-first with WPF dialogs and CLI.

## Current Known Gaps

- The project still needs deeper runtime QA with real services beyond the current Vite validation.
- CLI behavior should continue to be tested against a running tray instance, not only offline/build mode.
- The directory may still need Git initialization or connection to a remote repository before release.
- Release artifacts in `dist` should be regenerated only as part of an explicit release/build step and should leave only `ServicePilot.exe`.
- The running `dist\ServicePilot.exe` can lock publish output. Use `dist-staged/` for validation when the user has not closed the app.
