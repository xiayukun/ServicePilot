# 会话交接

最后更新：2026-07-22

English counterpart: [session-handoff-en.md](session-handoff-en.md)

## 修复发布：ServicePilot 4.0.2（2026-07-22）

- **问题**：日志持续输出时，手动关闭一个折叠组后，该组又有新行输出，折叠会被错误地重新弹开。
- **根因**：增量 `RebuildFoldings` 里，活跃组的 child 增加会让 AvalonEdit 在 `UpdateFoldings` 时销毁并重建该折叠区；旧逻辑用 `_foldingInitialized`（HashSet，只在组头首次出现折叠一次）记录，重建后既不补折、又丢了用户手动折叠态，于是弹开。
- **修复**：改用 `_foldStateByHeader`（`Dictionary<LogEntry,bool>`，按组头记录折叠意图，默认折叠）。`RebuildFoldings` 在 `UpdateFoldings` 前把当前各 section 的 `IsFolded` 采集进字典（捕获用户手动切换），之后按字典权威回写；仅在清空日志时清字典，切 tab 不清（保留每组状态）。所有手动入口（fold margin 点击、搜索展开、摘要按钮）都被"重建前采集"统一覆盖，无需各自挂钩。
- 版本 `4.0.2`；更新 `CHANGELOG` 中英、新增 `docs/release-notes-v4.0.2.md`、`AGENTS`（折叠状态持久化约定）。构建 0 警告 0 错误。按用户要求覆盖本地部署并推 GitHub Release（tag `v4.0.2`）。

## 修复发布：ServicePilot 4.0.1（2026-07-22）

- **问题**：某 Java/Spring API 服务启动时日志折叠错位——折叠组头平铺在上、明细堆在底部、错误起始行错位（见用户截图）。
- **排查过程（先分析后改）**：
  - 用 `merge-script test` 对两组贴近截图的真实样本(启动日志+错误+堆栈 16→4；请求波+SQL+错误 12→4)验证，**合并函数逻辑完全正确**，排除脚本问题。
  - 确认用户运行的是新版 exe（含 `PreviousResult/InCollapseGroup/State`）。顺带发现 `dist-staged` 是加 globals 前的旧构建但版本号也是 4.0.0（同号不同内容，易混淆）。
  - CLI 一次性全量跑折叠正确，而 UI 是逐行增量——差异定位到 UI；进一步顺着"后产生的行先进页面"的假设,查到根因。
- **根因**：`ProcessRunner` 的 `stdout`/`stderr` 由两个并发 `PumpOutputAsync` 任务读取，经 `ProcessManager.RunOnUiThread`(`Dispatcher.Invoke`)投递。多线程下入队顺序=线程抢占顺序，导致**后产生的日志行可能先进入 `LogEntries`**，喂给依赖顺序的折叠状态机(`LogWindow.ApplyMerge`)就错乱。
- **修复**：`ProcessRunner` 新增 `_emitGate` 锁,所有输出(stdout/stderr/系统提示)统一走 `Emit(...)` 串行提交;`ProcessManager` 保持阻塞式 `Dispatcher.Invoke`(持锁期间阻塞→严格保序),并加注释禁止改为 `BeginInvoke`。合并函数不动。
- 版本 bump 到 `4.0.1`(`csproj` + `AGENTS.md`);`CHANGELOG`/`CHANGELOG-en` 加 4.0.1 条目;新增 `docs/release-notes-v4.0.1.md`(按新规范:中文正文 + 底部仅链 `CHANGELOG-en`)。
- 构建 0 警告 0 错误。按用户要求推 GitHub 并建 Release(tag `v4.0.1`),**本地不覆盖**(用户自行下载)。

## 发布：ServicePilot 4.0.0（2026-07-21）

- 版本从 3.x 提升到 `4.0.0`（`csproj` + `AGENTS.md`），作为整合本会话全部新功能的重大版本发布。
- 图标白边根因是**源图 V1 自带不透明白底**；`scripts\make_icon.py` 已改为检测青色 squircle 边界 + 圆角遮罩抠图，导出透明 `app.ico`（exe/任务栏）与 `app.png`（标题栏 `ui:ImageIcon`，避免多帧 ico 缩放白边）。彻底清 `obj/bin` 重编译确保新图标嵌入 exe。
- README/README-en 顶部加入 hero 主图 `Assets/servicepilot-hero.png`（AI 生成，青色品牌调），并把展示 4.0 折叠/概览的日志截图 `Assets/screenshots/log-window-zh.png` 提到首屏。
- CHANGELOG/CHANGELOG-en 将原 3.1.0 条目整合为 `4.0.0` 发布条目。
- 通过 `gh` 提交、推送并创建 GitHub Release（tag `v4.0.0`，上传 `ServicePilot.exe`）。
- 本机部署目标是中文目录“同步软件”（在部分 shell 里显示为乱码 `ͬ������`，实为同一目录，30+ 软件）；用字节精确定位避免误建重复目录/误删。

## 更早改动：全新应用图标 + 版本 3.1.0（2026-07-21）

- 采用新的青色圆角图标（源图 V1）。用 `scripts\make_icon.py`（Pillow）裁掉透明留白、居中补边并导出多分辨率 `ServicePilot\Resources\Icons\app.ico`（16/24/32/48/64/128/256）。
- `app.ico` 作为唯一图标源：`csproj` 的 `<ApplicationIcon>`（exe 图标）、每个 `ui:FluentWindow` 的 `Icon`（任务栏）、每个 `ui:TitleBar.Icon`（标题栏左侧可见图标，`ui:ImageIcon` 18×18）。涉及全部 9 个窗口 XAML。
- 托盘徽章图标仍由 `App.CreateTrayIconWithBadge` 动态生成（显示运行数），**不**使用 `app.ico`，保持不变。
- 版本 bump 到 `3.1.0`（`csproj` + `AGENTS.md`），`CHANGELOG`/`CHANGELOG-en`/`README`/`README-en` 已加 3.1.0 条目（含本会话累计的合并脚本/折叠/概览/热加载/菜单滚动/系统主题色/图标标题栏等用户可见能力）。
- 构建 0 警告 0 错误后发布覆盖到本地私有目标。

## 更早改动：日志折叠可视化 + 托盘菜单（2026-07-21）

在「日志合并/折叠」批次基础上，完成了折叠的真实可视化渲染与相关 UI 细节。

日志折叠可视化（`LogWindow.xaml.cs` / 新增 `Views/FoldColorMarkerRenderer.cs`）：
- 折叠改为**真正的 AvalonEdit 折叠**（`FoldingManager.Install` 接入 TextView 行生成，真正隐藏折叠行），左侧有 `>`/`+` 展开切换；原始行始终保留，展开可见全部子行。折叠区从 header 行行首开始，折叠态只显示摘要 Title。
- 折叠内容可搜索：`FindLogMatch` 命中折叠区内的行时自动展开该折叠；`Summary` 按钮一键折叠全部/展开全部。
- 折叠占位**文字固定白色**（`FoldingElementGenerator.TextBrush` 全局静态，初始化设一次）。
- **多色折叠**：AvalonEdit 折叠框只能全局单色，无法逐区上色（`FoldingElementGenerator` 为 `sealed`）。改由 `FoldColorMarkerRenderer`（`IBackgroundRenderer` 叠加层）在 `+` 号与摘要文字之间画一个约 100px 的内容色块，颜色取被折叠**第一行**色；摘要 Title 用前缀空格（`GetFoldTitlePrefix`，按等宽字体空格宽度估算）把文字挤到色块右侧，二者不重叠。这是同屏显示多个不同色折叠的唯一支持方式。
- 右侧概览 `Views/OverviewMargin.cs`：贴近原生滚动条的彩色概览图，逐像素取最高优先级色（Error > Warning > 自定义 > System > 普通），折叠感知（折叠子行不占行），点击跳转；无可拖动缩略块（拖动会导致逐帧重绘卡顿），`InvalidateVisualCache` 有签名守卫避免纯滚动时重建。

托盘菜单：
- 曾尝试「点击运行/停止项后菜单不关闭（`StaysOpenOnClick`）」，用户体验不佳，**已全部回退**为点击即关闭（恢复运行后 `RebuildTrayMenu()`）。

合并脚本升级为「带跨行状态的流式函数」（2026-07-21）：
- 新增输入（`MergeScriptGlobals`）：`PreviousResult`（上一行返回的完整 `MergeResult`）、`PreviousWasCollapsed`、`InCollapseGroup`。
- 新增输出（`MergeResult`）：`State`（`Dictionary<string, object?>`），本行返回后作为下一行 `PreviousResult.State`，可做累计/去重/条件折叠。
- 约束：仅运行期、不落盘、重建 tab 不恢复；只存简单类型（string/int/double/bool，因脚本跑在可回收 ALC）；每 tab 独立（`LogTabState.LastResult`）。
- 落地点：`MergeScriptGlobals.cs`、`MergeResult.cs`、`LogMergeService.BuildSource`（注入新局部变量，`UserBodyStartLine` 16→19）、`LogWindow.ApplyMerge`、`ServiceCommandProcessor.MergeScriptTestAsync`（CLI test 同样携带状态）；编辑框预填注释、AI 帮助（中英）、AGENTS 均已同步。

## 更早改动：日志合并折叠修复（2026-07-20）

修复了「设置了 `LogMergeScript` 但日志窗口进度行不折叠」的问题。两个真实根因：

1. `LogWindow` 从未消费 `MergeResult.Collapse`：只替换了文本和颜色，没有实现折叠渲染。（本轮已进一步演进为真实 AvalonEdit 折叠，见上。）
2. `LogMergeService.BuildReferences` 缺少 `System.Text.RegularExpressions` 等引用，导致任何用 `Regex` 的脚本运行时编译失败并被静默吞掉（用户脚本正是用了 `Regex`）。现已补齐引用，并在 `BuildSource` 预置 `using System.Text.RegularExpressions;` / `using System.Globalization;`（同步更新了 `UserBodyStartLine`）。

配套改动：
- `merge-script set` 现在会先编译校验，失败拒绝保存（`--skip-validate` 强制）；运行时编译失败会在服务日志里以 `MergeScriptCompileError` 提示一次，不再静默。
- 新增 `merge-script test SERVICE STEP --file lines.txt [--json]`：逐行喂入 CurrentLine，输出命中/MergedMessage/Color/Collapse 及最终渲染结果，无需真实跑服务即可验证。已用真实脚本+日志离线验证 8 行→3 行、单文件发布版同样通过。
- 契约明确并写入 AGENTS.md / AI 帮助：`PreviousLine`/`CurrentLine` 是完整整行 `"HH:mm:ss [Level] message"`；合并脚本每行实时读取当前配置（`UpdateService` 更新 `RuntimeState.Config`），改后下一行即生效无需重启；`Color` 支持任意 WPF 颜色；`Children` 预留未渲染。

## 当前状态

ServicePilot 是一个 .NET 8 Windows 托盘优先的开发服务管理器。当前产品方向是托盘菜单、WPF 管理窗口、日志窗口和 CLI，不再提供桌面悬浮模式。

当前发布版本为 ServicePilot 3.0.0：

- 项目版本属性当前为 `3.0.0`（`ServicePilot/ServicePilot.csproj`）。上述「日志合并/折叠」两批改动尚未 bump 版本、尚未提交，提交时需决定新版本号并同步 CHANGELOG。
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

- 正常构建检查：`dotnet build ServicePilot.sln`。
- 单文件发布命令：`dotnet publish ./ServicePilot/ServicePilot.csproj -t:Rebuild -c Release -o ./dist`。
- `Release` publish 默认应产出单个 `ServicePilot.exe`。
- 如果运行中的 exe 锁定 `dist`，先发布到 `dist-staged`。
- 每次成功产出 exe 后，如果 `LOCAL_NOTES.private.md` 存在，按其中的本机私有复制目标处理；不要把目标路径写入可提交文档。
- 覆盖本机安装目标前，先自行检测目标 exe 是否被进程占用（如 `Get-Process ServicePilot`），仅在被锁时才请用户关闭，不要默认要求用户关闭。
- 当前阶段用户要求：先产出 exe 给用户测试，不提交、不打 tag、不发 GitHub Release，除非用户明确要求。
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
dotnet build ServicePilot.sln
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
