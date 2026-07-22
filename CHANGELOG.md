# 更新日志

[English](CHANGELOG-en.md)

本文只记录公开发布版本的用户可见变化。

## 4.0.2 - 2026-07-22

- **修复：折叠组被日志刷新重新弹开**: 日志持续输出时，手动关闭一个折叠组后，若该组随后又有新行输出，折叠状态会被错误地重新展开。原因是增量重建折叠时 AvalonEdit 可能销毁并重建折叠区，导致用户的折叠状态丢失。现在按组头记录折叠意图并在每次重建后回写，手动折叠/展开状态在持续输出和切换标签时都能稳定保持。

## 4.0.1 - 2026-07-22

- **修复：日志折叠错位（多线程乱序）**: 进程的 `stdout` 与 `stderr` 由两个并发线程读取，此前可能导致后产生的日志行先进入日志页面，喂给依赖顺序的合并/折叠状态机后出现“折叠组头平铺、明细堆在底部、错误起始行错位”。现在同一步骤的输出按真实读取顺序串行提交，折叠与日志顺序恢复正确。合并函数本身无需改动。

## 4.0.0 - 2026-07-21

本次是一次重大功能版本：引入可编程的日志合并脚本引擎与 VSCode 式日志折叠、右侧颜色概览导航、外部配置文件热加载，并全面刷新品牌图标。

- **日志合并脚本引擎（Log Merge Script）**: 步骤/模板动作支持自定义 C# 合并函数，可将多行日志按规则折叠为一行摘要并着色。脚本入参新增 `PreviousResult`、`PreviousWasCollapsed`、`InCollapseGroup`，返回结果新增 `State` 字典用于跨行携带状态（仅运行期，不持久化）；服务/模板编辑页新增合并函数代码框并预填带注释的模板。CLI 提供 `merge-script test` 验证。
- **VSCode 式日志折叠**: 保留原始行的同时生成可展开折叠组，折叠时只显示摘要；搜索命中折叠内容会自动展开；新增“折叠全部/展开全部”摘要视图开关。折叠占位文字为白色，左侧加号与文本之间用约 100px 色块显示该组颜色（取被折叠首行颜色）。
- **右侧颜色概览导航**: 日志右侧新增静态可点击颜色概览图，按优先级聚合每行颜色（错误 > 警告 > 自定义 > 系统 > 普通），点击可快速跳转；感知折叠状态，仅反映可见日志分布。
- **外部配置热加载**: 直接编辑 `%APPDATA%\ServicePilot\config.v2.json` 后托盘自动检测并热加载，不再覆盖外部改动，保留运行中服务的运行状态。
- **菜单可滚动**: 托盘一级/二级及各级菜单在项目过多时可上下滚动，打开时自动回到顶部；样式在各级统一。
- **系统主题色**: 服务管理左侧选中条等强调色改为读取 Windows 系统主题色（DWM AccentColor）。
- **全新应用图标**: 采用更精致的青色透明圆角图标，同步应用到 exe 图标、任务栏图标以及所有 FluentWindow 标题栏左侧图标（日志、服务管理、模板管理、AI 帮助、服务/模板编辑、模板选择、变量输入、确认弹窗），消除旧图标的白边。
- **日志窗口标题栏**: 完整标题（如 `日志 - <服务名>`）移入标题栏显示，工具栏不再重复标题；标题过长自动截断，不再遮挡右侧操作按钮。
- **AI 帮助**: “复制给 AI 的帮助”与 `ai-help` 补回完整 CLI 帮助，并说明合并脚本契约、折叠语义与外部配置热加载用法。

## 3.0.0 - 2026-07-17

- **FluentWindow 界面升级**: 所有管理窗口（AI 帮助、日志、服务管理、模板管理、服务编辑、模板编辑、模板选择、变量输入）全面迁移为 WPF-UI `FluentWindow`，启用 `ExtendsContentIntoTitleBar` 现代标题栏，支持最小化/最大化/关闭按钮。
- **深色主题统一**: 新增 `MenuItem` 系统主题色 hover 高亮 + 左侧 3px 选中条；`ListBoxItem` 选中时显示系统主题色左侧边框条；所有 `TextBlock` / `Label` 统一深色前景色。
- **日志窗口工具栏重构**: 按钮样式升级为 `Appearance="Primary"` / `Appearance="Danger"`，布局使用 WPF-UI 一致的间距和圆角。
- **AI 帮助窗口迁移**: 加入 `TitleBar`、深色背景适配，移除旧版标题栏。
- **ServiceCommandProcessor 增强**: 新增 CLI 模板操作和扩展能力（141 行增量）。
- **ServiceManager UI 对齐**: 列表项选中样式、按钮布局全面对齐 FluentWindow 设计系统。

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
