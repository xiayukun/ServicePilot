# Session Handoff

Last updated: 2026-07-21

Chinese counterpart: [session-handoff.md](session-handoff.md)

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

The current released version is ServicePilot 3.0.0:

- Project version properties are currently `3.0.0` (ServicePilot/ServicePilot.csproj). The log merge/collapse batches above are not yet version-bumped or committed; pick a new version and sync the CHANGELOG when committing.
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
- Current user instruction: produce local exe builds for testing only. Do not commit, tag, or publish a GitHub Release unless explicitly asked.
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