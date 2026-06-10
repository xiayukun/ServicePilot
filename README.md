# ServicePilot

[English](README-en.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

ServicePilot 是一个 **AI 友好、托盘优先、面向本地开发项目的 Windows 服务启动器**。它把散落在多个项目里的 `npm run dev`、`dotnet run`、Python 服务、Batch/PowerShell/Node.js 脚本收进一个托盘菜单，并同时提供适合 AI/脚本调用的命令行接口。

一句话介绍：

```text
ServicePilot 从 Windows 托盘和 CLI 启动、监控、停止本地开发服务，让人和 AI 都能可靠操作 npm、dotnet、Python 和自定义脚本。
```

**下载：** [ServicePilot.exe](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe) | [最新发布](https://github.com/xiayukun/ServicePilot/releases/latest)

![ServicePilot 管理服务窗口](Assets/app-preview.png)

更多界面截图：

- [托盘右键菜单](Assets/screenshots/tray-menu-zh.png)
- [编辑服务和脚本步骤](Assets/screenshots/service-editor-zh.png)
- [实时日志窗口](Assets/screenshots/log-window-zh.png)
- [CLI / AI 状态检查](Assets/screenshots/status-doctor-cli-zh.png)

## 为什么做它

本地开发通常不是只启动一个命令：前端、后端、H5、管理端、网关、配置切换、日志排查，经常散落在不同终端和不同目录里。ServicePilot 的定位不是替代 PM2、WinSW 或 Taskfile，而是把 **Windows 本地开发服务** 做成一个轻量的托盘控制台：

- 双击启动，右键托盘数字就能管服务。
- 依赖真实文件系统和项目目录，不把配置藏进云端。
- 每个服务可以有多个脚本步骤，既能启动服务，也能单独执行某个工具步骤。
- 预设变量可以快速切换 API 地址、环境名、启动参数。
- CLI 输出支持 JSON，适合让 AI 先检查状态，再精确执行操作。

## 给 AI 的推荐提示词

把下面这段发给你的 AI 助手，它就能先了解 ServicePilot，再用命令行帮你管理本机服务：

```text
你可以使用 ServicePilot 管理我的 Windows 本地开发服务。请先运行：

ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe list --json
ServicePilot.exe status all --json

然后基于 JSON 结果行动，不要猜服务名、步骤名或变量。启动、停止、重启、执行步骤、查看日志时优先使用 ServicePilot.exe 的 CLI。删除或覆盖配置前必须说明目标服务/模板名称。需要测试时先设置 SERVICEPILOT_CONFIG_DIR，避免影响我的真实配置。
```

AI 常用命令：

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

## 快速开始

1. 下载 [`ServicePilot.exe`](https://github.com/xiayukun/ServicePilot/releases/latest/download/ServicePilot.exe)。
2. 双击启动，任务栏通知区域会出现一个数字图标。
3. 右键数字，选择 `新增服务`。
4. 填写服务名称和工作目录，添加一个或多个脚本步骤。
5. 按需填写预设变量，例如本地、测试、开发环境 API 地址。
6. 从托盘菜单、管理服务窗口、日志窗口或 CLI 启动服务。

## 核心能力

- **托盘优先**：无额外桌面面板，主界面就是任务栏通知区域数字。
- **中英文界面**：默认跟随 Windows 语言，也可以在托盘右键菜单中手动切换。
- **数字状态**：托盘图标显示正在运行或启动中的服务数量，默认显示 `0`。
- **多步骤服务**：一个服务可包含 Batch、PowerShell、Python、Node.js 步骤。
- **启动步骤与手动步骤分离**：启动执行步骤从 `1` 编号；不启动执行步骤不编号，可作为工具动作单独运行。
- **变量切换**：启动步骤使用服务预设变量；手动步骤可使用自己的步骤变量。
- **AI/脚本友好 CLI**：`list/status/service/step/template/logs` 支持 JSON 输出。
- **完整服务模板**：模板包含名称、说明、脚本步骤和变量，应用时保留目标工作目录。
- **实时日志**：支持搜索、复制、复制全部、横向滚动和有限缓存。
- **可靠停止**：使用 Windows Job Object 清理进程组，避免 Vite/npm 子进程残留并继续占用端口。
- **本地优先**：配置和变量缓存保存在用户目录，不上传路径、日志或脚本内容。

## 命令行

启动、停止、重启、执行步骤、读取运行时日志和关闭程序会控制正在运行的托盘实例；配置查询和服务/模板编辑在无托盘实例时也可操作配置文件。

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

`SERVICE`、`STEP`、`TEMPLATE` 可以是名称或 GUID。`STEP` 也可以是数字：`1..N` 表示启动执行步骤显示编号，`0` 保留给旧内部顺序兼容。脚本类型支持 `Batch`、`PowerShell`、`Python`、`Node`。

步骤规格支持：

```text
Name|Type|command
Name|Type|UseVariable|command
Name|Type|UseVariable|RunOnStart|command
```

示例：

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

## 服务模型

每个服务包含：

- **名称**：托盘、管理窗口、CLI 中显示的名称。
- **工作目录**：所有脚本执行的根目录。
- **脚本步骤**：按顺序执行的脚本，每步可设置是否使用变量、是否启动执行。
- **预设变量**：启动、重启、执行启动步骤时使用的一行一个字符串。
- **步骤变量**：不启动执行步骤自己的变量列表。
- **自启**：ServicePilot 启动时自动启动该服务。

普通启动只运行勾选 `启动执行` 的步骤。步骤必须返回退出码 `0` 才会继续下一步。未勾选 `启动执行` 的步骤会出现在 `不启动执行` 分组中，可以在服务运行或停止时单独执行。

## 配置位置

```text
%APPDATA%/ServicePilot/config.json
%APPDATA%/ServicePilot/variable-usage-cache.json
```

`config.json` 是服务和模板配置。`variable-usage-cache.json` 只保存变量最近使用排序，可删除重建。

隔离测试：

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-Test"
ServicePilot.exe list --json
```

设置 `SERVICEPILOT_CONFIG_DIR` 后，CLI 默认不会连接正在运行的全局托盘实例，避免测试命令误操作真实配置。确实需要连接托盘管道时，再显式设置 `SERVICEPILOT_ALLOW_TRAY_PIPE=1`。

## 和类似工具的区别

- PM2 更适合 Node.js 生产进程管理；ServicePilot 更偏 Windows 本地开发、托盘 GUI 和跨脚本步骤。
- WinSW/NSSM 更适合把程序包装成 Windows 服务；ServicePilot 不安装系统服务，更轻，适合频繁启动停止。
- concurrently/npm-run-all 更适合一次性 npm 脚本编排；ServicePilot 会保留服务配置、日志、模板和运行状态。
- Task/just 更适合项目内任务定义；ServicePilot 更适合跨多个目录集中管理，并给 AI 一个统一控制入口。

详细调研见 [同类项目代码调研](docs/competitive-research.md) 和 [进程运行器调研](docs/process-runner-research.md)。

## GitHub 关键词

推荐搜索和仓库主题：

```text
windows, service-manager, task-runner, system-tray, developer-tools,
local-development, process-manager, cli, ai-tools, automation,
wpf, dotnet, npm, vite, powershell
```

更多仓库配置建议见 [仓库配置](docs/repository-profile.md)。

## 从源码构建

要求：

- Windows
- .NET SDK 8.0+

```powershell
dotnet build .\ServicePilot.sln
dotnet publish .\ServicePilot\ServicePilot.csproj -c Release -r win-x64 --self-contained false -o .\dist
```

## 文档

- [AI 使用说明](docs/ai-usage.md)
- [宣传截图指南](docs/screenshot-guide.md)
- [同类项目代码调研](docs/competitive-research.md)
- [进程运行器调研](docs/process-runner-research.md)
- [隐私说明](PRIVACY.md)
- [贡献指南](CONTRIBUTING.md)
- [更新日志](CHANGELOG.md)

## 隐私

ServicePilot 是本地工具。它不会上传文件、路径、日志、配置或机器名。

## 许可证

MIT。
