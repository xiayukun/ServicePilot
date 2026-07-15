# Changelog

[中文](CHANGELOG.md)

This changelog only records user-visible changes in public releases.

## 2.4.2 - 2026-07-15

- **P0 fix**: Persist in-memory config to disk before `config reload` to prevent CLI modifications from being overwritten by stale disk config.
- **P1 improvement**: Normalize ScriptStep Order values on config load (sort then reassign 0-based consecutive numbers), fixing negative/duplicate order entries from manual JSON editing.

## 2.4.1 - 2026-07-14

- "Copy help for AI" window no longer includes the exe path and "run first" paragraphs; only core operation guide remains.

## 2.4.0 - 2026-07-14

- Editing an action now preserves its Id; composite member references no longer become dangling.
- Added `config reload` command to notify the tray to reload the config file into memory without restarting.
- Added `config apply --file PATH` command to validate and apply an external JSON config, with automatic rollback to cached config on failure.
- `step move --position` now supports `first`/`0` for top position; clarified 0-based position index semantics.
- `doctor` COMPOSITE_MEMBER_MISSING now reports the specific dangling member Ids.
- `ai-help` and "Copy help for AI" now include config file path and JSON structure overview.
- COMPOSITE_VARIABLE_MEMBER_MULTIPLE now only counts valid members; no longer false-positives when dangling references exist.
- Saving config now automatically purges dangling composite member references.

## 2.3.1 - 2026-07-14

- `step add --use-variable` now defaults to false (previously true, which caused UseVariable=True+VarCount=0 contradiction).
- `step add` rejects duplicate step names within the same service/template.
- `step edit` now echoes key fields (UseVariable/Variables/Type) after update, eliminating the need for a follow-up list to verify.
- `doctor` exits 0 as long as it successfully produces a report; issue severity is expressed via JSON Counts.Errors/Warnings.
- `doctor` adds UseVariable=True+VarCount=0 warning (STEP_USEVARIABLE_NO_VARS).
- Using a name to locate edit/remove now errors when multiple steps share the same name, requiring a GUID instead.
- `ai-help` unified terminology (step=Action=动作), listed all --type enum values, documented defaults and semantics.

## 2.3.0 - 2026-07-14

- `template import` adds `--on-conflict` option (`rename`/`overwrite`/`skip`); feedback distinguishes new, overwrite, skip, and rename, and shows the resolved file path and template Id.
- `--json` output is forced to UTF-8 and goes to stdout (even on Error), fixing Chinese garbled text in `| python` / `| jq` pipes; exit code semantics unchanged.
- Added `step set-members`/`add-member`/`remove-member` and `template step set-members`/`add-member`/`remove-member` fine-grained commands to directly manage composite action member lists.
- `service edit`/`template edit`/`step edit`/`template step edit` return "no changes detected" instead of "updated" when no actual modification is made.
- `service get`/`status` annotate the default start composite action (`DefaultStartStep`/`IsDefaultStartStep`), making it clear which composite `start SERVICE` runs.
- Tray icon number is smaller: 1-digit 17pt → 2-digit 14pt → 99+ 10pt.
- GitHub README main screenshot updated to show the right-click context menu.
- Added `--json` return to template step CLI commands (`step edit`, `step remove`, `step move`) for easier script and AI parsing.

## 2.2.0 - 2026-07-10

- Added step-level incremental edit CLI commands for fine-grained add, delete, and modify operations on action steps.

## 2.1.1 - 2026-07-10

- Fixed inaccurate AI prompts in ai-help and tray context menu "Copy help for AI".

## 2.1.0 - 2026-07-03

- Added `Copy help for AI` to the tray context menu. It copies an AI prompt with the current absolute `ServicePilot.exe` path.
- `ServicePilot.exe ai-help` and the tray AI help window now use the same content source, reducing drift between CLI help and UI guidance.
- After CLI configuration changes routed through the running tray instance, the tray menu, service manager, template manager, and related log windows refresh immediately.

## 2.0.0 - 2026-06-18

- Refactored the model to `Action` / `Composite`.
- Moved active configuration to `%APPDATA%/ServicePilot/config.v2.json`; legacy `config.json` is preserved.
- Migrated service-level preset variables to action-level `StepVariables`.
- Service/template editors now support composite member orchestration.
- Chinese UI/docs now use the action terminology consistently, and action-kind controls display localized `Action` / `Composite` labels.
- The log window removed the separate Start button and now runs from the unified Run action menu; log tabs are created lazily per action and switch when an action enters Running.
- The log window coalesces non-error webpack progress output at the display layer to reduce UI stalls from high-frequency build logs.
- CLI `start` runs the first composite, and `step run` can run an action or a composite.
- Template import/export preserves composite member relationships.

## 1.0.0 - 2026-06-10

The first public release includes:

- Windows tray-first local development service management.
- Large numeric tray icon showing the count of running/starting services.
- Chinese/English UI that follows Windows language by default and can be switched from the tray context menu.
- Service manager and template manager GUI.
- Multi-step Batch, PowerShell, Python, and Node.js scripts.
- Separate startup steps and manual-only steps.
- Service preset variables and step variables.
- Variable injection as `SERVICEPILOT_VARIABLE`, plus `{{variable}}` / `{{变量}}` replacement.
- Full service templates without working directories.
- Template import/export for sharing `.servicepilot-template.json` files.
- Built-in general "Default developer actions" template with Git, npm, and common tool opener actions.
- Live log window with search, copy selected, copy all, horizontal scrolling, and bounded history.
- Windows Job Object process-group cleanup to reduce orphan npm/Vite child processes.
- AI/script-friendly CLI with `ai-help`, JSON queries, service/template CRUD, step execution, and step-variable maintenance.
- `doctor [--json]` configuration diagnostics for missing directories, empty steps, duplicate names, and duplicate variables.
- JSON output keeps Chinese text readable instead of escaping it by default.
- Configuration stored in `%APPDATA%/ServicePilot/config.json`.
- Variable last-use cache stored in `%APPDATA%/ServicePilot/variable-usage-cache.json`.
