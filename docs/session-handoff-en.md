# Session Handoff

Last updated: 2026-06-09

## Current State

ServicePilot is a .NET 8 Windows tray-first service manager. The current product direction is tray-only: no desktop floating mode.

The latest work removed the over-complex subservice design and replaced it with service preset variables:

- `ServiceConfig.PresetVariables` is a list of strings.
- A selected variable is injected as `SERVICEPILOT_VARIABLE`.
- A selected variable also replaces `{{variable}}` and `{{变量}}` in scripts before execution.
- `ScriptStep.UseVariable` controls this per step. Old configs default to `true`; when it is `false`, that step ignores the selected variable and execute-step menus run it directly.
- `ScriptStep.RunOnStart` controls whether normal service startup runs that step. Old configs default to `true`; when it is `false`, the step is skipped during startup but remains manually runnable.
- Preset variables can be selected from tray start/restart menus and from single-step execution menus.
- Preset variable menu order is driven by `%APPDATA%\ServicePilot\variable-usage-cache.json`, or by the `SERVICEPILOT_CONFIG_DIR` equivalent during isolated tests.
- Selecting an existing variable records cache usage without reordering `ServiceConfig.PresetVariables`.
- Choosing `新增` in a variable menu opens `PresetVariableInputDialog`, defaults to the most recently used variable, saves a new variable to the service, records usage, and immediately runs the selected start/restart/step action.
- CLI `--variable` records usage when routed to the running tray instance, but it does not automatically append the value to the persistent preset list.

Templates are now full service templates without working directories:

- `AppConfig.ServiceTemplates` stores `ServiceTemplate` entries.
- A template includes name, description, script steps, preset variables, and timestamps.
- Applying a template replaces a service's name, steps, and preset variables while preserving its id, working directory, sort order, created time, and autostart flag.

The old keep-running checkbox was removed. A final command that keeps running naturally keeps the service in `Running`; if it exits with `0` the state becomes `Completed`, and if it exits nonzero the state becomes `StartFailed`.

## Important Runtime Notes

- `ProcessRunner` launches Batch through `cmd.exe /d /s /c` with `chcp 65001 > nul`.
- Batch output is decoded as strict UTF-8 first and OEM code page fallback.
- Stderr is not automatically an error. `ProcessRunner` classifies known normal stderr progress, such as webpack progress, as `Info`; final failures are still emitted from nonzero exit codes and explicit system exceptions.
- PowerShell, Python, and Node steps use temporary script files.
- PowerShell temporary scripts must be written as UTF-8 with BOM because Windows PowerShell 5 reads UTF-8 without BOM as the local ANSI code page and can corrupt Chinese string literals into parse errors. Batch temporary scripts must remain UTF-8 without BOM because this system's `cmd.exe` treats a BOM as part of the first command.
- Every launched process is assigned to a Windows Job Object with kill-on-close.
- Empty script steps are skipped at runtime for compatibility with older configs; the GUI prevents saving new empty steps.
- Single-step execution promotes an otherwise stopped service to `Running` while the standalone step is running, then returns it to `Completed`, `StartFailed`, or `Stopped` after standalone step work finishes.
- Normal service startup runs only steps with `RunOnStart=true`; skipped startup steps are marked `Skipped` and remain available from execute-step menus.
- Stop also catches invalid process handles as already exited, which avoids intermittent `Win32Exception (6): invalid handle` UI errors.
- Keep the Job Object cleanup path intact; it is what prevents Vite/npm child processes from leaving port 3000 occupied.

## GUI Notes

- `App.RebuildTrayMenu` builds the tray menu.
- Tray menu status and enablement should use `ServiceRuntimeState.State`; using the view-model `State` can lag and show stale `Starting` after a short step has completed.
- The tray icon is generated dynamically as a large active-service count. It shows `0` when none are running/starting.
- Tray tooltip text includes active services and selected variables.
- Startup/step final failures raise a throttled tray balloon with the service name and a compact 1-2 line message; full details remain in logs.
- Service dialog: `Views\ServiceConfigDialog.xaml(.cs)`. Editing an existing service can save the current draft as a full service template.
- Service manager: `Views\ServiceManagerWindow.xaml(.cs)`.
- Template manager: `Views\TemplateManagerWindow.xaml(.cs)`.
- Template editor: `Views\ServiceTemplateDialog.xaml(.cs)`.
- Variable input dialog: `Views\PresetVariableInputDialog.xaml(.cs)`.
- Service and template manager DataGrids use explicit dark styles to avoid white rows/cells.
- Log window action buttons use a local style so disabled button text stays black and readable.
- Log window start/restart/execute-step actions use the same preset-variable menus as tray and service manager, and the log toolbar can open the existing service edit dialog.
- Log window action buttons subscribe to service/step state changes; Start must be disabled while the service is running/starting or any standalone step is running.
- Opening a log window uses `LoadLogs()` to bind buffered history in bulk, and auto-scroll is throttled. Do not loop through cached entries with `AddLog()` because that scrolls repeatedly and can freeze the UI for long webpack logs.
- Service and template step editors include `使用变量` and `启动执行` checkboxes next to script type.
- Execute-step menus are grouped into `启动执行` and `不启动执行`.
- Tray service menus and execute-step menus use compact colored dots instead of verbose state prefixes: green for running/starting, red for failed/error, orange for stopping/cancelled, and no dot for stopped/not-run/succeeded.
- Service manager start/execute-step/stop/restart buttons are enabled from the selected row's live runtime state, not from global service state.

## CLI Notes

Important commands:

```text
ServicePilot.exe ai-help
ServicePilot.exe doctor [--json]
ServicePilot.exe start SERVICE [--variable VALUE]
ServicePilot.exe restart SERVICE [--variable VALUE]
ServicePilot.exe step list SERVICE [--json]
ServicePilot.exe step run SERVICE STEP [--variable VALUE]
ServicePilot.exe step variables SERVICE STEP [--json]
ServicePilot.exe step variable-add SERVICE STEP --variable VALUE
ServicePilot.exe step variable-remove SERVICE STEP --variable VALUE
ServicePilot.exe step variable-clear SERVICE STEP
ServicePilot.exe service add --name NAME --dir DIR --step "Name|Batch|command" [--preset VALUE]
ServicePilot.exe service edit SERVICE [--preset VALUE] [--clear-presets]
ServicePilot.exe template add --name NAME --step "Name|Batch|command" [--preset VALUE]
ServicePilot.exe template save-from-service --service SERVICE --name NAME
ServicePilot.exe template apply TEMPLATE --service SERVICE
ServicePilot.exe template step-variables TEMPLATE STEP [--json]
ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-clear TEMPLATE STEP
```

The legacy `template create --template auto|node|dotnet|python --dir DIR` remains for folder-based service creation.

`start all` has been removed. Keep `stop all`, but do not add a bulk-start UI or CLI path back unless the user explicitly asks. CLI step specs can be `Name|Type|command`, `Name|Type|UseVariable|command`, or `Name|Type|UseVariable|RunOnStart|command`.

The `subservice` command now returns an error saying subservices were removed and to use preset variables plus `step run`.

Any CLI handler that saves configuration must stay async all the way through. Do not block async config writes with `.GetAwaiter().GetResult()` on the WPF command-mode path; it can hang command processes and lock the Debug exe.

`doctor [--json]` is an offline configuration diagnostic command. It checks missing directories, empty steps, duplicate names, duplicate variables, and related issues. It exits with code `2` when Errors are found. JSON CLI output keeps Chinese text readable instead of escaping it as `\uXXXX`.

When `SERVICEPILOT_CONFIG_DIR` is set, CLI commands skip the global tray command pipe by default and operate on the isolated config. This prevents test commands from accidentally routing to a real running tray instance. Set `SERVICEPILOT_ALLOW_TRAY_PIPE=1` only when pipe routing is intentional.

## Documentation Direction

- Chinese is the primary documentation language. Default Markdown files should be Chinese when the content can reasonably be localized.
- English counterparts use `-en.md`, for example `README-en.md`, `CHANGELOG-en.md`, and `docs/ai-usage-en.md`.
- Chinese and English docs link to each other near the top.
- The project has not been publicly launched, so user-facing docs should not keep pre-launch bugfix history. CHANGELOG uses an initial `1.0.0 - To Be Released` style.
- Added `docs/ai-usage.md` / `docs/ai-usage-en.md` for AI and automation workflows.
- Added `docs/competitive-research.md` / `docs/competitive-research-en.md` covering code/positioning research for more than 10 related projects.
- `docs/repository-profile.md` / `docs/repository-profile-en.md` contains GitHub description, topics, search keywords, and launch copy.

## Verification Already Run

- `rtk dotnet build ServicePilot.sln`
- Isolated CLI config with `SERVICEPILOT_CONFIG_DIR=%TEMP%\servicepilot-cli-test`.
- Offline CLI:
  - Added `VarSvc` with old-compatible `Name|Batch|false|command` step format.
  - Listed services as JSON.
  - Saved a full service template from `VarSvc`.
  - Listed templates as JSON.
- Running tray CLI:
  - Started `VarSvc` with `--variable dev`.
  - Confirmed logs showed `cmd dev dev`.
  - Ran one step with `step run VarSvc Echo --variable prod`.
  - Confirmed logs showed `cmd prod prod`.
  - Shut down the isolated tray instance.
- Published exe runtime:
  - Rebuilt `dist\ServicePilot.exe`.
  - Confirmed `template apply` replaces service name, steps, and preset variables.
  - Confirmed `start PubVarSvc --variable alpha` logs `alpha alpha`.
  - Confirmed a short service can run, return to `Completed`, run again immediately, and execute a single step again.
- Real `screen` Vite service:
  - Confirmed ports 3000-3006 were clear before test.
  - Autostart used `http://localhost:3000/`.
  - Stop cleared ports 3000-3006.
  - Manual start reused 3000.
  - Two restarts reused 3000 and did not listen on 3001-3006.
  - Final stop cleared ports 3000-3006 and the published tray instance was closed.

## 2026-06-09 Step-State Update

- Added in-memory per-step runtime states: `NotRun`, `Running`, `Succeeded`, `Failed`, `Skipped`, and `Cancelled`.
- Execute-step menus in tray and service manager disable only the step currently `Running`.
- A service can keep one step running while another step runs with a selected preset variable.
- Trying to run the already-running step is rejected.
- Stopping the service marks running steps as `Cancelled`.
- Volta shim startup failures with exit code `126` and Volta-specific output are retried narrowly before marking startup failed.
- Command pipe disposal is idempotent so repeated application-exit paths do not surface `CancellationTokenSource has been disposed`.
- Published exe validation confirmed running one long step, executing another variable step, rejecting the already-running step, and cancelling the running step on stop.

## 2026-06-09 Variable Cache And Error Prompt Update

- Added `Services\PresetVariableUsageStore.cs`.
- Added `Views\PresetVariableInputDialog.xaml(.cs)`.
- Tray and service manager variable menus now sort by last use and include `新增` at the end.
- New variables entered from GUI are saved to the selected service and used immediately.
- Existing variable selections and CLI `--variable` calls record usage in `variable-usage-cache.json`.
- Added throttled tray balloon notifications for final startup/step failures.
- Fixed log-window disabled action button text contrast.

## 2026-06-09 Step Variable Toggle And Manager State Update

- Added `ScriptStep.UseVariable`, defaulting to `true` for backward compatibility.
- Steps with `UseVariable=false` do not receive `SERVICEPILOT_VARIABLE`, do not replace `{{variable}}`, and do not show variable submenus for execute-step actions.
- Added the `使用变量` checkbox to service and template step editors.
- Standalone step execution now marks a stopped service as `Running` while the step is active.
- Service manager start/stop/restart buttons now refresh from the selected service row's live state.
- CLI step specs support `Name|Type|UseVariable|command` in addition to `Name|Type|command`.

## 2026-06-09 Start-Step And Log-Window Update

- Removed the bulk start-all action from tray UI and CLI; `stop all` remains.
- Added `ScriptStep.RunOnStart`, defaulting to `true` for backward compatibility.
- Added `启动执行` to service and template step editors; normal service startup skips unchecked steps.
- Execute-step menus in tray, service manager, and log window are grouped into `启动执行` and `不启动执行`.
- Service edit dialog can save the current service draft as a full service template.
- Log window now has variable-aware start, execute-step, and restart controls.
- Log window now has an edit button that opens the existing service edit dialog with the log window as owner.
- Log window start/stop/restart/execute-step buttons now refresh from live service and step state, so Start darkens during standalone step execution.
- Service manager toolbar order is now start, execute-step, stop, restart.
- CLI step specs now also support `Name|Type|UseVariable|RunOnStart|command`; `step list --json` includes `RunOnStart`.
- Verification run:
  - `rtk dotnet build ServicePilot.sln`
  - Isolated CLI add/list using `RunOnStart=false` and `RunOnStart=true` steps.
  - Isolated tray test confirmed `start all` returns exit code `2`, startup skips the unchecked step, and manual step execution still works.
  - Published `dist\ServicePilot.exe` and verified help plus JSON step output.

## 2026-06-09 Stderr Classification Update

- Fixed false red `[Error]` log labels for normal webpack progress lines written to stderr.
- `ProcessRunner` now classifies stderr by content instead of treating the entire stderr stream as `Error`.
- Known benign webpack/vite progress output becomes `Info`; warning-like stderr becomes `Warning`; strong failure terms remain `Error`.

## 2026-06-09 Log Window Performance Update

- Fixed opening long logs causing repeated auto-scroll and UI freezes.
- Added `LogWindow.LoadLogs()` for bulk buffered-history loading after the window is shown.
- Changed live auto-scroll to a short timer so bursts of log lines scroll at most once per tick.

## 2026-06-09 Compact Status Dot Update

- Replaced verbose tray service and execute-step state prefixes with compact colored status dots.
- Stopped/not-run/succeeded items have no dot; running/starting show green; failed/error show red; stopping/cancelled show orange.
- Step state text remains available as tooltip text instead of occupying menu labels.

## 2026-06-09 Step Variable And Log Search Update

- Added `ScriptStep.StepVariables` for manual-only steps (`RunOnStart=false`).
- Startup steps continue to use service `PresetVariables`; manual-only steps use their own `StepVariables` and are sorted by `PresetVariableUsageStore` using the step id.
- Manual-only steps with `UseVariable=true` show a variable submenu with `新增` even when no step variables exist yet.
- Startup execute-step labels now number from `1` within the `启动执行` group; manual-only steps under `不启动执行` show no number.
- Service and template editors switch the left variable box between `预设变量` for startup steps and `手动执行变量` for manual-only steps.
- Log buffers are capped at 20,000 entries in both the app buffer and the log window.
- Log window now supports search, previous/next match, Ctrl+C/right-click copy, copy all, and horizontal scrolling for long lines.
- CLI `step list --json` and `status --json` include `StepVariables` and display-oriented metadata. Numeric step selectors now use `1..N` for startup display numbers, with `0` kept for legacy internal order.

## 2026-06-09 AI CLI And Positioning Docs

- Added `ServicePilot.exe ai-help` with a safe operating guide for AI/script usage.
- Added CLI step-variable maintenance commands:
  - `step variables SERVICE STEP [--json]`
  - `step variable-add SERVICE STEP --variable VALUE`
  - `step variable-remove SERVICE STEP --variable VALUE`
  - `step variable-clear SERVICE STEP`
- Added CLI template step-variable maintenance commands:
  - `template step-variables TEMPLATE STEP [--json]`
  - `template step-variable-add TEMPLATE STEP --variable VALUE`
  - `template step-variable-remove TEMPLATE STEP --variable VALUE`
  - `template step-variable-clear TEMPLATE STEP`
- `step run` records variable usage against the service id for startup steps and against the step id for manual-only steps.
- Fixed an async deadlock in step-variable CLI commands that could leave command-mode `ServicePilot.exe` processes running and lock build output.
- README now uses AI-friendly Chinese-primary positioning with an English counterpart.
- CHANGELOG now uses initial public-release wording instead of pre-launch bugfix history.
- Completed isolated CLI validation: add service, add two step variables, query JSON, remove one variable, clear variables; add template, add two template step variables, query JSON, remove one template variable, clear template variables.

## 2026-06-09 Doctor Config Diagnostics Update

- Added `ServicePilot.exe doctor [--json]`.
- Diagnostics include duplicate service/template names, missing service directories, empty steps, missing startup steps, duplicate step order, duplicate step names, duplicate preset variables, and duplicate step variables.
- Errors return exit code `2`; warning-only results return `0`.
- JSON output now keeps Chinese text readable for humans and AI agents.
- `SERVICEPILOT_CONFIG_DIR` now disables tray pipe routing by default, protecting isolated tests from touching real config.
- Completed isolated CLI validation: empty config passes, valid service passes, bad config returns `SERVICE_DIR_MISSING` and `STEP_CONTENT_EMPTY`.

## 2026-06-09 GitHub Launch Material Update

- Fixed `.github` issue/PR template cross-links so default Chinese templates link to `-en.md` English templates, and English templates link back to the Chinese defaults.
- Expanded issue templates with affected areas: tray menu, service manager window, log window, CLI/AI commands, port cleanup, config/templates/variables.
- Removed the stale floating-window check from PR templates and replaced it with tray, log window, CLI/AI command, and safety-boundary checks.
- Updated `.github/workflows/build.yml` with minimal permissions, post-publish `ai-help` / `doctor --json` smoke tests, and artifact missing-file failure.
- Added NuGet Dependabot coverage and expanded `.gitignore` for `dist-staged/`, `TestResults/`, package files, and other local outputs.
- Updated `docs/github-launch-checklist.md` / `docs/github-launch-checklist-en.md` for GitHub Actions, templates, Dependabot, and `.gitignore` preflight checks.
- Normalized English counterpart link labels in public root docs, including CONTRIBUTING, PRIVACY, SECURITY, MAINTAINERS, THIRD-PARTY-NOTICES, and release templates.
- Fixed the English CHANGELOG filename in `docs/release-checklist.md` / `docs/release-checklist-en.md` and added checks for exiting `dist\ServicePilot.exe`, temporary-config CLI smoke tests, `shutdown`, and Actions artifacts.
- Updated `docs/first-push.md` / `docs/first-push-en.md` with Actions smoke-test, issue/PR template, and Dependabot visibility checks.
- Updated `docs/release-notes-v1.0.0.md` / `docs/release-notes-v1.0.0-en.md` to match current features: tray number, startup/manual steps, variables, full templates, log search, AI CLI, and Job Object cleanup.
- Added temporary-config `ai-help` / `doctor --json` checks to the release notes template.
- Verified `rtk dotnet build ServicePilot.sln`, Markdown relative links, public-doc prelaunch wording scan, and `ai-help` / `doctor --json` under a temporary `SERVICEPILOT_CONFIG_DIR`.

## 2026-06-09 Language Switch And Screenshot Guide Update

- Added `Services\LocalizationService.cs` with `auto`, `zh-CN`, and `en-US` support.
- `AppConfig.Settings.Language` persists the UI language; old configs without this field default to `auto`.
- `auto` follows the Windows UI language: Chinese systems use Chinese, all others default to English.
- The tray context menu now has a `语言` / `Language` submenu for follow-system, Chinese, and English. Selecting an option saves config and rebuilds the menu immediately.
- Tray menu text, state/step tooltips, language menu, add-variable dialog, service manager, template manager, log window, service editor, template editor, and template picker now use the localization service.
- User data is not translated: service names, template names, step names, variables, script content, and raw logs remain unchanged.
- Added `docs/screenshot-guide.md` / `docs/screenshot-guide-en.md` for required pre-release screenshots: tray menu, service manager, service editor, log window, template manager, and CLI/AI usage.
- README, CHANGELOG, release notes, and release checklist now mention Chinese/English UI switching and the screenshot guide.
- Verified `rtk dotnet build ServicePilot.sln`, published to `dist-staged`, ran `dist-staged\ServicePilot.exe ai-help` with exit code `0` under a temporary `SERVICEPILOT_CONFIG_DIR`, and confirmed `doctor --json` passes with an empty config.
- A running `dist\ServicePilot.exe` was detected, so the official `dist` folder was not overwritten to avoid locked files or disrupting active services.

## 2026-06-10 Log Window Layout And Step-State Settlement Update

- Increased log window size from `900x520` to `1040x560`.
- Added `TextTrimming=CharacterEllipsis`, `NoWrap`, and `MaxWidth=260` to log-window `TitleText`, so long service names are clipped and cannot push start/stop/run-step buttons away.
- Renamed and broadened single-step completion settlement in `ProcessManager` from `CompleteStandaloneStepServiceState` to `CompleteIdleServiceState`.
- Main service executors and standalone step executors now both call `CompleteIdleServiceState`; once no main executor, no step executor, and no running step remain, service state settles from the current run's step results.
- `CompleteIdleServiceState` uses `ServiceRuntimeState.StartTime` to ignore stale step states, preventing historical failed steps from making later successful runs stay `StartFailed` or `Running`.
- Republished to `dist` and verified:
  - `rtk dotnet build ServicePilot.sln`
  - `rtk dotnet publish ServicePilot/ServicePilot.csproj -c Release -r win-x64 --self-contained false -o dist`
  - `dist\ServicePilot.exe ai-help` exits `0` under a temporary `SERVICEPILOT_CONFIG_DIR`
  - `dist\ServicePilot.exe doctor --json` passes with an empty config under a temporary `SERVICEPILOT_CONFIG_DIR`

## 2026-06-10 Screenshot Organization And GitHub Profile Update

- Organized Snipaste screenshots under `Assets/screenshots/`:
  - `tray-menu-zh.png`
  - `service-manager-zh.png`
  - `service-editor-zh.png`
  - `log-window-zh.png`
  - `ai-help-cli-zh.png`
  - `status-doctor-cli-zh.png`
- Copied `Assets/screenshots/service-manager-zh.png` to `Assets/app-preview.png` for README hero image and GitHub above-the-fold preview.
- Replaced screenshot placeholder text in `README.md` / `README-en.md` with the real hero image and screenshot links.
- Added the current screenshot file list to `docs/screenshot-guide.md` / `docs/screenshot-guide-en.md`.
- Updated `docs/repository-profile.md` / `docs/repository-profile-en.md` with bilingual GitHub description, homepage, topics, and search keywords.
- Updated `docs/first-push.md` / `docs/first-push-en.md` with the GitHub repository description.

## 2026-06-10 Recent Service Ordering Update

- `PresetVariableUsageStore` now records recent service usage in the same `variable-usage-cache.json` file used for variable ordering.
- The tray service list is sorted by most recently used first; services without usage records still fall back to `SortOrder` and name.
- The service manager window now binds to a sorted snapshot from `PresetVariableUsageStore.SortServices` instead of binding directly to the raw service collection, and refreshes try to keep the current service selected.
- Start, stop, restart, run-step, view logs, edit, delete, save-as-template, and tray-routed CLI start/stop/restart/run-step/log commands refresh recent service usage.
- This ordering only affects display and cache state. It does not mutate `ServiceConfig.SortOrder` or reorder service definitions in `config.json`.
- Verified: `rtk dotnet build ServicePilot.sln`.

## Next Useful Checks

- Rebuild after any further edits.
- Publish to `dist` only when preparing a release or user-facing exe validation.
- Test the user's real `screen` Vite service after publishing:
  - Kill/listeners on ports 3000-3006 if requested.
  - Start `screen`.
  - Stop/restart repeatedly.
  - Confirm Vite reuses port 3000 and no listeners remain on 3000-3006 after stop.
- If changing command execution again, re-test Chinese cmd output and npm/Vite logs.
