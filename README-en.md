# ServicePilot

[中文说明](README.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

Thanks to the linux.do community for the strong support.

ServicePilot is a **tray-first, AI-friendly Windows launcher for local development services**. It brings frontend apps, backend services, script steps, environment switching, and logs from multiple folders into one tray menu, with a CLI that AI agents and scripts can call safely.

ServicePilot starts, monitors, and stops local development services from the Windows tray and CLI, so humans and AI agents can reliably control npm, dotnet, Python, and custom scripts.

**Drop this prompt into your AI agent: it will inspect state first, then generate services, templates, actions, and variables from facts.**

**Copy this to your AI agent:**

```text
You may use ServicePilot to manage my Windows local development services. Start with ServicePilot.exe ai-help, doctor --json, list --json, and status all --json, then use the JSON output to understand existing services, steps, variables, and templates. When adding services or templates, prefer the built-in "Default developer actions" template where useful, or generate a project-specific multi-step service directly. Before deleting or overwriting configuration, state the exact target name.
```

Anything the command line can do can usually become a ServicePilot step: switch API URLs, pull a branch, install dependencies, open an IDE, or start a dev server. ServicePilot includes a general built-in template, "Default developer actions", with Git branch/tag actions, npm install/build steps, and common tool openers. A practical workflow is to let an AI agent read `ai-help`, `doctor --json`, and `status --json`, then generate project-specific services and templates for you.

**Download:** [ServicePilot.exe](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe) | [Latest release](https://github.com/xiayukun/ServicePilot/releases/latest) | [Full user guide](docs/user-guide-en.md)

![ServicePilot service manager window](Assets/app-preview.png)

| Tray context menu | Service editor and script steps |
| --- | --- |
| ![Tray context menu](Assets/screenshots/tray-menu-zh.png) | ![Service editor and script steps](Assets/screenshots/service-editor-zh.png) |

| Live log window | CLI / AI status checks |
| --- | --- |
| ![Live log window](Assets/screenshots/log-window-zh.png) | ![CLI / AI status checks](Assets/screenshots/status-doctor-cli-zh.png) |

| AI command help |
| --- |
| ![AI command help](Assets/screenshots/ai-help-cli-zh.png) |

## Quick Start

1. Download [`ServicePilot.exe`](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe).
2. Launch it. A numeric icon appears in the Windows notification area.
3. Right-click the number and choose `Add service`, or open `Manage services`.
4. Enter a service name, working directory, and script steps.
5. Optionally add action variables such as local, test, or dev API URLs.
6. Start services from the tray menu, service manager, log window, or CLI.

## Core Features

- **Tray-first UI**: no extra desktop panel; the notification-area number is the main entry point.
- **Multi-step services**: Batch, PowerShell, Python, and Node.js steps in one service.
- **Actions and composites**: run single script actions or ordered composite actions.
- **Variable switching**: choose action variables when starting, restarting, or running an action.
- **Full service templates**: save names, descriptions, actions, composites, and variables while keeping each target working directory.
- **Template sharing**: export templates as JSON files and import templates shared by others.
- **Built-in general template**: first launch seeds editable "Default developer actions" with Git, npm, IDE, and terminal actions.
- **Live logs**: per-action tabs, search, copy, horizontal scrolling, and bounded history.
- **AI/script CLI**: JSON output for `list/status/service/step/template/logs`.
- **Reliable stop**: Windows Job Object cleanup reduces Vite/npm child processes that keep ports alive.
- **Chinese/English UI**: follows Windows language by default, with a tray menu switch.

## Common CLI

```powershell
ServicePilot.exe version
ServicePilot.exe ai-help
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
ServicePilot.exe start "Frontend" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Set API URL" --variable "http://localhost:9000"
ServicePilot.exe logs "Frontend" --tail 200 --json
ServicePilot.exe stop "Frontend"
```

See the [full user guide](docs/user-guide-en.md) for all commands, the service model, templates, variables, and AI workflows.

## Configuration

```text
%APPDATA%\ServicePilot\config.v2.json
%APPDATA%\ServicePilot\config.json
%APPDATA%\ServicePilot\variable-usage-cache.json
```

`config.v2.json` is the active ServicePilot 2.0 configuration. Legacy `config.json` is used as a read-only migration source. `variable-usage-cache.json` only stores last-use ordering and can be rebuilt.

## Build From Source

Requirements:

- Windows
- .NET SDK 8.0+

```powershell
dotnet build .\ServicePilot.sln
dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist
```

`Release` publish defaults to a self-contained single-file `dist\ServicePilot.exe`.

## Docs

- [Full user guide](docs/user-guide-en.md)
- [AI usage guide](docs/ai-usage-en.md)
- [Privacy](PRIVACY-en.md)
- [Contributing](CONTRIBUTING-en.md)
- [Changelog](CHANGELOG-en.md)

## Privacy

ServicePilot is local-first. It does not upload files, paths, logs, configuration, or machine names.

## License

MIT.
