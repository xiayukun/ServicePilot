# Session Handoff

Last updated: 2026-09-02

Chinese counterpart: [session-handoff.md](session-handoff.md)

## 4.2.1 global log-fold mode and horizontal search positioning (2026-09-02)

- **New log groups inherit the aggregate mode**: The log window now keeps a window-level `_foldNewGroupsByDefault`. Toolbar Expand opens every current merge group and makes future groups start expanded; Fold closes every current group and makes future groups start folded. The existing `_foldStateByHeader` remains the per-group authority, so an individual marker click or search-triggered expansion does not overwrite the global mode, and clearing logs does not reset the mode selected for that window.
- **Search positions both axes**: After selecting a match, search calls AvalonEdit `Caret.BringCaretToView()`. A target far to the right in an unwrapped long log line now moves the horizontal scrollbar to the selection instead of positioning only the line.
- **Verification**: A temporary real-WPF harness passed 2/2: a second group appended after aggregate Expand remained expanded, a third group appended after returning to Fold remained folded, and a match at the end of a 600-character line was selected correctly while `HorizontalOffset` moved from 0 to about 3491 pixels. The temporary harness was removed and is not part of the product files.
- **Acceptance, build, and local state**: The user confirmed that this round passed validation. The final Debug build completed with 0 warnings and 0 errors. The 4.2.1 Release single-file publish succeeded with only `ServicePilot.exe` in `dist`; the CLI reports version 4.2.1, isolated `doctor --json` reports 0 errors and 0 warnings, and the candidate SHA-256 is `075adc02e338fe27bf51a0b09a6b90b545cc92192cf1bc4d465a668634785bbb`. All 42 managed services were idle before deployment, so the 4.2.0 tray exited normally, was replaced with 4.2.1, and restarted hidden; source and target hashes match, exactly one tray process remains, and the configuration still contains 42 services.

## 4.2.0 release completed (2026-08-25)

- This accumulated work is prepared as minor release `4.2.0`: live merge-script `Notify("message")`, instant tray service-name filtering, template exchange preserving `LogMergeScript`, trailing-fold self-repair during streaming, the AvalonEdit mouse exception workaround, and the transparent icon-edge fix.
- Pre-release verification: `dotnet build ServicePilot.sln --nologo` completed with 0 warnings and 0 errors; the concurrency harness passed 5/5, and the template-exchange/live-notification release checks passed 2/2. All PNG corner alpha values are zero. Release publishing produced only `ServicePilot.exe` in `dist`; it reports `4.2.0`, file version `4.2.0.0`, isolated `doctor --json` reports 0 errors and 0 warnings, and AI help documents the `Notify` contract. Candidate SHA-256: `84fdadecb429b6aee40d1b967699df48c8b3bcf7b8d5af5fc42d2303cf681d4f`.
- GitHub `main` now contains release commit `01348f60c52d60970eb67d558a2c48d80a8e2a9e`; its build workflow passed Restore, Build, single-file Publish, CLI smoke test, and artifact upload. The formal `v4.2.0` Release is published as latest, its tag targets that commit, and its only asset is `ServicePilot.exe` with a GitHub digest matching the candidate SHA-256. A post-release status read still found two managed services `Running`, so the private deployment rule was followed: no service was stopped, and the locally synchronized EXE remains at 4.1.0 rather than being overwritten.

## Merge-script system notifications and instant tray filtering (2026-08-20)

- **Live notification contract**: Merge scripts now expose `Notify("message")`. `LiveLogMergeProcessor` uses Roslyn syntax to recognize only real invocations, so comments and string literals do not enable background evaluation. Notification-capable scripts run on the application-level live stream and therefore work with no log window open. Their transient result is attached to `LogEntry` and reused by the window, avoiding a second execution and duplicate side effects. Historical replay and `merge-script test` only collect/preview requests; identical text from the same service Action is de-duplicated for five seconds.
- **Tray filtering**: The root tray menu now pins a search box above the service list. It clears and focuses on open, performs case-insensitive name containment filtering, moves Down Arrow to the first visible service, and lets `Esc` clear an existing query first. The add/manage/status/exit footer stays pinned and is never filtered.
- **Current verification and local update**: The final project Debug build completed with 0 warnings and 0 errors. A notification harness passed state carry, real invocation, comment/string exclusion, clear reset, and preview-without-popup cases; an isolated real CLI `merge-script set/test` also confirmed that notification calls compile and are only previewed in test output. A tray-filter harness passed Chinese/English case-insensitive matching and clear reset. Because a WPF `ContextMenu` enters its popup message loop, automatic focus and immediate typing still require acceptance in the deployed real tray. Release single-file publish succeeded with only `ServicePilot.exe` in the output, version 4.1.0, and isolated `doctor --json` reported zero errors and zero warnings. The user then explicitly requested overwrite and restoration of the two previously running services. On the new check both the tray instance and all managed services had stopped, and the recent-use cache identified the two targets unambiguously. The private copy was replaced, source and target SHA-256 matched, and the new tray started hidden as a single process. Both exact startup Actions were sent concurrently; one status read showed both targets `Running`.

## API/frontend startup completion notifications (2026-08-20)

- Audited the active configuration of 36 services and 6 templates: all 11 `启动 API` service Actions, all 21 `启动服务` frontend Actions, and all 4 corresponding template Actions now contain notification scripts. Frontend Actions that previously had no merge script were populated as well.
- API Actions notify `API 启动完成` only when the exact completion marker `所有启动任务执行完成` appears. Frontend Actions notify `前端启动成功（100%）` only when `[webpack.Progress] 100%` appears. Both script families carry a `PreviousResult.State` flag so one startup/compile group cannot notify repeatedly; ordinary progress, stderr, and historical replay do not produce system notifications.
- `merge-script test` previewed the real conditions and produced one matching notification request for each representative API/frontend script; preview mode never shows a system popup. `doctor --json` remains at 0 errors and 15 existing warnings; no business service was started, stopped, or restarted.
- Fixed `TemplateExchangeService` import/export cloning so template Action `LogMergeScript` values survive template exchange. `dotnet build ServicePilot.sln --nologo` passed with 0 warnings and 0 errors. The older running tray process was not restarted: notification scripts are persisted in the active config, and the in-memory template cache will refresh when the updated ServicePilot starts next time.

## Frontend startup notification is once per Action run (2026-08-20)

- **Root cause**: The frontend script originally returned `frontendNotified=true` only on a `webpack.Progress 100%` line. An ordinary line or `[WARNING]` returned `null`, discarding the previous `State`, so a later HMR `100%` notified again.
- **Fix**: The 21 frontend service Actions and 3 frontend template Actions now keep carrying `State` through ordinary, warning, and unmatched progress lines after the guard is set; the first `100%` notifies once. `App.OnProcessStepStateChanged` clears live merge state when an Action enters `Running`, so the next startup run is armed for one new notification.
- **Verification**: `merge-script test` used a five-line sample with “first 100% → ordinary WARNING → two HMR 100% lines”; it produced exactly one notification request and rendered 3 lines. Both `web` and `screen` passed. Active-config read-back confirmed identical scripts across 21 frontend services and 3 frontend templates; the 11 API scripts were unchanged.
- No business service was started, stopped, or restarted in this turn. Status read-back showed `leniu-tengyun` already `Running`; it was not touched. The source lifecycle fix takes effect with a newly built/deployed ServicePilot; the current older tray process was not restarted.

## Self-repair for trailing fold sections during streaming output (2026-08-20)

- **Symptom and evidence**: While a standard API kept streaming, roughly three raw lines could occasionally remain visible at the bottom. Clicking Fold changed the button to Expand but did not hide them. Live `logs --json` read-back confirmed those entries were already marked as collapsed children, ruling out the merge script; the ineffective toggle showed that the rows were outside the current `FoldingManager` ranges.
- **Fix**: After every `UpdateFoldings`, `RebuildFoldings` now verifies the actual section count and every start/end range against the groups calculated from the current log entries. Only a mismatch triggers clearing and recreating sections, after which `_foldStateByHeader` restores the user's intent. The toolbar Fold/Expand command also reconciles sections before toggling, so a missing trailing section is repaired before the requested action runs.
- **Verification and local update**: Passed focused tests for a growing EOF fold, a newly appended group, the real 5,000-entry log shape, and real WPF rendering. The project's own `LogWindow` also passed 50 consecutive batches of `header + 2 children` with no stranded render queue. After deliberately removing the final section to reproduce the three uncontrolled tail rows, two Fold/Expand actions restored the exact EOF range and folded it again. `dotnet build ServicePilot.sln --nologo` completed with 0 warnings and 0 errors; the Release publish directory contains only the single EXE, and isolated `doctor --json` reports zero errors. A pre-copy check found no active services, so the local private copy was updated, source/target SHA-256 equality was verified, and the tray was restarted hidden; the restarted process is unique and still has no active services.

## New-version menu-upgrade log folding (2026-08-18)

- Based on the current standard API's actual new-version menu-authorization logs, removed the legacy audit/thread-pool recognition and now recognize only the new authorization request and its same-thread SQL flow.
- The fold summary now shows menu and language query totals, batched Updates counts, percentages, and final completion; later role-menu cleanup in the same request is not counted as language progress.
- Verified with merge-script test against real log samples and with a project build; the API was not stopped or restarted in this turn.

## Full copy of new-version menu-upgrade folding (2026-08-18)

- Copied the verified new-version menu-upgrade folding script to the 11 API-service actions named “启动 API” in the active configuration and to the same action in the “Java Maven API” template.
- Service actions were individually compile-checked and runtime-refreshed through merge-script set; the template was applied through the official config apply command. Final read-back confirmed all 12 target scripts are byte-for-byte identical and contain only the new recognition.
- A representative sample in the new log format verified progress from preparation to menu 2510/2510 and language 1835/1835 completion; no business service was started, stopped, or restarted.

## Active configuration action fix (2026-08-13)

- Audited the active local configuration: 36 services and 6 templates; 11 service actions and 1 template action were creating an extra backup file while changing database addresses.
- Updated all 12 actions to write directly to the target configuration file while preserving the existing database URL validation, master/slave updates, and success output. Legacy read-only migration data and historical configuration snapshots were left unchanged.
- Verified that the active configuration still parses, `doctor --json` reports zero errors, and the target actions no longer contain the backup-generation logic. No business service was started, stopped, or restarted in this turn.

## Current working-tree fixes (2026-08-13)

- **Icon corner halo**: The project assets include `servicepilot_icon_final.png`, a transparent RGBA source whose boundary still carried a white matte. The generator now keeps a full square canvas, makes the four corners transparent, removes the outer matte with an alpha mask, preserves the previous visible subject size at about 91% of the canvas, and resizes PNG and ICO entries through premultiplied alpha. On-device testing confirmed that the title-bar PNG was clean while the taskbar still showed a faint fringe when it loaded the ICO, so every FluentWindow `Icon` now loads the same PNG directly; the ICO remains for the exe's Windows Shell icon.
- **Log folding**: No log-folding code was changed in this pass. Version 4.1.0 already contains serialized output ordering, process-output draining, fold-state capture/restore, and layout-driven scrolling fixes; the issue has not reappeared after moving from 4.0.2 to 4.1.0.

## Current working-tree fix (2026-08-17)

- **AvalonEdit UI-thread exception**: After a service failed, moving the mouse in its log window produced `FileNotFoundException: System.Windows.Forms`; the stack landed in AvalonEdit `TextArea.ShowMouseCursor()`. The upstream implementation confirms that this optional feature calls `System.Windows.Forms.Cursor.Show/Hide`, so `HideCursorWhileTyping` is disabled on every AvalonEdit control in the log, service-edit, and template-edit windows. Log folding, search, copy, and script editing are unaffected.
- **Verification and release preparation**: `dotnet build ServicePilot.sln --nologo` passed with 0 warnings and 0 errors; publishing and overwriting the local sync-software executable follow the service-state check in this turn.

## 4.1.0 public screenshot: closable action-log tab

- Added public asset `Assets/screenshots/log-window-action-tabs-zh.png`, captured with an isolated "Public demo service" and synthetic build output. It shows the action-tab close `×`, the `Expand` fold control, auto-scroll, and folded build summaries.
- Both READMEs now use the new image. The Chinese and English screenshot guides mark the old `log-window-zh.png` as a historical resource that needs a fresh sanitization review before any public reuse.
- Sanitization scope: the image contains no real working directory, private-network address, token, customer name, or household/personal information; the demo log uses only generic public text.

## 4.1.0 release content: bind auto-scroll to completed log layout

- **Root cause**: The previous path started a fixed 120 ms `DispatcherTimer` after a visible-tab log batch and then called `ScrollToLine`. That delay had no causal relationship with `TextDocument` insertion, folding reconstruction, or AvalonEdit/WPF publishing the latest scroll extent. Under high-frequency output or complex folding layout, it could fire against the previous extent and leave the viewport short of the real end.
- **Fix**: Each render batch for the visible tab now coalesces at most one scroll request, issued only after document insertion, folding rebuild, and redraw. `LogEditor.LayoutUpdated` consumes the request after the real layout pass and calls `ScrollToEnd`; the flag is cleared first so scrolling cannot create a layout loop. Disabling auto-scroll, clearing logs, and closing the window cancel pending intent, while enabling it invalidates layout and immediately moves the existing document to its end. Output for non-visible tabs still updates history without requesting a scroll.
- **Verification**: All 7 focused source-order checks passed; `dotnet build ServicePilot.sln --nologo` succeeded with 0 warnings and 0 errors.

## 4.1.0 release content: synchronize fold intent and Summary/Expand button

- **Root cause**: The log toolbar's `_summaryViewActive` was a second state detached from the real `FoldingSection.IsFolded` values. It became stale after individual fold-margin clicks, search expansion, tab changes, and section rebuilds, making the label disagree with the next click. Capturing old sections against a new tab's document offsets could also associate intent with the wrong header.
- **Fix**: The button is now derived only from the current `FoldingManager.AllFoldings`. Individual marker clicks save header-keyed (`LogEntry`) intent immediately through `VisualLinesChanged`; search expansion and aggregate clicks synchronize explicitly. Incremental rebuilds capture live state before restoring it inside a guarded rebuild phase, while full tab rebuilds capture before replacing the old document to avoid cross-tab offset association. New groups still default to folded, and unreachable headers are pruned from the intent dictionary.
- **Verification**: All four isolated state-machine harness scenarios passed; `dotnet build ServicePilot.sln` completed with 0 warnings and 0 errors.

## 4.1.0 release content: closable action log tabs and authoritative clearing

- **Tab identity**: `LogEntry` now carries a stable `StepId`, and action log tabs are grouped by ID rather than display name. Actions with identical names but different IDs no longer share a tab or clear one another. Service logs and legacy entries without a stable ID do not expose a close button.
- **Closing a tab**: Action tab headers expose a keyboard-focusable close button with localized accessibility text. Closing removes only that action's application-level buffered history, pending deliveries, tab collection, rendered document, search continuation, merge continuation, and fold state. Closing the active tab selects the right neighbour when available (otherwise the left); it never stops the action or service. New output may lazily recreate the tab, but cleared history cannot return.
- **Clear scope**: The toolbar Clear command keeps its existing global scope: it clears all authoritative buffered logs and derived window state for the current service. Reopening the log window cannot restore pre-clear content, while later output still appends normally.
- **Verification**: The focused static check passed 11/11; Debug and Release builds both completed with 0 warnings and 0 errors. The tab close × button has a transparent background; the final 4.1.0 single-file package and local overwrite are handled by the downstream release card.

## 4.1.0 release content: process completion, stop takeover, and tail-output drain

- **Problem and evidence**: Beyond short-process `Process.Exited` racing ahead of stdout/stderr drain, review confirmed three blocking concurrency defects: Stop could miss the window between cancellation checking and runner publication/start; a five-second drain timeout was swallowed and still published `Stopped`; and `_emitGate` invoked external subscribers while held, synchronously entering `Dispatcher.Invoke`. Before remediation, the focused harness failed 3/4 scenarios: a process started after Stop returned, drain timeout looked successful, and a normal stop published two terminal states.
- **Fix**: `ScriptExecutor._runnerGate` now makes cancellation recheck, runner publication/start, Stop takeover, clearing, and disposal one boundary. `ProcessRunner` uses a single-reader channel for program-observed enqueue order; its lock protects only enqueue/suppression state, while subscribers and Dispatcher run outside the lock. It explicitly does not claim absolute OS generation order across stdout/stderr. `Completion` waits for process exit, both stream EOFs, and delivery of already-enqueued subscriber work. After the initial five-second Stop timeout, the runner force-kills again, closes redirected streams, suppresses future delivery, drains existing callbacks, and throws `TimeoutException`; `ProcessManager` publishes one `Error`, while a normal stop publishes one `Stopped`.
- **Verification**: All 5 `scripts/ServicePilot.ConcurrencyHarness` scenarios pass: cancellation in the runner-publication window; a slow subscriber receiving 201 stdout and 201 stderr lines including unterminated tails while retaining exit code 7; drain timeout not masquerading as success with no callbacks starting after Stop; one Manager `Error` on timeout; and one `Stopped` on a normal Stop. `dotnet build ServicePilot.sln --nologo` completed with 0 warnings and 0 errors. Tests used a temporary `SERVICEPILOT_CONFIG_DIR` and did not touch real services or the deployed executable.

## Fix release: ServicePilot 4.0.2 (2026-07-22)

- **Symptom**: While logs stream, manually collapsing a fold group and then getting more lines for that group re-expanded it.
- **Root cause**: In incremental `RebuildFoldings`, a growing active group makes AvalonEdit destroy+recreate its section during `UpdateFoldings`. The old `_foldingInitialized` HashSet (fold once on first appearance) neither refolded nor preserved the user's manual state after recreation, so it sprang open.
- **Fix**: Replaced with `_foldStateByHeader` (`Dictionary<LogEntry,bool>`, per-header fold intent, default folded). `RebuildFoldings` captures each live section's `IsFolded` into the dict BEFORE `UpdateFoldings` (recording manual toggles), then reapplies it authoritatively AFTER; the dict is cleared only when logs are cleared, not on tab switch. All manual entry points (fold margin clicks, search-expand, summary button) are covered by the capture-before step, so no per-hook wiring is needed.
- Version `4.0.2`; updated `CHANGELOG` (zh/en), added `docs/release-notes-v4.0.2.md`, and `AGENTS` (fold-state persistence rule). Build: 0 warnings, 0 errors. Per request, overwrote the local deployment and published the GitHub Release (tag `v4.0.2`).

## Fix release: ServicePilot 4.0.1 (2026-07-22)

- **Symptom**: On a Java/Spring API service startup, log folding was misaligned — fold headers flattened at the top, details piled at the bottom, error start line misplaced (see user's screenshot).
- **Investigation (analyze first, then fix)**:
  - Used `merge-script test` on two realistic samples (startup+error+stack 16→4; request wave+SQL+error 12→4): the **merge function logic is correct**, ruling out the script.
  - Confirmed the user runs the new exe (with `PreviousResult/InCollapseGroup/State`). Noted `dist-staged` is a pre-globals build also labeled 4.0.0 (same version, different bits — confusing).
  - CLI folds correctly in a single full pass while the UI renders incrementally line-by-line, narrowing it to the UI; following the "a later line reaches the page first" hypothesis found the root cause.
- **Root cause**: `ProcessRunner` pumps `stdout`/`stderr` on two concurrent `PumpOutputAsync` tasks, delivered via `ProcessManager.RunOnUiThread` (`Dispatcher.Invoke`). Under threads the enqueue order equals thread scheduling, so **a later log line could enter `LogEntries` first**, scrambling the order-dependent fold state machine (`LogWindow.ApplyMerge`).
- **Fix**: Added an `_emitGate` lock in `ProcessRunner`; all output (stdout/stderr/system notices) now goes through a serialized `Emit(...)`. `ProcessManager` keeps the blocking `Dispatcher.Invoke` (the emit lock is held until the UI queues the line → strict order) with a comment forbidding a switch to `BeginInvoke`. The merge function is unchanged.
- Bumped to `4.0.1` (`csproj` + `AGENTS.md`); added 4.0.1 entries to `CHANGELOG`/`CHANGELOG-en`; added `docs/release-notes-v4.0.1.md` (Chinese body + English `CHANGELOG-en` footer only, per convention).
- Build: 0 warnings, 0 errors. Pushed to GitHub and created the Release (tag `v4.0.1`) per request; **local exe not overwritten** (user will download it).

## Release: ServicePilot 4.0.0 (2026-07-21)

- Bumped from 3.x to `4.0.0` (`csproj` + `AGENTS.md`) as a major release consolidating this session's new features.
- The icon white-halo root cause was the **opaque white background baked into source PNG V1**; `scripts\make_icon.py` now detects the teal squircle bounds + applies a rounded-rect mask, exporting a transparent `app.ico` (exe/taskbar) and `app.png` (title-bar `ui:ImageIcon`, avoids multi-frame ico downscale halos). A full `obj/bin` clean rebuild ensures the new icon is embedded in the exe.
- README/README-en gained a top hero image `Assets/servicepilot-hero.png` (AI-generated, teal brand), and the 4.0 folding/overview log screenshot `Assets/screenshots/log-window-zh.png` was promoted to the top.
- CHANGELOG/CHANGELOG-en consolidated the former 3.1.0 entry into the `4.0.0` release entry.
- Committed, pushed, and created the GitHub Release via `gh` (tag `v4.0.0`, uploading `ServicePilot.exe`).
- The local deploy target is the Chinese folder "同步软件" (shown as garbled `ͬ������` in some shells; it is the same directory with 30+ apps); byte-exact matching is used to avoid creating duplicate dirs or accidental deletion.

## Earlier change: new app icon + version 3.1.0 (2026-07-21)

- Adopted a new teal squircle icon (source PNG V1). `scripts\make_icon.py` (Pillow) trims transparent padding, re-pads centered, and exports a multi-resolution `ServicePilot\Resources\Icons\app.ico` (16/24/32/48/64/128/256).
- `app.ico` is the single icon source: the csproj `<ApplicationIcon>` (exe icon), every `ui:FluentWindow` `Icon` (taskbar), and every `ui:TitleBar.Icon` (visible left-side title-bar icon, `ui:ImageIcon` 18×18). All 9 window XAMLs updated.
- The tray badge icon is still generated dynamically by `App.CreateTrayIconWithBadge` (running count) and intentionally does NOT use `app.ico`.
- Version bumped to `3.1.0` (`csproj` + `AGENTS.md`); `CHANGELOG`/`CHANGELOG-en`/`README`/`README-en` gained a 3.1.0 entry covering this session's user-visible work (merge scripts / folding / overview / hot-reload / scrollable menus / system accent / icon & title bar).
- Built with 0 warnings/0 errors, then published over the local private target.

## Earlier change: fold visualization + tray menu (2026-07-21)

On top of the log merge/collapse batch, the collapse feature now has real fold visualization plus related UI polish.

Fold visualization (`LogWindow.xaml.cs` / new `Views/FoldColorMarkerRenderer.cs`):
- Folding is now a REAL AvalonEdit fold (`FoldingManager.Install` wired into TextView line generation, which actually hides folded lines), with a left-side `>`/`+` expand toggle. Raw lines are always kept; expanding reveals every child line. The fold starts at the header line offset so the collapsed view shows only the summary Title.
- Folded content is searchable: `FindLogMatch` auto-expands any fold containing a hit; the `Summary` button toggles fold-all / expand-all.
- The collapsed placeholder TEXT is fixed white (`FoldingElementGenerator.TextBrush`, a global static set once).
- Multi-color folds: AvalonEdit's fold box is one global color and cannot be colored per section (`FoldingElementGenerator` is `sealed`). Instead `FoldColorMarkerRenderer` (an `IBackgroundRenderer` overlay) paints a ~100px content-color block between the `+` marker and the summary text, using the fold's FIRST child color; the Title is padded with leading spaces (`GetFoldTitlePrefix`) so text sits to the right of the block and never overlaps. This is the only supported way to show multiple differently-colored folds at once.
- Right-side overview `Views/OverviewMargin.cs`: a color overview map next to the native scrollbar, one pixel row per highest-priority color (Error > Warning > custom > System > normal), folding-aware, click-to-scroll, no draggable thumb (which caused per-scroll repaint lag); `InvalidateVisualCache` has a signature guard so pure scrolling does not rebuild.

Tray menu:
- Briefly tried "keep the menu open after clicking a run/stop item" (`StaysOpenOnClick`); it felt wrong, so it was fully reverted — clicking closes the menu as before (run items call `RebuildTrayMenu()`).

Merge script upgraded to a stateful streaming function (2026-07-21):
- New inputs (`MergeScriptGlobals`): `PreviousResult` (the full `MergeResult` returned for the previous line), `PreviousWasCollapsed`, `InCollapseGroup`.
- New output (`MergeResult`): `State` (`Dictionary<string, object?>`), handed to the next line as `PreviousResult.State` — enables counters / de-dup / conditional folding.
- Constraints: runtime only, never persisted, NOT restored on tab rebuild; store simple values only (string/int/double/bool, since scripts run in a collectible ALC); per tab (`LogTabState.LastResult`).
- Touchpoints: `MergeScriptGlobals.cs`, `MergeResult.cs`, `LogMergeService.BuildSource` (new injected locals, `UserBodyStartLine` 16→19), `LogWindow.ApplyMerge`, `ServiceCommandProcessor.MergeScriptTestAsync` (CLI test carries state too); editor prefill comments, AI help (zh/en), and AGENTS are all synced.

## Earlier change: log merge collapse fix (2026-07-20)

Fixed "LogMergeScript is set but progress lines never fold in the log window." Two real root causes:

1. `LogWindow` never consumed `MergeResult.Collapse` — it only replaced text and color, so folding was never rendered. (This batch further evolved it into a real AvalonEdit fold, see above.)
2. `LogMergeService.BuildReferences` was missing `System.Text.RegularExpressions` (and a few others), so any script using `Regex` failed to compile at runtime and was silently swallowed (the user's script used `Regex`). References are now complete, and `BuildSource` pre-adds `using System.Text.RegularExpressions;` / `using System.Globalization;` (with `UserBodyStartLine` updated to match).

Supporting changes:
- `merge-script set` now compile-checks and refuses to save on error (`--skip-validate` to force); a runtime compile failure surfaces once per step in the service log via `MergeScriptCompileError` instead of being silent.
- New `merge-script test SERVICE STEP --file lines.txt [--json]`: feeds each line as CurrentLine and prints hit / MergedMessage / Color / Collapse plus the final rendered view — verify without running a service. Verified offline (8 lines → 3) and in the single-file publish build.
- Contract documented in AGENTS.md / AI help: `PreviousLine`/`CurrentLine` are the FULL formatted line `"HH:mm:ss [Level] message"`; the script is read live from the current config on every line (`UpdateService` updates `RuntimeState.Config`), so edits take effect on the next line without restart; `Color` accepts any WPF color; `Children` is reserved/not rendered.

## Current State

ServicePilot is a .NET 8 Windows tray-first developer service manager. The current product direction is tray menus, WPF management windows, log windows, and CLI automation. The desktop floating mode is intentionally removed.

4.1.0 version fields, CHANGELOG entries, and the Chinese Release Notes are prepared; the artifact card has produced the final single-file package and completed the private local overwrite, while GitHub publication remains a downstream operation:

- Project version properties are now `4.1.0` (ServicePilot/ServicePilot.csproj), with all four version fields normalized to `4.1.0` / `4.1.0.0`.
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

- Normal build check: `dotnet build ServicePilot.sln`.
- Single-file publish command: `dotnet publish ./ServicePilot/ServicePilot.csproj -t:Rebuild -c Release -o ./dist`.
- `Release` publish defaults should produce a single `ServicePilot.exe`.
- If the running exe locks `dist`, publish to `dist-staged` first.
- After successfully producing an exe, follow the local private copy target in `LOCAL_NOTES.private.md` when that file exists. Do not copy that target path into committed docs.
- Before overwriting the local install target, detect whether the target exe is locked by a running process yourself (e.g. `Get-Process ServicePilot`) and only ask the user to close it when it is actually locked; do not ask by default.
- The artifact card ran `dotnet publish ServicePilot/ServicePilot.csproj -t:Rebuild -c Release -o ./dist --nologo`; `dist` contains only `ServicePilot.exe`, and both it and the private local overwrite target return version `4.1.0` with matching length and SHA-256. This verification is not a full independent QA pass; real-service GUI, long-running, and regression scenarios still require follow-up validation.
- This documentation-normalization task does not commit, tag, or create a GitHub Release.
- GitHub Release pages already show the title, so the notes body should not add a duplicate top-level heading.

## Documentation Rules

- Chinese is the primary documentation language. English counterparts use `-en.md`.
- When user-visible behavior changes, update `AGENTS.md`, this handoff, the English handoff, and related README / user guide / ai-usage / changelog files.
- Current user-facing docs for new users should say actions/composites; keep step/步骤 only in historical release notes or compatibility CLI names.
- Sensitive details must not be written into README files, user guides, handoff docs, AGENTS, release notes, or issue/PR templates.
- If local deployment targets, private services, customer projects, or screenshot source details must be remembered, write them to `LOCAL_NOTES.private.md`.

## Verification Suggestions

Run at least this after functional changes:

```text
dotnet build ServicePilot.sln
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
