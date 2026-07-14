# AI Usage Guide

[中文](ai-usage.md)

This document is for AI assistants, automation scripts, and maintainers. ServicePilot's CLI is designed so agents inspect current facts before taking explicit actions.

## Recommended AI Handoff

After downloading and launching ServicePilot, ask the user to right-click the tray number and choose `Copy help for AI`, then paste the full window content into the AI assistant. The copied content includes the current absolute `ServicePilot.exe` path, recommended first commands, and safety guidance from the same source as `ai-help`, so the AI does not have to guess the exe location.

If the tray window is not available yet, use this generic prompt, but replace `ServicePilot.exe` with the real absolute exe path:

```text
You may use ServicePilot to manage my Windows local development services. Start with ServicePilot.exe ai-help, config-path, doctor --json, list --json, and status all --json, and use the real output as the source of truth. Do not guess service names, action names, variables, templates, or paths. Before deleting, overwriting, or renaming anything, state the exact target name or id. For tests, set SERVICEPILOT_CONFIG_DIR first so my real configuration is not modified.
```

## Agent Rules

- If the tray instance is running, you may ask the user to copy `Copy help for AI` from the tray context menu first. That window includes the current absolute `ServicePilot.exe` path, recommended first commands, and safety guidance from the same source as `ai-help`.
- Start with `ServicePilot.exe ai-help`.
- Prefer `--json` for configuration and runtime state.
- Run `doctor --json` before edits or startup when possible to catch missing directories, empty steps, duplicate names, and duplicate variables.
- Do not guess names. Use `list --json`, `service get --json`, and `step list --json`.
- Start, stop, restart, step execution, and runtime logs require the tray instance to be running.
- Before deleting services/templates or applying templates, identify the exact name or GUID.
- Set `SERVICEPILOT_CONFIG_DIR` for automation tests.
- When `SERVICEPILOT_CONFIG_DIR` is set, CLI commands do not connect to the global running tray instance by default. Set `SERVICEPILOT_ALLOW_TRAY_PIPE=1` only when pipe routing is intentional.
- After a successful configuration change routed through the tray pipe, the tray menu, open service manager, open template manager, and related log windows refresh immediately.
- Do not reintroduce `start all`; batch startup should be explicit in the caller.

## Initial Probe

```powershell
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
ServicePilot.exe template list --json
```

## Service Inspection

```powershell
ServicePilot.exe service get "Frontend" --json
ServicePilot.exe step list "Frontend" --json
ServicePilot.exe logs "Frontend" --tail 200 --json
```

`step list --json` returns persisted order, `Kind`, `MemberStepIds`, `UseVariable`, `StepVariables`, and runtime step state.
The GUI log window groups output by action tabs, but CLI `logs` currently returns the raw service log stream. For AI-heavy workflows, a future improvement should add an action-name filter to `logs`.

## Lifecycle Commands

```powershell
ServicePilot.exe start "Frontend" --variable "http://localhost:9000"
ServicePilot.exe restart "Frontend" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Start" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Set API URL" --variable "http://localhost:9000"
ServicePilot.exe stop "Frontend"
```

When an Action has `UseVariable` enabled, the selected value is injected as `SERVICEPILOT_VARIABLE` and replaces `{{variable}}` / `{{变量}}`. A Composite passes the selected value to its variable-enabled member Action.

## Step Variables

Variables live on Actions as `StepVariables`. Service-level preset variables are legacy migration data only.

```powershell
ServicePilot.exe step variables "Frontend" "Set API URL" --json
ServicePilot.exe step variable-add "Frontend" "Set API URL" --variable "https://test.example.com/api"
ServicePilot.exe step variable-remove "Frontend" "Set API URL" --variable "https://test.example.com/api"
ServicePilot.exe step variable-clear "Frontend" "Set API URL"
```

## Add Service Example

```powershell
ServicePilot.exe service add `
  --name "Frontend" `
  --dir "D:\projects\frontend" `
  --step "Set API|PowerShell|true|$p='src/store/index.js'; (Get-Content $p) -replace 'http://.*?/api', '{{variable}}' | Set-Content $p" `
  --step "Start dev server|Batch|false|npm run dev"

ServicePilot.exe step variable-add "Frontend" "Set API" --variable "http://localhost:9000"
ServicePilot.exe step variable-add "Frontend" "Set API" --variable "https://test.example.com/api"
```

## Templates

Templates are complete service definitions without a working directory. They include name, description, Actions, Composites, and Action variables.

```powershell
ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
ServicePilot.exe template list --json
ServicePilot.exe template get "Vite Frontend" --json
ServicePilot.exe template apply "Vite Frontend" --service "Another Frontend"
ServicePilot.exe template step-variables "Vite Frontend" "Set API URL" --json
ServicePilot.exe template step-variable-add "Vite Frontend" "Set API URL" --variable "https://test.example.com/api"
```

Template import supports `--on-conflict` to control name/ID collision strategy:

```powershell
ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json"                    # default: rename
ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json" --on-conflict overwrite
ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json" --on-conflict skip
```

## Composite Member Management

CLI can directly manage composite action member lists:

```powershell
ServicePilot.exe step set-members "Frontend" "Start" --member "Set API URL" --member "Start Server"
ServicePilot.exe step add-member "Frontend" "Start" --member "Health Check"
ServicePilot.exe step remove-member "Frontend" "Start" --member "Health Check"
ServicePilot.exe template step set-members "Vite Frontend" "Start" --member "Set API URL" --member "Start Server"
ServicePilot.exe template step add-member "Vite Frontend" "Start" --member "Health Check"
ServicePilot.exe template step remove-member "Vite Frontend" "Start" --member "Health Check"
```

## --json Output And Encoding

`--json` output is forced to UTF-8 encoding; errors also go to stdout (exit code semantics unchanged), so `| python` / `| jq` pipes never garble Chinese text:

```powershell
ServicePilot.exe step edit "Frontend" "Set API URL" --name "Set API" --json
ServicePilot.exe step remove "Frontend" "Old Step" --json
ServicePilot.exe step move "Frontend" "Set API" --position 1 --json
```

`service edit` / `step edit` etc. return "no changes detected" when no actual modification is made, preventing AI agents from repeatedly writing empty changes.

## Isolated Tests

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-AI-Test"
ServicePilot.exe list --json
```

Delete that directory after the test if desired.
