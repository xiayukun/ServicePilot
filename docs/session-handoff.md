# 会话交接

最后更新：2026-07-10

English counterpart: [session-handoff-en.md](session-handoff-en.md)

## 当前状态

ServicePilot 是一个 .NET 8 Windows 托盘优先的开发服务管理器。当前产品方向是托盘菜单、WPF 管理窗口、日志窗口和 CLI，不再提供桌面悬浮模式。

当前主线版本为 ServicePilot 2.2.0：

- 项目版本属性当前为 `2.2.0`（`ServicePilot/ServicePilot.csproj`）。
- 活跃配置文件是 `%APPDATA%\ServicePilot\config.v2.json`。
- 旧版 `%APPDATA%\ServicePilot\config.json` 只作为 v1 迁移来源读取，不删除、不覆盖。
- `SERVICEPILOT_CONFIG_DIR` 用于隔离测试，避免碰用户真实配置。
- 运行配置、私有服务名、本机路径、备份文件名、客户项目名、数据库/API 地址等机器专属信息不得写入可提交文档。
- 本机私有交接信息放在仓库根目录的 `LOCAL_NOTES.private.md`；该文件已由 `.gitignore` 忽略，不应提交。
- v2.2.0 新增 `step add` / `step edit` / `step remove` / `step move` CLI 命令，支持动作级增量编辑。
- 上一个已发布版本为 v2.1.1（tag `v2.1.1`，commit `6b49baa`）。
- 活跃配置文件是 `%APPDATA%\ServicePilot\config.v2.json`。
- 旧版 `%APPDATA%\ServicePilot\config.json` 只作为 v1 迁移来源读取，不删除、不覆盖。
- `SERVICEPILOT_CONFIG_DIR` 用于隔离测试，避免碰用户真实配置。
- 运行配置、私有服务名、本机路径、备份文件名、客户项目名、数据库/API 地址等机器专属信息不得写入可提交文档。
- 本机私有交接信息放在仓库根目录的 `LOCAL_NOTES.private.md`；该文件已由 `.gitignore` 忽略，不应提交。

## 2.0 模型

ServicePilot 2.0 使用 `Action` / `Composite` 模型：

- `Action` 是可运行命令，包含脚本类型、脚本内容、动作变量、是否使用变量、是否弹出日志。
- `Composite` 是有序动作编排，只保存成员动作 id，不包含命令内容。
- `Composite` 不能嵌套 `Composite`。
- 编辑器保存时应校验：动作命令非空、组合成员存在、组合至少包含一个动作、组合内最多一个启用变量的成员动作。
- `start SERVICE` 运行该服务第一个 `Composite`。
- `step run SERVICE ACTION_OR_COMPOSITE` 可运行单个 `Action` 或指定 `Composite`。
- `RunOnStart` 和服务级 `PresetVariables` 只保留作旧配置迁移字段，不再作为新 UI 设计依据。

## 变量与 AI 使用

- 动作级变量保存在 `ScriptStep.StepVariables`。
- `UseVariable=true` 时，运行变量会注入 `SERVICEPILOT_VARIABLE`，并替换脚本中的 `{{variable}}` / `{{变量}}`。
- `UseVariable=false` 时，动作直接运行，不显示变量子菜单。
- 最近使用变量和最近使用服务由 `%APPDATA%\ServicePilot\variable-usage-cache.json` 缓存；它不是源配置。
- `ai-help` 是 AI/脚本入口。后续改 CLI 时必须让 AI 能先用 `doctor --json`、`list --json`、`status --json`、`step list --json`、`logs --json` 理解状态后再操作。
- 托盘右键菜单提供 `复制给 AI 的帮助`，由 `Views/AiHelpWindow` 展示当前 `ServicePilot.exe` 绝对路径、建议首批命令和可复制提示词。
- `AiHelpContentService` 是 `ServicePilot.exe ai-help` 和托盘 AI 帮助提示词的同源内容服务；后续更新 AI 指南应优先改这里。
- 公开文档、仓库简介和发布文案应优先引导 GitHub 下载用户“先启动 exe，再从托盘复制给 AI 的帮助”，避免让 AI 猜测下载后的 exe 位置。
- 面向 AI 的 CLI 输出应保持结构化、中文可读、错误明确，避免要求 AI 解析 UI 文案。

## UI 状态

- 用户可见中文术语统一为“动作”和“组合动作”，不要再把普通操作称为“步骤”。
- 动作类型下拉在中文界面显示“动作 / 组合动作”，英文界面显示 “Action / Composite”。
- 日志窗口不再有独立“启动”按钮，统一从“运行动作”菜单运行第一个组合动作、指定组合动作或单个动作。
- 日志窗口页签懒创建：不再默认显示“全部”或“服务”；动作进入 `Running` 时激活对应动作页签，即使这个页签已经存在；无动作名的系统日志只有实际出现时才创建服务页签。
- 持续输出日志时不能仅因为新日志反复抢占用户当前页签；页签切换由动作运行状态驱动。
- 日志窗口仍需保留搜索、复制、水平滚动、自动滚动；每个可见页签最多渲染最近 5000 行，并批量刷新高频日志，避免 webpack/Vite 进度日志卡死 UI。
- 日志窗口会把非错误的 `[webpack.Progress] NN% ...` 进度行在显示层合并成一条带文本进度条的日志；底层日志缓存和 CLI JSON 不应因此丢失原始日志。
- 托盘 tooltip 和状态行只显示运行数、总数、失败数，不显示服务名或变量，避免菜单过长。
- 托盘和管理窗口的服务列表按最近使用优先排序，但不要改动持久化 `SortOrder`。
- 通过运行中托盘实例执行 CLI 配置变更后，`App.RefreshAfterCommand` 会按命令类型刷新托盘菜单、已打开的服务管理窗口、模板管理窗口和相关日志窗口。

## 打包与发布

- 正常构建检查：`rtk dotnet build ServicePilot.sln`。
- 单文件发布命令：`rtk dotnet publish .\ServicePilot\ServicePilot.csproj -t:Rebuild -c Release -o .\dist`。
- `Release` publish 默认应产出单个 `ServicePilot.exe`。
- 如果运行中的 exe 锁定 `dist`，先发布到 `dist-staged`。
- 每次成功产出 exe 后，如果 `LOCAL_NOTES.private.md` 存在，按其中的本机私有复制目标处理；不要把目标路径写入可提交文档。
- 当前阶段用户要求：先产出 exe 给用户测试，不提交、不打 tag、不发 GitHub Release，除非用户明确要求。
- v2.1.0 已发布（tag `v2.1.0`），v2.1.1 已提交并打 tag（tag `v2.1.1`，commit `6b49baa`），但尚未 push 到 remote 或发布 GitHub Release。
- 发布说明草稿位于 `docs/release-notes-v2.1.0.md` / `docs/release-notes-v2.1.0-en.md`；v2.1.1 的可选发布说明待定。
- GitHub Release 页面已有标题，发布 notes body 不要再额外加重复一级标题。

## 文档规则

- 中文是主文档语言，英文配套文件使用 `-en.md`。
- 修改用户可见行为时，同步更新 `AGENTS.md`、本交接文档、英文交接文档，以及相关 README / user guide / ai-usage / changelog。
- 当前面向新用户的对外文档应使用“动作 / 组合动作”和 action/composite 表述；仅历史发布说明或兼容 CLI 名称中保留 step/步骤。
- 敏感信息不要写入 README、用户指南、交接文档、AGENTS、release notes、issue/PR 模板。
- 如果确实需要记录本机特殊部署、私有服务、客户项目或截图来源，写入 `LOCAL_NOTES.private.md`。

## 验证建议

每次功能修改至少执行：

```text
rtk dotnet build ServicePilot.sln
```

涉及配置迁移或 CLI 时，使用隔离目录验证：

```text
set SERVICEPILOT_CONFIG_DIR=<temporary-test-dir>
ServicePilot.exe doctor --json
ServicePilot.exe ai-help
ServicePilot.exe list --json
ServicePilot.exe step list SERVICE --json
```

涉及运行时行为时，还要验证：

- 第一个 `Composite` 可运行。
- 指定 `Composite` 可运行。
- 单个 `Action` 可运行。
- `UseVariable=false` 动作不弹变量菜单。
- 变量新增后写入动作变量并更新最近使用排序。
- `Stop` 能停止该服务全部运行内容。
