# ServicePilot

[English](README-en.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)
![Version](https://img.shields.io/github/v/release/xiayukun/ServicePilot?color=blue)

ServicePilot 是一个 **托盘优先、AI 友好、FluentWindow 现代界面的 Windows 本地开发服务启动器**。v3.0.0 带来所有管理窗口全面升级为 WPF-UI FluentWindow 现代界面，支持 TitleBar 标题栏、系统主题色选中效果和统一深色主题。它从托盘和 CLI 启动、监控、停止本地开发服务，把前端、后端、脚本动作、环境变量和日志收进一个托盘菜单，让人和 AI 都能可靠操作 npm、dotnet、Python 和自定义脚本。

**交给 AI 的推荐方式：** 下载并启动 ServicePilot 后，在托盘数字图标上右键选择 `复制给 AI 的帮助`。AI 就能先用 `ai-help`、`doctor --json`、`list --json`、`status all --json` 读取真实状态，再帮你新增个性化服务、模板、动作和变量。只要命令行能做，就可以包装成 ServicePilot 动作：切换 API 地址、拉取分支、安装依赖、打开 IDE。ServicePilot 内置了“默认开发动作模板”，也推荐让 AI 直接生成适合当前项目的服务和模板。

**下载：** [ServicePilot.exe](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe) | [最新发布](https://github.com/xiayukun/ServicePilot/releases/latest) | [完整用户指南](docs/user-guide.md)

![ServicePilot 托盘右键菜单](Assets/screenshots/tray-menu-zh.png)

| 编辑服务和脚本动作 | 实时日志窗口 |
| --- | --- |
| ![编辑服务和脚本动作](Assets/screenshots/service-editor-zh.png) | ![实时日志窗口](Assets/screenshots/log-window-zh.png) |

| CLI / AI 状态检查 | AI 命令帮助 |
| --- | --- |
| ![CLI / AI 状态检查](Assets/screenshots/status-doctor-cli-zh.png) | ![AI 命令帮助](Assets/screenshots/ai-help-cli-zh.png) |

## 快速开始

1. 下载 [`ServicePilot.exe`](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe)。
2. 双击启动，任务栏通知区域会出现一个数字图标。
3. 右键数字，选择 `新增服务` 或打开 `管理服务`。
4. 填写服务名称、工作目录和脚本动作。
5. 按需填写预设变量，例如本地、测试、开发环境 API 地址。
6. 从托盘菜单、管理服务窗口、日志窗口或 CLI 启动服务。

## 核心能力

- **托盘优先**：无额外桌面面板，任务栏通知区域数字就是主入口。
- **FluentWindow 现代界面**：v3.0.0 全面升级，所有管理窗口使用 WPF-UI FluentWindow，带 TitleBar 标题栏、系统主题色选中效果和统一深色主题。
- **多动作服务**：一个服务可包含 Batch、PowerShell、Python、Node.js 动作。
- **动作与组合动作**：可以单独运行脚本动作，也可以把多个动作编排成组合动作。
- **变量切换**：启动、重启、执行动作时可选择不同预设变量。
- **完整服务模板**：模板保存名称、说明、动作和变量，应用时保留目标工作目录。
- **模板分享**：模板可导出为 JSON 文件，也可从别人分享的文件导入。
- **内置通用模板**：首次启动自动提供“默认开发动作模板”，内含 Git、npm、常用 IDE/终端打开等可编辑动作。
- **实时日志**：支持按动作分 Tab、搜索、复制、横向滚动和有限缓存。
- **AI/脚本 CLI**：`list/status/service/step/template/logs` 支持 JSON 输出；`--json` 错误也走 stdout 且强制 UTF-8 编码，管道消费无乱码；托盘提供 `复制给 AI 的帮助`，CLI 配置变更会刷新已打开的管理/日志窗口。
- **动作级增量编辑**：`step add/edit/remove/move` 可精确增、改、删、排序单个动作步骤；`step set-members/add-member/remove-member` 可直接维护组合动作成员；`template import --on-conflict` 控制同名冲突策略；变更通过托盘管道即时刷新托盘菜单和管理/日志窗口。
- **可靠停止**：使用 Windows Job Object 清理进程组，减少 Vite/npm 子进程残留占端口。
- **中英文界面**：默认跟随 Windows 语言，也可从托盘菜单切换。

## 常用 CLI

```powershell
ServicePilot.exe version
ServicePilot.exe ai-help
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
ServicePilot.exe start "Frontend" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Set API URL" --variable "http://localhost:9000"
ServicePilot.exe step add "Frontend" --name "Check Node" --type Batch --script "node --version" --position after:"Set API URL"
ServicePilot.exe step edit "Frontend" "Check Node" --name "Node Version Check"
ServicePilot.exe step remove "Frontend" "Node Version Check"
ServicePilot.exe step move "Frontend" "Set API URL" --position end
ServicePilot.exe step set-members "Frontend" "Start" --member "Set API URL" --member "Start Server"
ServicePilot.exe step add-member "Frontend" "Start" --member "Health Check"
ServicePilot.exe template import --file ".\my-template.servicepilot-template.json" --on-conflict skip
ServicePilot.exe logs "Frontend" --tail 200 --json
ServicePilot.exe stop "Frontend"
```

完整命令、服务模型、模板、变量和 AI 工作流见 [完整用户指南](docs/user-guide.md)。

## 配置位置

```text
%APPDATA%\ServicePilot\config.v2.json
%APPDATA%\ServicePilot\config.json
%APPDATA%\ServicePilot\variable-usage-cache.json
```

`config.v2.json` 是当前服务和模板配置。旧 `config.json` 只作为迁移来源保留；`variable-usage-cache.json` 只保存最近使用排序，可删除重建。

## 从源码构建

要求：

- Windows
- .NET SDK 8.0+

```powershell
dotnet build .\ServicePilot.sln
dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist
```

`Release` 发布默认生成自包含单文件 `dist\ServicePilot.exe`。

## 文档

- [完整用户指南](docs/user-guide.md)
- [AI 使用说明](docs/ai-usage.md)
- [隐私说明](PRIVACY.md)
- [贡献指南](CONTRIBUTING.md)
- [更新日志](CHANGELOG.md)

## 隐私

ServicePilot 是本地工具。它不会上传文件、路径、日志、配置或机器名。

## 许可证

MIT。
