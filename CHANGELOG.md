# 更新日志

[English](CHANGELOG-en.md)

ServicePilot 还未正式公开发布。上线前的内部打磨不会作为用户可见的修改流水账保留。

## 1.0.0 - 待发布

首个公开版本将提供以下能力：

- Windows 托盘优先的本地开发服务管理。
- 大号数字托盘图标，显示正在运行或启动中的服务数量。
- 中英文界面，默认跟随 Windows 语言，并可在托盘右键菜单中切换。
- 服务管理和模板管理 GUI。
- Batch、PowerShell、Python、Node.js 多步骤脚本。
- 启动执行步骤与手动执行步骤分组。
- 服务预设变量和步骤变量。
- 变量注入为 `SERVICEPILOT_VARIABLE`，并替换 `{{variable}}` / `{{变量}}`。
- 完整服务模板，不绑定工作目录。
- 实时日志窗口，支持搜索、复制、复制全部、横向滚动和有限缓存。
- Windows Job Object 进程组清理，减少 npm/Vite 子进程残留。
- AI/脚本友好的 CLI，包括 `ai-help`、JSON 查询、服务/模板 CRUD、步骤执行和步骤变量维护。
- `doctor [--json]` 配置体检，用于检查缺失目录、空步骤、重名和重复变量。
- JSON 输出默认保留中文，方便人和 AI 直接阅读。
- 配置保存在 `%APPDATA%/ServicePilot/config.json`。
- 变量最近使用缓存保存在 `%APPDATA%/ServicePilot/variable-usage-cache.json`。

上线前仍需重点验证：

- 更多真实开发服务的启动、停止、重启。
- 发布包和 GitHub Release 下载链路。
- README 截图和发布说明。
- 中英文截图按 `docs/screenshot-guide.md` 准备。
