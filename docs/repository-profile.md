# 仓库配置

[English](repository-profile-en.md)

## 仓库名

```text
ServicePilot
```

## GitHub Description

推荐 GitHub About 描述（v4.2.0）：

```text
AI-friendly Windows tray manager for local development services — run npm, Vite, .NET, Python, and PowerShell with GUI + CLI.
```

如果 GitHub description 需要更短：

```text
AI-friendly Windows tray manager for local development services.
```

## Homepage

公开发布后推荐填：

```text
https://github.com/xiayukun/ServicePilot/releases/latest
```

## Topics

```text
windows
system-tray
service-manager
task-runner
process-manager
developer-tools
local-development
cli
ai-tools
automation
dotnet
wpf
wpf-ui
fluentwindow
vite
npm
powershell
log-management
log-viewer
logging
```

## 搜索关键词

```text
Windows service manager
Windows tray service manager
Windows tray app
Windows system tray
local development service launcher
local dev service manager
local dev launcher
AI friendly CLI service manager
AI agent tool
AI automation
npm run dev manager
Vite dev server manager
frontend dev server manager
PowerShell task runner GUI
Batch script runner
Node.js script runner
dotnet local service launcher
Windows process manager for developers
system tray task runner
Windows developer tool
process tree cleanup
Job Object
```

## README 首屏卖点（v4.2.0 更新）

- v3.0.0 全面升级为 WPF-UI FluentWindow 现代界面：所有管理窗口带 TitleBar 标题栏、系统主题色选中效果和统一深色主题。
- 下载并启动后，用户可以从托盘右键菜单复制 `复制给 AI 的帮助`，让 AI 直接拿到当前 `ServicePilot.exe` 绝对路径和首批检查命令。
- AI 可以先读 `ServicePilot.exe ai-help`、`ServicePilot.exe doctor --json` 和 JSON 状态，再安全创建个性化服务、模板、动作和变量。
- 托盘数字就是当前活跃服务数，适合长期挂在任务栏。
- 支持多项目、多目录、多脚本动作。
- 支持启动变量和手动动作变量，适合切换后端地址、环境名、配置值。
- 不安装系统服务，不要求管理员权限，更适合本地开发。
- Windows Job Object 清理进程组，解决 npm/Vite 停止后端口残留问题。
- 合并函数可在启动完成等实时节点发送 Windows 通知，日志窗口关闭时仍然有效。
- 托盘菜单支持打开后直接输入服务名筛选，服务再多也能快速定位。
- 动作日志标签按稳定动作标识区分；可关闭单个标签并清除其已保留日志，不停止仍在运行的动作。
- 高频输出时，自动滚动与折叠/展开交互以实际布局和折叠状态为准；进程完成或停止会排空已读取的输出，减少短命令尾部日志丢失。

## 社交预览文案

```text
ServicePilot helps developers hand local Windows dev services to AI: copy tray AI help, inspect JSON state, filter services instantly, and receive startup notifications from programmable log rules.
```

## 发布帖草稿

```text
ServicePilot 4.2.0 是一个 AI 友好的 Windows 托盘本地开发服务管理器。下载启动后，从托盘右键复制“给 AI 的帮助”，AI 就能先用 JSON CLI 读取真实状态，再帮你管理 npm/Vite、dotnet、Python、Batch、PowerShell、Node.js 脚本。这个版本支持合并函数在启动完成时发送 Windows 通知，并可在打开托盘菜单后直接输入服务名快速筛选。
```
