# 会话交接

最后更新：2026-08-25

English counterpart: [session-handoff-en.md](session-handoff-en.md)

## 4.2.0 发布候选（2026-08-25）

- 本轮累积功能以中版本 `4.2.0` 发布：合并函数实时 `Notify("通知文字")`、托盘服务名即时筛选、模板交换保留 `LogMergeScript`，并包含持续输出末尾折叠区自修复、AvalonEdit 鼠标异常规避和透明图标边缘修复。
- 发布前验证：`dotnet build ServicePilot.sln --nologo` 为 0 警告、0 错误；并发回归 harness 5/5，通过模板脚本交换与实时通知专项 2/2；图标 PNG 四角 alpha 均为 0。Release 单文件发布成功，`dist` 仅有 `ServicePilot.exe`，版本输出 `4.2.0`，文件版本 `4.2.0.0`，隔离 `doctor --json` 为 0 错误、0 警告，AI 帮助已包含 `Notify` 契约；候选包 SHA-256 为 `84fdadecb429b6aee40d1b967699df48c8b3bcf7b8d5af5fc42d2303cf681d4f`。
- 发布前真实状态回读有 2 个受管服务处于运行状态，因此遵守私有部署规则，不停止服务、不覆盖本机同步软件中的 EXE；GitHub 推送、CI 和 Release 结果将在发布完成后回填。

## 合并函数系统通知与托盘即时筛选（2026-08-20）

- **实时通知契约**：合并函数新增 `Notify("通知文字")`。`LiveLogMergeProcessor` 只用 Roslyn 语法识别真正的函数调用，注释/字符串不会触发后台求值；带通知的脚本在应用级实时日志通道执行，因此日志窗口关闭时仍有效。结果以瞬态字段附在 `LogEntry` 上，窗口直接复用，避免同一行再次执行和重复副作用。历史日志回放与 `merge-script test` 只收集/预览通知请求，不弹系统通知；同一服务动作五秒内相同文字去重。
- **托盘筛选**：一级托盘菜单顶部新增固定搜索框，打开菜单后清空并自动聚焦，可直接输入服务名做不区分大小写的包含匹配。向下键进入第一个可见服务，`Esc` 优先清空已有查询；底部新增/管理/状态/退出区域始终固定且不参与过滤。
- **当前验证与本机更新**：项目最终 Debug 构建为 0 警告、0 错误；通知专项 harness 已验证跨行状态、真实调用、注释/字符串排除、清空重置及预览不弹窗，隔离的真实 CLI `merge-script set/test` 也确认通知可编译并只在测试输出中预览。托盘过滤 harness 已验证中英文不区分大小写匹配和清空恢复；由于 WPF `ContextMenu` 会进入弹出层消息循环，自动聚焦与直接键入仍需部署后的真实托盘验收。Release 单文件发布成功，目录只有 `ServicePilot.exe`，版本为 4.1.0，隔离 `doctor --json` 为 0 错误、0 警告。用户随后明确要求覆盖并恢复之前运行的两个服务；再次检查时托盘实例与受管服务均已停止，最近使用缓存明确锁定两个目标。私有副本已覆盖，源/目标 SHA-256 一致，新版托盘隐藏启动且进程唯一；两个目标的精确启动 Action 已并发发送，一次状态回读均为 `Running`。

## API/前端启动完成通知（2026-08-20）

- 已盘点活动配置中的 36 个服务、6 个模板：11 个 `启动 API` 服务动作、21 个 `启动服务` 前端动作，以及 4 个对应模板动作全部写入通知脚本；原本没有合并脚本的前端动作也已补齐。
- API 只有在日志出现精确完成标记“所有启动任务执行完成”时通知“API 启动完成”；前端只有在 `[webpack.Progress] 100%` 时通知“前端启动成功（100%）”。两组脚本均通过 `PreviousResult.State` 防止同一轮启动重复通知，普通进度、stderr 和历史回放不会触发系统通知。
- 已用 `merge-script test` 预览真实条件：API 和前端各收到 1 条对应通知请求；预览不会弹系统通知。`doctor --json` 仍为 0 错误、15 个既有警告；未启动、停止或重启业务服务。
- 修复 `TemplateExchangeService` 的导入/导出克隆，确保模板动作的 `LogMergeScript` 不会在模板交换时丢失；`dotnet build ServicePilot.sln --nologo` 通过，0 警告、0 错误。运行中的旧托盘进程未重启，模板脚本已写入活动配置，更新后的模板内存缓存需在下一次启动新版 ServicePilot 时刷新。

## 前端启动通知按动作轮次去重（2026-08-20）

- **根因**：前端脚本原本只在 `webpack.Progress 100%` 返回 `frontendNotified=true`；中间的普通日志或 `[WARNING]` 会返回 `null`，把上一行的 `State` 清掉，热更新再次到 `100%` 就会重复通知。
- **修复**：前端 21 个服务动作和 3 个前端模板动作现在会在状态已建立后，让普通日志、警告和未匹配进度行继续携带 `State`；首次 `100%` 只通知一次。`App.OnProcessStepStateChanged` 在动作进入 `Running` 时清理实时合并状态，因此下一轮重新启动动作会重新允许一次通知。
- **验证**：`merge-script test` 使用“首次 100% → 普通 WARNING → 两次热更新 100%”的 5 行样本，通知请求始终为 1 条，折叠渲染为 3 行；`web` 与 `screen` 均通过。活动配置回读确认 21 个前端服务、3 个前端模板脚本一致，11 个 API 脚本未改。
- 本轮没有启动、停止或重启业务服务；状态回读显示 `leniu-tengyun` 原本处于 `Running`，未对其做任何操作。源码生命周期修复需随新版 ServicePilot 构建/部署后生效，当前旧托盘进程未重启。

## 持续输出时末尾折叠区自修复（2026-08-20）

- **现象与证据**：标准版 API 持续输出时，窗口末尾偶发保留约 3 条原始行；点击“摘要”后按钮虽变为“展开”，这些行仍不隐藏。实时 `logs --json` 回读确认对应条目已经正确标记为折叠子行，因此排除合并脚本；按钮无法控制这些行则说明它们不在当前 `FoldingManager` 的有效区间内。
- **修复**：`RebuildFoldings` 在每次 `UpdateFoldings` 后核对实际 section 与当前分组计算出的全部起止范围，仅在发现数量或范围不一致时清理并重建 section，再按 `_foldStateByHeader` 恢复用户意图。工具栏“摘要/展开”在切换前也先同步一次，因此即使末尾 section 曾缺失，用户操作也会先修复再展开/折叠。
- **验证与本机更新**：纯 AvalonEdit 末尾增长、追加新组、真实 5000 条日志结构及真实 WPF 渲染测试均通过；项目自己的 `LogWindow` 连续 50 批“组头 + 2 子行”压力测试无遗留队列。另人工移除最后一个 section 复现“末尾三行不受控制”后，两次摘要/展开可恢复精确尾部区间并重新折叠。`dotnet build ServicePilot.sln --nologo` 为 0 警告、0 错误；Release 发布目录只有单个 EXE，隔离 `doctor --json` 为 0 错误。覆盖前复查无活动服务，已更新本机私有副本、校验源/目标 SHA-256 一致并隐藏重启；重启后进程唯一且仍无活动服务。

## 新版菜单升级日志折叠（2026-08-18）

- 根据当前标准版 API 的实际新版菜单授权日志，移除旧版菜单审核/线程池识别，改为仅识别新版菜单授权请求及其同线程 SQL 流程。
- 折叠摘要现在会显示菜单和多语言的查询总量、分批 Updates 完成数、百分比和最终完成状态；同一请求后续的角色菜单清理不会计入多语言进度。
- 已通过 merge-script test 使用真实日志样本验证，且通过项目构建；本轮未停止或重启 API。

## 新版菜单升级折叠全量复制（2026-08-18）

- 将已验证的新版菜单升级折叠脚本复制到活动配置中的 11 个 API 服务“启动 API”动作，以及“Java Maven API”模板的同名动作。
- 服务动作通过 merge-script set 逐项编译校验并刷新运行时；模板通过官方 config apply 应用。最终回读确认 12 个目标脚本逐字一致，均只保留新版识别。
- 使用新版日志格式的关键流程样本验证，折叠摘要可从准备状态推进到菜单 2510/2510、多语言 1835/1835 的完成状态；未启动、停止或重启业务服务。

## 当前活动配置动作修复（2026-08-13）

- 盘点本机活动配置中的 36 个服务和 6 个模板，找到 11 个服务动作与 1 个模板动作会在修改数据库地址时额外生成备份文件。
- 已统一改为直接写回目标配置文件，保留原有数据库地址校验、master/slave 两处修改和成功输出；旧版只读迁移数据与历史配置快照未修改。
- 已验证活动配置仍可解析，`doctor --json` 错误数为 0，目标动作中不再存在该备份生成逻辑；本轮未启动、停止或重启业务服务。

## 当前工作树修复（2026-08-13）

- **图标圆角白边**：确认项目资产中的 `servicepilot_icon_final.png` 是带 Alpha 的透明源，但其边界仍带白色蒙版；生成脚本现在保留完整正方形画布，把四角设为透明，用 alpha 遮罩去除外轮廓蒙版，并把可见主体保持在原来的约 91% 画布比例，再对 PNG 与 ICO 各尺寸使用预乘 alpha 缩放。实机验证标题栏使用 PNG 后白边消失，而任务栏使用 ICO 时仍有轻微亮边，因此所有 FluentWindow 的 `Icon` 也改为直接加载同一 PNG；ICO 仍保留给 exe 的 Windows Shell 图标。
- **日志折叠**：本轮未修改日志逻辑。4.1.0 已包含输出顺序串行化、进程结束后的输出排空、折叠状态采集/恢复以及布局驱动滚动修复；从 4.0.2 升级到 4.1.0 后该问题未再复现。

## 当前工作树修复（2026-08-17）

- **AvalonEdit UI 线程异常**：用户在服务失败后打开日志窗口并移动鼠标时出现 `FileNotFoundException: System.Windows.Forms`；调用栈落在 AvalonEdit `TextArea.ShowMouseCursor()`。上游实现确认该可选功能会调用 `System.Windows.Forms.Cursor.Show/Hide`，因此在日志、服务编辑和模板编辑的全部 AvalonEdit 控件上关闭 `HideCursorWhileTyping`，不影响日志折叠、搜索、复制或脚本编辑。
- **验证与发布准备**：`dotnet build ServicePilot.sln --nologo` 通过，0 警告、0 错误；发布和本机同步软件覆盖需在本轮后续状态检查完成后执行。

## 4.1.0 对外截图：可关闭动作日志页签

- 新增公开资源 `Assets/screenshots/log-window-action-tabs-zh.png`，使用隔离的“公开演示服务”和合成构建输出拍摄；画面展示动作页签关闭 `×`、`展开` 折叠控制、自动滚动以及折叠后的构建摘要。
- README 中英文均已改用新截图；`docs/screenshot-guide.md` 与英文版同步标记旧 `log-window-zh.png` 为历史资源，重新公开引用前必须复查脱敏。
- 脱敏范围：截图不含真实工作目录、内网地址、令牌、客户名或家庭/个人信息；演示日志仅使用公开通用文本。

## 4.1.0 发布内容：自动滚动绑定日志布局

- **根因**：旧逻辑在当前 Tab 的批量日志写入后启动固定 120ms `DispatcherTimer`，随后用 `ScrollToLine` 定位。固定延迟与 `TextDocument` 插入、折叠区重建及 AvalonEdit/WPF 生成最新滚动范围没有因果关系；高频输出或复杂折叠布局下，计时器可能在最新 extent 生效前触发，导致视口停在旧末尾。
- **修复**：每个当前可见 Tab 的渲染批次只合并记录一次滚动意图，并且只在 Document 插入、折叠重建和 redraw 之后提出。`LogEditor.LayoutUpdated` 在真实布局完成后消费意图并调用 `ScrollToEnd`；消费标志先清除，避免滚动自身触发循环。关闭自动滚动、清空日志和关闭窗口都会取消待处理意图；重新开启开关会主动触发一次布局并立即到当前末尾。非当前 Tab 的日志仍只写入历史，不请求滚动。
- **验证**：专项源代码时序检查 7/7 通过；`dotnet build ServicePilot.sln --nologo` 成功，0 警告、0 错误。

## 4.1.0 发布内容：折叠意图与摘要/展开按钮同步

- **根因**：日志工具栏原有 `_summaryViewActive` 是脱离真实 `FoldingSection.IsFolded` 的第二份状态；单组折叠按钮、搜索展开、Tab 切换和 section 重建后它会陈旧，导致按钮文字与点击动作相反。重建时若旧 section 与新 Tab 文档 offset 混用，也可能把折叠意图关联到错误组头。
- **修复**：按钮现在只从当前 `FoldingManager.AllFoldings` 派生；单组点击由 `VisualLinesChanged` 即时保存以 `LogEntry` 组头为稳定键的意图，搜索展开和聚合点击也显式同步。增量重建先捕获真实状态、再在受保护的重建阶段恢复；Tab 全量重建则在替换旧文档前捕获，避免跨 Tab offset 串联。新组仍默认折叠，已不可达组头会从状态字典清理。
- **验证**：隔离状态机 harness 的 4 个场景通过；`dotnet build ServicePilot.sln` 为 0 警告、0 错误。

## 4.1.0 发布内容：可关闭动作日志页签与真清空

- **页签身份**：`LogEntry` 现在携带稳定 `StepId`，动作日志页签按 ID 而不是名称分组；同名但不同 ID 的动作不会串页签或误清。服务日志和没有稳定 ID 的历史兼容日志不显示关闭叉号。
- **关闭页签**：动作页签头提供可键盘聚焦的叉号及中英文本地化辅助名称。关闭只移除目标动作的应用级日志缓冲、待投递日志、页签集合、文档渲染、搜索续点、合并续态和折叠状态；若关闭当前页签则选择右侧（否则左侧）相邻页签，不停止动作或服务。关闭后产生的新日志仍可懒创建新页签，但旧日志不会恢复。
- **清空范围**：工具栏“清空”保持原有全局语义，即清空当前服务的全部应用级权威日志缓冲和窗口派生状态。关闭并重开日志窗口后，清空前内容不会恢复；清空后新日志仍正常追加。
- **验证**：静态专项检查 11/11 通过；Debug/Release 构建均为 0 警告、0 错误。关闭页签的 × 按钮已改为透明背景；4.1.0 的最终单文件包和本机覆盖由下游发布卡处理。

## 4.1.0 发布内容：进程退出、停止接管与尾部日志排空

- **问题与证据**：除短进程 `Process.Exited` 早于 stdout/stderr pump 排空外，审查还确认三个并发阻断项：取消检查到 runner 发布/启动之间存在 Stop 可漏接窗口；五秒 drain 超时被吞掉后仍发布 `Stopped`；`_emitGate` 持锁调用外部订阅者并同步进入 `Dispatcher.Invoke`。聚焦 harness 在修复前稳定得到 3/4 失败：发布窗口进程会在 Stop 返回后启动、drain 超时伪装成功、正常 Stop 重复发布两次终态。
- **修复**：`ScriptExecutor._runnerGate` 把取消复检、runner 发布/启动、Stop 接管、清理和释放串成同一边界。`ProcessRunner` 以单读者 channel 保存程序观测到的入队顺序，锁只保护入队/抑制状态，外部订阅者与 Dispatcher 均在锁外执行；不再声称还原 stdout/stderr 的绝对 OS 生成顺序。`Completion` 统一等待进程退出、双流 EOF 和已入队订阅者投递。Stop 的首轮五秒超时会再次强杀并关闭重定向流、抑制后续投递、排空已有回调后抛出 `TimeoutException`；`ProcessManager` 只发布一个 `Error`，正常停止只发布一个 `Stopped`。
- **验证**：`scripts/ServicePilot.ConcurrencyHarness` 5/5 通过：取消命中 runner 发布窗口；慢订阅者下 stdout/stderr 各 201 行（含无换行尾部）并保留非零退出码 7；drain 超时不伪装成功且 Stop 后无新回调；Manager 超时只发布一次 `Error`；正常 Stop 只发布一次 `Stopped`。`dotnet build ServicePilot.sln --nologo` 为 0 警告、0 错误；全程使用临时 `SERVICEPILOT_CONFIG_DIR`，未触碰真实服务或部署 EXE。

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

4.1.0 的版本字段、CHANGELOG 和中文 Release Notes 已整理完成；制品卡已生成最终单文件包并完成本机私有覆盖，GitHub 发布操作仍由后续流程负责：

- 项目版本属性当前为 `4.1.0`（`ServicePilot/ServicePilot.csproj`），四个版本字段统一为 `4.1.0` / `4.1.0.0`。
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
- 制品卡已通过 `dotnet publish ServicePilot/ServicePilot.csproj -t:Rebuild -c Release -o ./dist --nologo` 生成单文件包；`dist` 仅含 `ServicePilot.exe`，且 dist 与本机私有覆盖目标均返回版本 `4.1.0`、文件长度和 SHA-256 一致。该验证不等同于完整独立 QA；真实服务 GUI、长时运行和回归场景仍需后续验证。
- 本次文档整理不执行提交、打 tag 或创建 GitHub Release。
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
