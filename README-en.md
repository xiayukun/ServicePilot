# ServicePilot

[中文说明](README.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

ServicePilot is an **AI-friendly, tray-first Windows launcher for local development services**. It turns scattered `npm run dev`, `dotnet run`, Python services, Batch/PowerShell/Node.js scripts into a single tray menu, with a command-line interface designed for AI agents and automation scripts.

Short description:

```text
ServicePilot starts, monitors, and stops local development services from the Windows tray and CLI, so humans and AI agents can reliably control npm, dotnet, Python, and custom scripts.
```

**Download:** [ServicePilot.exe](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe) | [Latest release](https://github.com/xiayukun/ServicePilot/releases/latest)

![ServicePilot service manager window](Assets/app-preview.png)

More screenshots:

- [Tray context menu](Assets/screenshots/tray-menu-zh.png)
- [Service editor and script steps](Assets/screenshots/service-editor-zh.png)
- [Live log window](Assets/screenshots/log-window-zh.png)
- [CLI / AI status checks](Assets/screenshots/status-doctor-cli-zh.png)

## Why It Exists

Local development rarely starts with one command. Frontend, backend, H5/mobile web, admin portals, gateways, environment switching, and log checks are often scattered across terminals and directories. ServicePilot is not trying to replace PM2, WinSW, or Taskfile. It is a lightweight tray control center for **Windows local development services**:

- Launch it once, then manage services from the tray number.
- Keep configuration tied to real folders on the local file system.
- Use ordered script steps for both startup and one-off utility actions.
- Switch preset variables such as API URLs or environment names.
- Give AI agents JSON-first commands so they inspect state before acting.

## Prompt For AI Agents

Give this prompt to an AI assistant:

```text
You may use ServicePilot to manage my Windows local development services. Start by running:

ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe list --json
ServicePilot.exe status all --json

Use the JSON output as the source of truth. Do not guess service names, step names, or variables. Prefer ServicePilot.exe CLI commands for starting, stopping, restarting, running steps, and reading logs. Before deleting or overwriting configuration, state the exact target service/template name. For tests, set SERVICEPILOT_CONFIG_DIR first so my real configuration is not modified.
```

Common AI commands:

```powershell
ServicePilot.exe ai-help
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
ServicePilot.exe service get "Frontend" --json
ServicePilot.exe step list "Frontend" --json
ServicePilot.exe logs "Frontend" --tail 200 --json
ServicePilot.exe start "Frontend" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Set API URL" --variable "http://localhost:9000"
ServicePilot.exe stop "Frontend"
```

## Quick Start

1. Download [`ServicePilot.exe`](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe).
2. Launch it. A numeric icon appears in the Windows notification area.
3. Right-click the number and choose `Add service`.
4. Enter a service name and working directory, then add one or more script steps.
5. Optionally add preset variables such as local, test, or dev API URLs.
6. Start services from the tray menu, service manager, log window, or CLI.

## Core Features

- **Tray-first UI**: no extra desktop panel; the notification-area number is the main surface.
- **Chinese/English UI**: follows Windows language by default, with a manual switch in the tray context menu.
- **Numeric status**: tray icon shows the count of running/starting services, including `0`.
- **Multi-step services**: Batch, PowerShell, Python, and Node.js steps.
- **Startup vs manual steps**: startup steps are numbered from `1`; manual-only steps are unnumbered utility actions.
- **Variable switching**: startup steps use service preset variables; manual-only steps can own step variables.
- **AI/script-friendly CLI**: JSON output for `list`, `status`, `service`, `step`, `template`, and `logs`.
- **Full service templates**: templates keep name, description, steps, and variables while leaving the target working directory intact.
- **Live logs**: search, copy selected, copy all, horizontal scrolling, and bounded in-memory history.
- **Reliable stop**: Windows Job Object cleanup prevents Vite/npm child processes from keeping ports alive.
- **Local-first privacy**: configuration and usage cache stay in the user profile.

## Command Line

Start, stop, restart, step execution, runtime logs, and shutdown target the running tray instance. Configuration queries and service/template edits can operate on the config file when the tray instance is not running.

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

`SERVICE`, `STEP`, and `TEMPLATE` can be names or GUIDs. `STEP` can also be numeric: `1..N` selects the startup-step display number, while `0` remains available for legacy internal order. Script types are `Batch`, `PowerShell`, `Python`, and `Node`.

Step specs:

```text
Name|Type|command
Name|Type|UseVariable|command
Name|Type|UseVariable|RunOnStart|command
```

## Service Model

Each service contains:

- **Name**: display name in tray, GUI windows, and CLI.
- **Working directory**: root folder where scripts execute.
- **Script steps**: ordered scripts, each with `Use variable` and `Run on start`.
- **Preset variables**: one string per line for startup/restart/startup-step execution.
- **Step variables**: variables owned by manual-only steps.
- **Autostart**: optional startup when ServicePilot launches.

Normal startup only runs steps with `Run on start` enabled. A step must exit with code `0` before the next startup step runs. Steps without `Run on start` appear in a separate execute-step group and can be run manually while the service is stopped or running.

## Configuration

```text
%APPDATA%/ServicePilot/config.json
%APPDATA%/ServicePilot/variable-usage-cache.json
```

`config.json` stores services and templates. `variable-usage-cache.json` only stores last-use ordering for service and step variables.

Isolated tests:

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-Test"
ServicePilot.exe list --json
```

When `SERVICEPILOT_CONFIG_DIR` is set, CLI commands do not connect to the global running tray instance by default, preventing tests from touching the real user configuration. Set `SERVICEPILOT_ALLOW_TRAY_PIPE=1` only when pipe routing is intentional.

## Compared With Similar Tools

- PM2 is stronger for production Node.js process management; ServicePilot focuses on Windows local development, tray UX, and cross-script steps.
- WinSW/NSSM are better for installing Windows services; ServicePilot is lighter and intended for frequent development start/stop cycles.
- concurrently/npm-run-all are good one-shot npm orchestration tools; ServicePilot persists services, templates, logs, and runtime state.
- Task/just are strong project-local command runners; ServicePilot centralizes multiple folders and gives AI agents one control surface.

See [competitive code research](docs/competitive-research-en.md) and [process-runner research](docs/process-runner-research-en.md).

## GitHub Keywords

Recommended search terms and topics:

```text
windows, service-manager, task-runner, system-tray, developer-tools,
local-development, process-manager, cli, ai-tools, automation,
wpf, dotnet, npm, vite, powershell
```

See [repository profile](docs/repository-profile-en.md).

## Build From Source

Requirements:

- Windows
- .NET SDK 8.0+

```powershell
dotnet build .\ServicePilot.sln
dotnet publish .\ServicePilot\ServicePilot.csproj -c Release -r win-x64 --self-contained false -o .\dist
```

## Docs

- [AI usage guide](docs/ai-usage-en.md)
- [Screenshot guide](docs/screenshot-guide-en.md)
- [Competitive code research](docs/competitive-research-en.md)
- [Process-runner research](docs/process-runner-research-en.md)
- [Privacy](PRIVACY-en.md)
- [Contributing](CONTRIBUTING-en.md)
- [Changelog](CHANGELOG-en.md)

## Privacy

ServicePilot is local-first. It does not upload files, paths, logs, configuration, or machine names.

## License

MIT.
