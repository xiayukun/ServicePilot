# AI Usage Guide

[中文](ai-usage.md)

This document is for AI assistants, automation scripts, and maintainers. ServicePilot's CLI is designed so agents inspect current facts before taking explicit actions.

## Recommended Prompt

```text
You may use ServicePilot to manage my Windows local development services. Start by running:

ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json

Use the JSON output as the source of truth. Do not guess service names, step names, or variables. Prefer ServicePilot.exe CLI commands for starting, stopping, restarting, running steps, and reading logs. Before deleting or overwriting configuration, state the exact target service/template name. For tests, set SERVICEPILOT_CONFIG_DIR first so my real configuration is not modified.
```

## Agent Rules

- Start with `ServicePilot.exe ai-help`.
- Prefer `--json` for configuration and runtime state.
- Run `doctor --json` before edits or startup when possible to catch missing directories, empty steps, duplicate names, and duplicate variables.
- Do not guess names. Use `list --json`, `service get --json`, and `step list --json`.
- Start, stop, restart, step execution, and runtime logs require the tray instance to be running.
- Before deleting services/templates or applying templates, identify the exact name or GUID.
- Set `SERVICEPILOT_CONFIG_DIR` for automation tests.
- When `SERVICEPILOT_CONFIG_DIR` is set, CLI commands do not connect to the global running tray instance by default. Set `SERVICEPILOT_ALLOW_TRAY_PIPE=1` only when pipe routing is intentional.
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

`step list --json` returns persisted order, display order, `UseVariable`, `RunOnStart`, `StepVariables`, and runtime step state.

## Lifecycle Commands

```powershell
ServicePilot.exe start "Frontend" --variable "http://localhost:9000"
ServicePilot.exe restart "Frontend" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Set API URL" --variable "http://localhost:9000"
ServicePilot.exe stop "Frontend"
```

When a step has `UseVariable` enabled, the selected value is injected as `SERVICEPILOT_VARIABLE` and replaces `{{variable}}` / `{{变量}}`.

## Step Variables

Startup steps use service preset variables. Manual-only steps can own step variables.

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
  --step "Set API|PowerShell|true|true|$p='src/store/index.js'; (Get-Content $p) -replace 'http://.*?/api', '{{variable}}' | Set-Content $p" `
  --step "Start dev server|Batch|false|true|npm run dev" `
  --preset "http://localhost:9000" `
  --preset "https://test.example.com/api"
```

## Templates

Templates are complete service definitions without a working directory. They include name, description, steps, and variables.

```powershell
ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
ServicePilot.exe template list --json
ServicePilot.exe template get "Vite Frontend" --json
ServicePilot.exe template apply "Vite Frontend" --service "Another Frontend"
ServicePilot.exe template step-variables "Vite Frontend" "Set API URL" --json
ServicePilot.exe template step-variable-add "Vite Frontend" "Set API URL" --variable "https://test.example.com/api"
```

## Isolated Tests

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-AI-Test"
ServicePilot.exe list --json
```

Delete that directory after the test if desired.
