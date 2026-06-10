# ServicePilot 1.0.0

英文发布说明：[release-notes-v1.0.0-en.md](release-notes-v1.0.0-en.md)

首次公开发布基线。

## 亮点

- 使用托盘菜单管理服务。
- 托盘数字显示当前运行/启动中的服务数量。
- 默认跟随 Windows 语言，并可在托盘右键菜单中切换中文或 English。
- 多步骤脚本配置，支持 Batch、PowerShell、Python、Node.js。
- 区分启动执行步骤和手动执行步骤，支持服务预设变量与步骤变量。
- 完整服务模板可复用名称、说明、步骤和变量，并保留目标工作目录。
- 日志窗口支持搜索、复制、复制全部、横向滚动和有限缓存。
- CLI 支持 JSON 输出、`ai-help`、`doctor --json`，适合 AI 和脚本自动化。
- 使用 Windows Job Object 清理进程组，减少 npm/Vite 子进程和端口残留。
- JSON 配置持久化到 `%APPDATA%`。

## 下载

- `ServicePilot.exe`

## 要求

- Windows
- .NET 8.0 运行时，或下载自包含可执行文件。
