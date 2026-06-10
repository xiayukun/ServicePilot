# ServicePilot Full User Guide

[中文](user-guide.md)

ServicePilot is a tray-first Windows manager for local development services. It centralizes startup commands, utility scripts, environment switching, and logs across multiple project folders, while exposing one queryable CLI for AI agents and automation scripts.

## Basic Workflow

1. Launch `ServicePilot.exe`.
2. Right-click the numeric notification-area icon.
3. Add a service through `Add service` or `Manage services`.
4. Configure the working directory, script steps, and variables.
5. Start, stop, restart, run steps, and inspect logs from the tray, service manager, log window, or CLI.

The tray number shows services in `Running` or `Starting` state. It shows `0` when nothing is active.

## Service Model

Each service contains:

- **Name**: display name in the tray, GUI, and CLI.
- **Working directory**: root folder where scripts execute.
- **Script steps**: ordered Batch, PowerShell, Python, or Node.js scripts.
- **Preset variables**: one string per line for start, restart, and startup-step execution.
- **Step variables**: variables owned by manual-only steps.
- **Autostart**: optional startup when ServicePilot launches.

Normal startup only runs steps with `Run on start` enabled. Disabled startup steps appear in a separate manual group and can be run as utility actions.

A step must exit with code `0` before the next step runs. If the final command keeps running, the service stays running. If it exits with `0`, the service becomes completed. A nonzero exit marks startup failed.

## Variables

Preset variables and step variables are plain strings. They do not need to be `key=value`.

At runtime:

- The selected value is injected as `SERVICEPILOT_VARIABLE`.
- `{{variable}}` and `{{变量}}` are replaced with the selected value.
- If a step disables `Use variable`, it receives no variable and no placeholder replacement.

Variable menus are sorted by recent use. The cache lives at:

```text
%APPDATA%\ServicePilot\variable-usage-cache.json
```

This file is only a cache and can be rebuilt.

## Templates

A template is a full service without a working directory:

- Name
- Description
- Script steps
- Preset variables

Applying a template replaces the target service name, steps, and preset variables while keeping the target working directory, service id, autostart setting, and display order.

On first tray startup, ServicePilot creates an editable default developer-action template for common actions such as opening tools, Git operations, dependency installation, and startup commands. If the user deletes it, ServicePilot does not recreate it on every launch.

## Log Window

The log window supports:

- Start, run step, stop, and restart.
- Variable-aware start and step execution.
- Editing the current service.
- Search, previous/next match.
- Copy selected logs and copy all logs.
- Horizontal scrolling for long lines.
- Bounded in-memory history.

Startup failures, step failures, and system errors are written to the log and also try to show a tray balloon. Notifications are best-effort and do not depend on Windows notification center being enabled.

## CLI / AI Workflow

Recommended first commands for AI agents:

```powershell
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
```

Use JSON output as the source of truth. Do not guess service names, step names, or variables.

Common commands:

```powershell
ServicePilot.exe help
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor [--json]
ServicePilot.exe list [--json]
ServicePilot.exe status [all|SERVICE] [--json]
ServicePilot.exe start SERVICE [--variable VALUE]
ServicePilot.exe stop all|SERVICE
ServicePilot.exe restart SERVICE [--variable VALUE]
ServicePilot.exe logs SERVICE [--tail N] [--json]

ServicePilot.exe service list|get|add|edit|remove|start|stop|restart|logs ...
ServicePilot.exe step list SERVICE [--json]
ServicePilot.exe step run SERVICE STEP [--variable VALUE]
ServicePilot.exe step variables SERVICE STEP [--json]
ServicePilot.exe step variable-add SERVICE STEP --variable VALUE
ServicePilot.exe step variable-remove SERVICE STEP --variable VALUE
ServicePilot.exe step variable-clear SERVICE STEP

ServicePilot.exe template list|get|add|edit|remove|apply|save-from-service ...
ServicePilot.exe template step-variables TEMPLATE STEP [--json]
ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-clear TEMPLATE STEP
ServicePilot.exe shutdown
```

`SERVICE`, `STEP`, and `TEMPLATE` can be names or GUIDs. `STEP` can also be numeric: `1..N` selects the displayed startup-step number, while `0` remains available for legacy internal order.

Supported script types:

```text
Batch
PowerShell
Python
Node
```

CLI step specs:

```text
Name|Type|command
Name|Type|UseVariable|command
Name|Type|UseVariable|RunOnStart|command
```

Example:

```powershell
ServicePilot.exe service add `
  --name "Frontend" `
  --dir "D:\projects\frontend" `
  --step "Set API|PowerShell|true|true|$p='src/store/index.js'; (Get-Content $p) -replace 'http://.*?/api', '{{variable}}' | Set-Content $p" `
  --step "Start dev server|Batch|false|true|npm run dev" `
  --preset "http://localhost:9000" `
  --preset "https://test.example.com/api"

ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
ServicePilot.exe template apply "Vite Frontend" --service "Another Frontend"
```

## Configuration And Isolated Tests

Real user configuration lives at:

```text
%APPDATA%\ServicePilot\config.json
%APPDATA%\ServicePilot\variable-usage-cache.json
```

If an older version or a manual run left `config.json` beside the executable or in the current directory, ServicePilot copies it into the Roaming target when the target config does not exist. The old file is not deleted automatically.

Isolated tests:

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-Test"
ServicePilot.exe list --json
```

When `SERVICEPILOT_CONFIG_DIR` is set, CLI commands do not connect to the global running tray instance by default. This prevents tests from touching the real user configuration. To intentionally route through the tray pipe, set:

```powershell
$env:SERVICEPILOT_ALLOW_TRAY_PIPE = "1"
```

## Compared With Similar Tools

- PM2 is stronger for production Node.js process management; ServicePilot focuses on Windows local development, tray UX, and cross-script steps.
- WinSW/NSSM are better for installing Windows services; ServicePilot is lighter and intended for frequent development start/stop cycles.
- concurrently/npm-run-all are good one-shot npm orchestration tools; ServicePilot persists services, templates, logs, and runtime state.
- Task/just are strong project-local command runners; ServicePilot centralizes multiple folders and gives AI agents one control surface.

Further reading:

- [AI usage guide](ai-usage-en.md)
- [Competitive code research](competitive-research-en.md)
- [Process-runner research](process-runner-research-en.md)
- [Repository profile](repository-profile-en.md)
- [Screenshot guide](screenshot-guide-en.md)

## Prompt For AI Agents

```text
You may use ServicePilot to manage my Windows local development services. Start by running:

ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json

Use the JSON output as the source of truth. Do not guess service names, step names, or variables. Prefer ServicePilot.exe CLI commands for starting, stopping, restarting, running steps, and reading logs. Before deleting or overwriting configuration, state the exact target service/template name. For tests, set SERVICEPILOT_CONFIG_DIR first so my real configuration is not modified.
```
