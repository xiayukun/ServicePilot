# 更新日志

[English](CHANGELOG-en.md)

本文只记录公开发布版本的用户可见变化。

## 2.3.0 - 2026-07-14

- `template import` 增加 `--on-conflict` 选项（`rename`/`overwrite`/`skip`），反馈区分新建、覆盖、跳过、重命名，并回显文件绝对路径和模板 Id。
- `--json` 输出强制 UTF-8 编码且走 stdout（即使有 Error），解决管道 `| python` / `| jq` 中文乱码问题；exit code 保持语义不变。
- 新增 `step set-members`/`add-member`/`remove-member` 和 `template step set-members`/`add-member`/`remove-member` 细粒度命令，可直接操作组合动作的成员列表。
- `service edit`/`template edit`/`step edit`/`template step edit` 无实质变更时返回"未检测到变更"而非"已更新"。
- `service get`/`status` 标注默认启动组合动作（`DefaultStartStep`/`IsDefaultStartStep`），`start SERVICE` 运行哪个组合动作一目了然。
- 托盘图标数字变小，1 位 17pt → 2 位 14pt → 99+ 10pt。
- GitHub README 主图更新为包含右键上下文菜单的新截图。
- 模板动作 CLI 命令 `step edit`、`step remove`、`step move` 新增 `--json` 返回，方便脚本和 AI 解析操作结果。

## 2.2.0 - 2026-07-10

- 新增 step 级增量编辑 CLI 命令，支持对动作步骤进行细粒度的增、删、改操作。

## 2.1.1 - 2026-07-10

- 修复 AI 提示语（ai-help 和托盘右键菜单 "复制给 AI 的帮助"）不准确的问题。

## 2.1.0 - 2026-07-03

- 托盘右键菜单新增 `复制给 AI 的帮助`，可复制带当前 `ServicePilot.exe` 绝对路径的 AI 提示词。
- `ServicePilot.exe ai-help` 与托盘 AI 帮助窗口改为同源内容，减少 CLI 帮助和 UI 提示不一致。
- 通过运行中的托盘实例执行 CLI 配置变更后，托盘菜单、服务管理窗口、模板管理窗口和相关日志窗口会即时刷新。

## 2.0.0 - 2026-06-18

- 重构为 `Action` / `Composite` 模型。
- 活跃配置迁移到 `%APPDATA%/ServicePilot/config.v2.json`，旧 `config.json` 保留。
- 服务级预设变量迁移为动作级 `StepVariables`。
- 服务/模板编辑器支持组合动作成员编排。
- 中文界面和文档统一使用“动作”术语，动作类型显示为“动作 / 组合动作”。
- 日志窗口移除独立启动按钮，改为从“运行动作”统一运行；日志页签按动作懒创建，动作进入运行时会切换到对应页签。
- 日志窗口对非错误的 webpack 进度输出进行显示层合并，减少高频构建日志造成的卡顿。
- CLI `start` 运行第一个组合动作，`step run` 可运行动作或组合动作。
- 模板导入导出保留组合动作成员关系。

## 1.0.0 - 2026-06-10

首个公开版本提供以下能力：

- Windows 托盘优先的本地开发服务管理。
- 大号数字托盘图标，显示正在运行或启动中的服务数量。
- 中英文界面，默认跟随 Windows 语言，并可在托盘右键菜单中切换。
- 服务管理和模板管理 GUI。
- Batch、PowerShell、Python、Node.js 多动作脚本。
- 启动执行动作与手动执行动作分组。
- 服务预设变量和动作变量。
- 变量注入为 `SERVICEPILOT_VARIABLE`，并替换 `{{variable}}` / `{{变量}}`。
- 完整服务模板，不绑定工作目录。
- 模板导入/导出，方便分享 `.servicepilot-template.json` 文件。
- 内置通用“默认开发动作模板”，包含 Git、npm 和常用工具打开动作。
- 实时日志窗口，支持搜索、复制、复制全部、横向滚动和有限缓存。
- Windows Job Object 进程组清理，减少 npm/Vite 子进程残留。
- AI/脚本友好的 CLI，包括 `ai-help`、JSON 查询、服务/模板 CRUD、动作执行和动作变量维护。
- `doctor [--json]` 配置体检，用于检查缺失目录、空动作、重名和重复变量。
- JSON 输出默认保留中文，方便人和 AI 直接阅读。
- 配置保存在 `%APPDATA%/ServicePilot/config.json`。
- 变量最近使用缓存保存在 `%APPDATA%/ServicePilot/variable-usage-cache.json`。
