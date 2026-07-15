# 更新日志

[English](CHANGELOG-en.md)

本文只记录公开发布版本的用户可见变化。

## 2.4.2 - 2026-07-15

- **P0 修复**: `config reload` 前先落盘内存配置，修复 CLI 修改在 reload 时被磁盘旧配置覆盖的问题。
- **P1 改进**: 加载配置时自动规整步骤的 Order 序号（排序后重分配 0-based 连续值），修正手动编辑 JSON 导致的负数/重复 Order。

## 2.4.1 - 2026-07-14

- "复制给 AI 的帮助"弹窗去除程序路径和建议先运行段落，只保留核心操作指南。

## 2.4.0 - 2026-07-14

- 编辑动作后 Id 保持恒定，不再重新分配，组合成员引用不再悬空。
- 新增 `config reload` 命令，通知托盘重新加载配置文件到内存，无需重启。
- 新增 `config apply --file PATH` 命令，校验并应用外部 JSON 配置，失败时自动回滚到缓存配置。
- `step move --position` 支持 `first`/`0` 表示置顶，明确 0-based 位置序号语义。
- `doctor` COMPOSITE_MEMBER_MISSING 报告具体悬空成员 Id 列表。
- `ai-help` 和"复制给 AI 的帮助"增加配置文件路径和 JSON 结构概要说明。
- COMPOSITE_VARIABLE_MEMBER_MULTIPLE 只统计有效成员，有悬空引用时不再误报。
- 保存配置时自动剔除悬空的组合成员引用。

## 2.3.1 - 2026-07-14

- `step add --use-variable` 默认改为 false（原默认 true 导致 UseVariable=True+VarCount=0 矛盾）。
- `step add` 同名动作直接拒绝，不允许重名。
- `step edit` 回显生效后关键字段（UseVariable/Variables/Type），省去改完后再 list 校验。
- `doctor` 成功产出报告即 exit 0，体检结果通过 JSON Counts 表达。
- `doctor` 新增 UseVariable=True+VarCount=0 告警（STEP_USEVARIABLE_NO_VARS）。
- 用名称定位 edit/remove 时同名多个会报错要求用 GUID。
- `ai-help` 统一术语说明（step=Action=动作）、列全 --type 枚举、说明默认值和语义。

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
