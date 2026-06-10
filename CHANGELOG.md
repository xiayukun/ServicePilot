# 更新日志

[English](CHANGELOG-en.md)

ServicePilot 还未正式公开发布。上线前的内部打磨不会作为用户可见的修改流水账保留。

## 1.0.0 - 2026-06-10

首个公开版本提供以下能力：

- Windows 托盘优先的本地开发服务管理。
- 大号数字托盘图标，显示正在运行或启动中的服务数量。
- 中英文界面，默认跟随 Windows 语言，并可在托盘右键菜单中切换。
- 服务管理和模板管理 GUI。
- Batch、PowerShell、Python、Node.js 多步骤脚本。
- 启动执行步骤与手动执行步骤分组。
- 服务预设变量和步骤变量。
- 变量注入为 `SERVICEPILOT_VARIABLE`，并替换 `{{variable}}` / `{{变量}}`。
- 完整服务模板，不绑定工作目录。
- 模板导入/导出，方便分享 `.servicepilot-template.json` 文件。
- 内置通用“默认开发动作模板”，包含 Git、npm 和常用工具打开动作。
- 实时日志窗口，支持搜索、复制、复制全部、横向滚动和有限缓存。
- Windows Job Object 进程组清理，减少 npm/Vite 子进程残留。
- AI/脚本友好的 CLI，包括 `ai-help`、JSON 查询、服务/模板 CRUD、步骤执行和步骤变量维护。
- `doctor [--json]` 配置体检，用于检查缺失目录、空步骤、重名和重复变量。
- JSON 输出默认保留中文，方便人和 AI 直接阅读。
- 配置保存在 `%APPDATA%/ServicePilot/config.json`。
- 变量最近使用缓存保存在 `%APPDATA%/ServicePilot/variable-usage-cache.json`。
