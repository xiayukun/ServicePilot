# 会话交接

最后更新：2026-06-10

## 当前状态

ServicePilot 是一个 .NET 8 Windows 托盘优先的服务管理器。当前产品方向是仅托盘：不再提供桌面悬浮模式。

最新工作已经移除过度复杂的子服务设计，改为服务预设变量：

- `ServiceConfig.PresetVariables` 是字符串列表。
- 选中的变量会注入为 `SERVICEPILOT_VARIABLE`。
- 选中的变量也会在执行前替换脚本中的 `{{variable}}` 和 `{{变量}}`。
- `ScriptStep.UseVariable` 控制某个步骤是否使用变量。旧配置默认 `true`；为 `false` 时，该步骤会忽略选中的变量，执行步骤菜单也会直接运行而不展开变量子菜单。
- `ScriptStep.RunOnStart` 控制普通启动服务时是否执行该步骤。旧配置默认 `true`；为 `false` 时，启动服务会跳过该步骤，但仍可手动执行。
- 预设变量可以从托盘启动/重启菜单选择，也可以从单步骤执行菜单选择。
- 预设变量菜单顺序来自 `%APPDATA%\ServicePilot\variable-usage-cache.json`；隔离测试时跟随 `SERVICEPILOT_CONFIG_DIR`。
- 选择已有变量只记录缓存使用，不重排 `ServiceConfig.PresetVariables`。
- 在变量菜单选择 `新增` 会打开 `PresetVariableInputDialog`，默认填入最近使用变量；确认后把新变量保存到服务、记录使用，并立即执行当前启动/重启/步骤操作。
- CLI `--variable` 在命中运行中的托盘实例时会记录使用缓存，但不会自动把该值追加到持久预设变量列表。

模板现在是“无工作目录的完整服务模板”：

- `AppConfig.ServiceTemplates` 存储 `ServiceTemplate`。
- 模板包含名称、说明、脚本步骤、预设变量和时间戳。
- 应用模板会替换服务名称、步骤和预设变量，同时保留服务 id、工作目录、排序、创建时间和自启设置。

旧的“保持运行”选择框已经移除。最后一个命令如果持续运行，服务会自然保持 `Running`；如果以 `0` 退出则变成 `Completed`，非零退出则变成 `StartFailed`。

## 重要运行时说明

- `ProcessRunner` 通过 `cmd.exe /d /s /c` 执行 Batch，并加 `chcp 65001 > nul`。
- Batch 输出优先按严格 UTF-8 解码，失败时回退到本机 OEM 代码页。
- stderr 不会自动等于错误。`ProcessRunner` 会把 webpack 进度这类正常 stderr 输出归为 `Info`；真正失败仍由非零退出码和显式系统异常日志产生。
- PowerShell、Python、Node 步骤使用临时脚本文件。
- PowerShell 临时脚本必须写成带 BOM 的 UTF-8；Windows PowerShell 5 会把无 BOM UTF-8 当成本机 ANSI 代码页读取，导致中文字符串字面量变成乱码甚至语法错误。Batch 临时脚本仍必须保持无 BOM UTF-8，因为本机 `cmd.exe` 会把 BOM 当成第一条命令的一部分。
- 每个启动的进程都会放进带 kill-on-close 的 Windows Job Object。
- 空脚本步骤会在运行时跳过，以兼容旧配置；GUI 会阻止继续保存新的空步骤。
- 单独执行步骤时，如果服务原本停止，会在步骤运行期间把服务状态提升为 `Running`，所有独立步骤结束后再回到 `Completed`、`StartFailed` 或 `Stopped`。
- 普通启动服务只运行 `RunOnStart=true` 的步骤；未启动执行的步骤会标记为 `Skipped`，并保留在执行步骤菜单里。
- 停止时遇到无效进程句柄会按已经退出处理，避免偶发 `Win32Exception (6): invalid handle` UI 错误。
- 不要破坏 Job Object 清理路径；它用于防止 Vite/npm 子进程残留并继续占用 3000 端口。

## GUI 说明

- `App.RebuildTrayMenu` 构建托盘菜单。
- 托盘菜单状态和启用条件应该使用 `ServiceRuntimeState.State`；使用 ViewModel 的 `State` 可能慢一拍，短步骤完成后会显示旧的 `Starting`。
- 托盘图标动态生成大号活动服务数量，没有运行/启动中的服务时显示 `0`。
- 托盘悬停提示会显示活动服务和选中的变量。
- 启动或步骤最终失败时，会弹出节流的托盘冒泡，包含服务名和 1-2 行简短错误；完整错误保留在日志里。
- 服务编辑：`Views\ServiceConfigDialog.xaml(.cs)`。编辑已有服务时，可以把当前草稿另存为完整服务模板。
- 服务管理：`Views\ServiceManagerWindow.xaml(.cs)`。
- 模板管理：`Views\TemplateManagerWindow.xaml(.cs)`。
- 模板编辑：`Views\ServiceTemplateDialog.xaml(.cs)`。
- 变量输入：`Views\PresetVariableInputDialog.xaml(.cs)`。
- 服务管理和模板管理 DataGrid 显式使用深色样式，避免行和单元格变白。
- 日志窗口按钮使用本地样式，禁用按钮文字保持黑色可读。
- 日志窗口的启动、重启、执行步骤使用和托盘/服务管理窗口一致的预设变量菜单，日志工具栏也可以打开现有服务编辑窗口。
- 日志窗口按钮会订阅服务/步骤状态变化；服务运行/启动中或任何独立步骤运行时，启动按钮必须禁用。
- 打开日志窗口时使用 `LoadLogs()` 批量加载缓存历史，并对自动滚动做节流。不要用 `AddLog()` 循环回放缓存日志，否则长 webpack 日志会反复滚动并卡住 UI。
- 服务和模板的步骤编辑器在脚本类型旁边有 `使用变量` 和 `启动执行` 勾选框。
- 执行步骤菜单分成 `启动执行` 和 `不启动执行` 两栏。
- 托盘服务菜单和执行步骤菜单使用紧凑状态小点，不再给每一项加很长的状态前缀：运行/启动为绿色，失败/错误为红色，停止中/已取消为橙色，已停止/未执行/已完成不显示小点。
- 服务管理窗口的启动/执行步骤/停止/重启按钮按当前选中行的实时运行状态启用，不按全局状态判断。

## CLI 说明

重要命令：

```text
ServicePilot.exe ai-help
ServicePilot.exe doctor [--json]
ServicePilot.exe start SERVICE [--variable VALUE]
ServicePilot.exe restart SERVICE [--variable VALUE]
ServicePilot.exe step list SERVICE [--json]
ServicePilot.exe step run SERVICE STEP [--variable VALUE]
ServicePilot.exe step variables SERVICE STEP [--json]
ServicePilot.exe step variable-add SERVICE STEP --variable VALUE
ServicePilot.exe step variable-remove SERVICE STEP --variable VALUE
ServicePilot.exe step variable-clear SERVICE STEP
ServicePilot.exe service add --name NAME --dir DIR --step "Name|Batch|command" [--preset VALUE]
ServicePilot.exe service edit SERVICE [--preset VALUE] [--clear-presets]
ServicePilot.exe template add --name NAME --step "Name|Batch|command" [--preset VALUE]
ServicePilot.exe template save-from-service --service SERVICE --name NAME
ServicePilot.exe template apply TEMPLATE --service SERVICE
ServicePilot.exe template step-variables TEMPLATE STEP [--json]
ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-clear TEMPLATE STEP
```

旧的 `template create --template auto|node|dotnet|python --dir DIR` 仍保留，用于按文件夹创建服务。

`start all` 已移除。保留 `stop all`，但除非用户明确要求，不要重新加入批量启动 UI 或 CLI。CLI 步骤规格支持 `Name|Type|command`、`Name|Type|UseVariable|command` 或 `Name|Type|UseVariable|RunOnStart|command`。

`subservice` 命令现在会返回错误，提示子服务已移除，请使用预设变量和 `step run`。

CLI 处理器里凡是会保存配置的命令必须保持 async 贯通，不要在 WPF 命令模式里用 `.GetAwaiter().GetResult()` 阻塞异步保存；这会导致命令进程挂住并锁定 Debug exe。

`doctor [--json]` 是离线配置体检命令，会检查缺失目录、空步骤、重名、重复变量等问题。发现 Error 时退出码为 `2`。JSON CLI 输出保留中文直出，不再默认转义为 `\uXXXX`。

设置 `SERVICEPILOT_CONFIG_DIR` 后，CLI 默认跳过全局托盘命令管道，直接操作隔离配置，避免真实托盘实例正在运行时测试命令误打到真实配置。只有明确要连托盘管道时才设置 `SERVICEPILOT_ALLOW_TRAY_PIPE=1`。

## 文档方向

- 中文是主文档语言，默认 Markdown 文件优先为中文。
- 英文配套文件使用 `-en.md`，例如 `README-en.md`、`CHANGELOG-en.md`、`docs/ai-usage-en.md`。
- 中英文文档顶部互链。
- 项目还未正式上线，用户可见文档不要保留上线前修复流水账；CHANGELOG 采用“1.0.0 待发布”的初始版本口径。
- 新增 `docs/ai-usage.md` / `docs/ai-usage-en.md`，用于给 AI 和自动化脚本说明推荐工作流。
- 新增 `docs/competitive-research.md` / `docs/competitive-research-en.md`，记录不少于 10 个同类项目的代码和定位调研。
- `docs/repository-profile.md` / `docs/repository-profile-en.md` 记录 GitHub description、topics、搜索关键词和发布文案。

## 已完成验证

- `rtk dotnet build ServicePilot.sln`
- 使用 `SERVICEPILOT_CONFIG_DIR=%TEMP%\servicepilot-cli-test` 做隔离 CLI 配置。
- 离线 CLI：
  - 用旧兼容格式 `Name|Batch|false|command` 新增 `VarSvc`。
  - JSON 列出服务。
  - 从 `VarSvc` 保存完整服务模板。
  - JSON 列出模板。
- 运行中托盘 CLI：
  - 用 `--variable dev` 启动 `VarSvc`。
  - 确认日志出现 `cmd dev dev`。
  - 用 `step run VarSvc Echo --variable prod` 单独执行步骤。
  - 确认日志出现 `cmd prod prod`。
  - 关闭隔离托盘实例。
- 发布版 exe 运行时：
  - 已重新构建 `dist\ServicePilot.exe`。
  - 确认 `template apply` 会替换服务名称、步骤和预设变量。
  - 确认 `start PubVarSvc --variable alpha` 日志出现 `alpha alpha`。
  - 确认短命令服务可以执行后回到 `Completed`，立刻再次启动，并继续单独执行某个步骤。
- 真实 `screen` Vite 服务：
  - 测试前确认 3000-3006 没有监听。
  - 自启使用 `http://localhost:3000/`。
  - 停止后 3000-3006 全部清空。
  - 手动启动复用 3000。
  - 两次重启都复用 3000，没有监听 3001-3006。
  - 最终停止后 3000-3006 全部清空，并关闭了发布版托盘实例。

## 2026-06-09 步骤状态更新

- 新增内存级步骤运行状态：`NotRun`、`Running`、`Succeeded`、`Failed`、`Skipped`、`Cancelled`。
- 托盘和管理服务窗口的执行步骤菜单只禁用当前 `Running` 的步骤。
- 服务可以保持一个步骤运行，同时用选中的预设变量执行另一个步骤。
- 再次执行已经运行中的步骤会被拒绝。
- 停止服务会把运行中的步骤标记为 `Cancelled`。
- Volta shim 启动失败并返回 `126` 且输出包含 Volta 特征时，会做窄重试，再决定是否标记启动失败。
- 命令管道释放现在是幂等的，避免退出流程重复触发时出现 `CancellationTokenSource has been disposed`。
- 发布版 exe 已验证：保持一个长步骤运行、执行另一个变量步骤、拒绝重复执行运行中步骤、停止时取消运行中步骤。

## 2026-06-09 变量缓存和错误提示更新

- 新增 `Services\PresetVariableUsageStore.cs`。
- 新增 `Views\PresetVariableInputDialog.xaml(.cs)`。
- 托盘和服务管理窗口的变量菜单现在按最近使用排序，并在末尾包含 `新增`。
- GUI 输入的新变量会保存到选中的服务，并立即用于当前操作。
- 已有变量选择和 CLI `--variable` 会记录到 `variable-usage-cache.json`。
- 新增启动/步骤最终失败的托盘冒泡提示。
- 修复日志窗口禁用按钮文字对比度。

## 2026-06-09 步骤变量开关和管理窗口状态更新

- 新增 `ScriptStep.UseVariable`，默认 `true` 以兼容旧配置。
- `UseVariable=false` 的步骤不会收到 `SERVICEPILOT_VARIABLE`，不会替换 `{{variable}}`，执行步骤时也不会显示变量子菜单。
- 服务和模板步骤编辑器新增 `使用变量` 勾选框。
- 单独执行步骤时，原本停止的服务会在步骤运行期间显示为 `Running`。
- 服务管理窗口的启动/停止/重启按钮会按选中服务的实时状态刷新。
- CLI 步骤规格支持 `Name|Type|UseVariable|command`，仍兼容 `Name|Type|command`。

## 2026-06-09 启动步骤和日志窗口更新

- 移除托盘 UI 和 CLI 的批量启动全部服务入口；`stop all` 保留。
- 新增 `ScriptStep.RunOnStart`，默认 `true` 以兼容旧配置。
- 服务和模板步骤编辑器新增 `启动执行`；普通启动会跳过未勾选步骤。
- 托盘、服务管理窗口、日志窗口的执行步骤菜单分为 `启动执行` 和 `不启动执行`。
- 服务编辑窗口可以把当前服务草稿另存为完整服务模板。
- 日志窗口新增带变量选择的启动、执行步骤、重启操作。
- 日志窗口新增编辑按钮，会以日志窗口为 Owner 打开现有服务编辑弹窗。
- 日志窗口的启动/停止/重启/执行步骤按钮现在按实时服务和步骤状态刷新，单独执行步骤时启动按钮会变暗。
- 服务管理工具栏顺序改为启动、执行步骤、停止、重启。
- CLI 步骤规格新增支持 `Name|Type|UseVariable|RunOnStart|command`；`step list --json` 会输出 `RunOnStart`。
- 已完成验证：
  - `rtk dotnet build ServicePilot.sln`
  - 隔离 CLI 新增/列出 `RunOnStart=false` 和 `RunOnStart=true` 步骤。
  - 隔离托盘测试确认 `start all` 返回退出码 `2`，普通启动跳过未勾选步骤，手动执行步骤仍可运行。
  - 已发布 `dist\ServicePilot.exe` 并验证 help 和 JSON 步骤输出。

## 2026-06-09 stderr 分类更新

- 修复 webpack 正常进度输出写到 stderr 时被显示成红色 `[Error]` 的问题。
- `ProcessRunner` 现在按内容分类 stderr，不再把整条 stderr 流都当作 `Error`。
- 已知正常的 webpack/vite 进度输出会显示为 `Info`；warning 类输出显示为 `Warning`；强失败词仍显示为 `Error`。

## 2026-06-09 日志窗口性能更新

- 修复打开长日志时反复自动滚动导致 UI 卡住的问题。
- 新增 `LogWindow.LoadLogs()`，窗口显示后批量加载缓存历史。
- 实时日志自动滚动改为短定时器节流，日志突增时每个 tick 最多滚动一次。

## 2026-06-09 紧凑状态小点更新

- 托盘服务菜单和执行步骤菜单用紧凑状态小点替代大段状态文字前缀。
- 已停止/未执行/已完成不显示小点；运行/启动显示绿色；失败/错误显示红色；停止中/已取消显示橙色。
- 步骤状态文字保留在 tooltip 中，不再占用菜单标签。

## 2026-06-09 步骤变量和日志搜索更新

- 新增 `ScriptStep.StepVariables`，用于 `RunOnStart=false` 的手动执行步骤。
- 启动执行步骤继续使用服务 `PresetVariables`；不启动执行步骤使用自己的 `StepVariables`，最近使用排序以步骤 id 作为缓存 key。
- `UseVariable=true` 的不启动执行步骤即使还没有变量，也会显示变量子菜单和 `新增`。
- 执行步骤菜单中，`启动执行` 分组从 `1` 开始编号；`不启动执行` 分组不显示编号。
- 服务和模板编辑器左侧变量框会根据当前步骤切换：启动步骤显示 `预设变量`，不启动步骤显示 `手动执行变量`。
- App 日志缓冲和日志窗口都限制为 20,000 行。
- 日志窗口新增搜索、上一个/下一个匹配、Ctrl+C/右键复制、复制全部，以及长行横向滚动。
- CLI `step list --json` 和 `status --json` 会输出 `StepVariables` 和显示用元数据。数字步骤选择现在用 `1..N` 对应启动步骤显示编号，`0` 保留给旧内部顺序兼容。

## 2026-06-09 AI CLI 和文档定位更新

- 新增 `ServicePilot.exe ai-help`，输出给 AI/脚本使用的安全操作指南。
- 新增 CLI 步骤变量维护命令：
  - `step variables SERVICE STEP [--json]`
  - `step variable-add SERVICE STEP --variable VALUE`
  - `step variable-remove SERVICE STEP --variable VALUE`
  - `step variable-clear SERVICE STEP`
- 新增 CLI 模板步骤变量维护命令：
  - `template step-variables TEMPLATE STEP [--json]`
  - `template step-variable-add TEMPLATE STEP --variable VALUE`
  - `template step-variable-remove TEMPLATE STEP --variable VALUE`
  - `template step-variable-clear TEMPLATE STEP`
- `step run` 记录变量使用缓存时，启动执行步骤使用服务 id，不启动执行步骤使用步骤 id。
- 修复步骤变量 CLI 命令的 async 死锁，避免命令模式残留 `ServicePilot.exe` 并锁定构建输出。
- README 已改为 AI 友好的中文主页定位，并保留英文配套。
- CHANGELOG 已改为初始公开版本口径，不再展示上线前内部修复流水账。
- 已完成隔离 CLI 验证：新增服务、添加两个步骤变量、JSON 查询、删除一个步骤变量、清空步骤变量；新增模板、添加两个模板步骤变量、JSON 查询、删除一个模板步骤变量、清空模板步骤变量。

## 2026-06-09 doctor 配置体检更新

- 新增 `ServicePilot.exe doctor [--json]`。
- 体检项包括：服务/模板重名、服务工作目录缺失、空步骤、无启动步骤、步骤顺序重复、步骤名重复、预设变量重复、步骤变量重复。
- 发现 Error 时返回退出码 `2`；只有 Warning 时返回 `0`。
- JSON 输出改为保留中文直出，方便人和 AI 直接阅读。
- `SERVICEPILOT_CONFIG_DIR` 现在默认禁用托盘管道，防止隔离测试误操作真实配置。
- 已完成隔离 CLI 验证：空配置通过、正常服务通过、坏配置返回 `SERVICE_DIR_MISSING` 和 `STEP_CONTENT_EMPTY`。

## 2026-06-09 GitHub 上线资料更新

- 修正 `.github` Issue/PR 模板的中英文互链，默认中文模板链接到 `-en.md` 英文模板，英文模板链接回中文默认模板。
- Issue 模板补充托盘菜单、管理服务窗口、日志窗口、CLI/AI 命令、端口清理、配置/模板/变量等影响范围。
- PR 模板删除过时的“悬浮窗行为”检查，改为托盘、日志窗口、CLI/AI 命令和安全边界检查。
- `.github/workflows/build.yml` 增加最小权限、发布后 `ai-help` / `doctor --json` 冒烟测试，以及 artifact 缺失时报错。
- Dependabot 增加 NuGet 依赖检查，`.gitignore` 增加 `dist-staged/`、`TestResults/`、包文件等本地输出。
- `docs/github-launch-checklist.md` / `docs/github-launch-checklist-en.md` 已同步更新 GitHub Actions、模板、Dependabot 和 `.gitignore` 发布前检查。
- 根目录公开文档的英文互链文字已统一为对应的 `-en.md` 文件名，包括 CONTRIBUTING、PRIVACY、SECURITY、MAINTAINERS、THIRD-PARTY-NOTICES 和发布模板。
- `docs/release-checklist.md` / `docs/release-checklist-en.md` 修正 CHANGELOG 英文文件名，并补充退出 `dist\ServicePilot.exe`、临时配置 CLI 冒烟测试、`shutdown` 和 Actions 制品检查。
- `docs/first-push.md` / `docs/first-push-en.md` 补充 Actions 冒烟测试、Issue/PR 模板和 Dependabot 可见性检查。
- `docs/release-notes-v1.0.0.md` / `docs/release-notes-v1.0.0-en.md` 已按当前功能口径更新：托盘数字、启动/手动步骤、变量、完整模板、日志搜索、AI CLI 和 Job Object 清理。
- 发布说明模板补充临时配置目录下的 `ai-help` / `doctor --json` 检查项。
- 已验证：`rtk dotnet build ServicePilot.sln`、Markdown 相对链接检查、公开文档内部修复口吻扫描、临时 `SERVICEPILOT_CONFIG_DIR` 下的 `ai-help` 和 `doctor --json`。

## 2026-06-09 语言切换和截图指南更新

- 新增 `Services\LocalizationService.cs`，支持 `auto`、`zh-CN`、`en-US`。
- `AppConfig.Settings.Language` 持久化界面语言；旧配置缺失该字段时默认 `auto`。
- `auto` 会按 Windows UI 语言判断：中文系统使用中文，其余默认 English。
- 托盘右键菜单新增 `语言` / `Language` 子菜单，可在跟随系统、中文、English 之间切换，切换后保存配置并立即重建菜单。
- 托盘菜单、状态/步骤提示、语言菜单、新增变量弹窗、管理服务窗口、管理模板窗口、日志窗口、服务编辑窗口、模板编辑窗口和模板选择框已接入本地化服务。
- 用户数据不翻译：服务名、模板名、步骤名、变量、脚本内容和日志原文保持原样。
- 新增 `docs/screenshot-guide.md` / `docs/screenshot-guide-en.md`，列出发布前必须截图的托盘菜单、管理服务、服务编辑、日志、模板管理和 CLI/AI 场景。
- README、CHANGELOG、发布说明和发布检查清单已同步提到中英文界面切换和截图指南。
- 已验证：`rtk dotnet build ServicePilot.sln`，发布到 `dist-staged`，临时 `SERVICEPILOT_CONFIG_DIR` 下运行 `dist-staged\ServicePilot.exe ai-help` 退出码 `0`，`doctor --json` 返回空配置通过。
- 当前检测到 `dist\ServicePilot.exe` 仍在运行，未覆盖正式 `dist`，避免锁文件或影响正在运行的服务。

## 2026-06-10 日志窗口布局和步骤状态收口更新

- 日志窗口尺寸从 `900x520` 调整为 `1040x560`。
- 日志窗口标题 `TitleText` 增加 `TextTrimming=CharacterEllipsis`、`NoWrap` 和 `MaxWidth=260`，长服务名会被裁剪，不能再把启动/停止/执行步骤按钮挤走。
- `ProcessManager` 的单独执行步骤完成后收口逻辑从 `CompleteStandaloneStepServiceState` 改为 `CompleteIdleServiceState`。
- 主服务执行器和单独步骤执行器结束后都会调用 `CompleteIdleServiceState`；当没有主执行器、没有步骤执行器、没有 Running 步骤时，按当前运行开始时间之后的步骤结果收敛服务状态。
- `CompleteIdleServiceState` 使用 `ServiceRuntimeState.StartTime` 过滤旧步骤状态，避免历史失败步骤导致后续成功执行仍显示 `StartFailed` 或 `Running`。
- 已重新发布到 `dist`，并验证：
  - `rtk dotnet build ServicePilot.sln`
  - `rtk dotnet publish ServicePilot/ServicePilot.csproj -c Release -r win-x64 --self-contained false -o dist`
  - 临时 `SERVICEPILOT_CONFIG_DIR` 下 `dist\ServicePilot.exe ai-help` 退出码 `0`
  - 临时 `SERVICEPILOT_CONFIG_DIR` 下 `dist\ServicePilot.exe doctor --json` 返回空配置通过

## 2026-06-10 截图整理和 GitHub 资料更新

- 已将 Snipaste 截图整理到 `Assets/screenshots/`：
  - `tray-menu-zh.png`
  - `service-manager-zh.png`
  - `service-editor-zh.png`
  - `log-window-zh.png`
  - `ai-help-cli-zh.png`
  - `status-doctor-cli-zh.png`
- 已复制 `Assets/screenshots/service-manager-zh.png` 为 `Assets/app-preview.png`，作为 README 主图和 GitHub 首屏预览。
- `README.md` / `README-en.md` 已从“截图待补充”改为真实主图和截图链接。
- `docs/screenshot-guide.md` / `docs/screenshot-guide-en.md` 已追加当前截图文件清单。
- `docs/repository-profile.md` / `docs/repository-profile-en.md` 已更新为中英文混合 GitHub description、homepage、topics 和搜索关键词。
- `docs/first-push.md` / `docs/first-push-en.md` 已同步 GitHub 仓库描述。

## 2026-06-10 最近使用服务排序更新

- `PresetVariableUsageStore` 现在除了变量使用顺序，也在同一个 `variable-usage-cache.json` 里记录最近使用服务。
- 托盘右键服务列表按最近使用时间倒序显示，上一次操作的服务会排到最顶部；没有使用记录的服务继续按 `SortOrder` 和名称排序。
- 管理服务窗口不再直接绑定原始服务集合，而是绑定 `PresetVariableUsageStore.SortServices` 生成的排序快照，刷新时尽量保留当前选中服务。
- 启动、停止、重启、执行步骤、查看日志、编辑、删除、存为模板，以及通过运行中托盘实例执行的 CLI 启动/停止/重启/执行步骤/读取日志，都会刷新服务最近使用记录。
- 这个排序只影响显示层和缓存，不会修改 `ServiceConfig.SortOrder`，也不会重排 `config.json` 里的服务定义。
- 已验证：`rtk dotnet build ServicePilot.sln`。

## 2026-06-10 README、配置迁移和默认模板更新

- README / README-en 已收束为首页入口：定位、下载、主图、并排截图、快速开始、核心能力、常用 CLI、配置位置和文档入口。
- 完整说明已迁移到 `docs/user-guide.md` / `docs/user-guide-en.md`，包括服务模型、变量、模板、日志窗口、完整 CLI、隔离测试、同类工具对比和 AI 推荐提示词。
- README 主图改为用户新增的管理服务截图，并整理为 `Assets/app-preview.png` 与 `Assets/screenshots/service-manager-overview-zh.png`，其他截图直接用表格展开，不再放“更多截图”链接列表。
- 首页文档入口不再展示截图指南、同类项目调研、进程运行器调研这类维护/研究资料；这些资料保留在完整用户指南的延伸阅读里。
- 新增 `AppSettings.BuiltInTemplatesSeeded`。无参数托盘启动时，如果尚未种过内置模板，会调用 `ServiceTemplateService.CreateBuiltInTemplates()` 创建一次可编辑的默认开发动作模板。
- 默认模板目前只是安全示例，用于承接后续用户指定的正式内置模板内容。以后改默认模板内容时集中改 `ServiceTemplateService.CreateBuiltInTemplates()`。
- `ConfigService` 默认仍使用 `%APPDATA%\ServicePilot`。当 Roaming 目标文件不存在时，会从 exe 同级目录或当前目录复制旧 `config.json` / `variable-usage-cache.json`，但不会删除旧文件。
- 修复 WPF 管理服务窗口和日志窗口菜单吞掉下划线的问题：用户变量和步骤标题必须用 `TextBlock` 作为 `MenuItem.Header`，不要直接把用户数据字符串赋给 Header。
- 已验证：`rtk dotnet build ServicePilot.sln`。

## 2026-06-10 本机项目服务和默认模板更新

- 已在真实 `%APPDATA%\ServicePilot\config.json` 中新增/更新两个维护服务，修改前备份为 `config.json.bak-20260610031027`，后续修正打开工具参数前又备份为 `config.json.bak-20260610031510-openers`：
  - `LinkShelf`：工作目录 `C:\git\其他\LinkShelf`，启动执行步骤为打开 `dist\LinkShelf.exe`；手动步骤包含 `dotnet build`、发布到 `dist`、`check --json`、`recommended --json` 和常用工具打开入口。
  - `ServicePilot`：工作目录 `C:\git\其他\ServicePilot`，启动执行步骤为打开 `dist\ServicePilot.exe`；手动步骤包含 `dotnet build`、发布到 `dist`、`ai-help`、`doctor --json`、`config-path` 和常用工具打开入口。
- 这两个服务不创建对应模板。
- 已把运行配置中的 `默认开发动作模板` 替换为 20 步通用开发动作模板：Git 拉取、安全/强制切换分支、安全/强制切换 Tag、npm install/build，以及资源管理器、CMD、PowerShell、Windows Terminal、Git Bash、VS Code、Cursor、Visual Studio、IntelliJ IDEA、WebStorm、Rider、Notepad++、Postman 打开入口。
- 默认模板的分支变量使用粗略版本系列：`main`、`master`、`develop`、`dev`、`release/1.0.0`、`release/2.0.0`、`feature/1.0.0`、`feature/2.0.0`、`hotfix/1.0.0`、`hotfix/2.0.0`。Tag 变量为 `v1.0.0`、`v1.1.0`、`v2.0.0`、`1.0.0`、`2.0.0`。
- `ServiceTemplateService.CreateBuiltInTemplates()` 已同步改为同一套内置默认模板内容，后续首启种子和当前真实配置一致。
- Windows Terminal 打开命令使用 `wt -d <dir>`，Git Bash 打开命令使用 `git-bash --cd=<dir>`；这两处已同步到真实配置和内置默认模板代码。
- 已验证：`rtk dotnet build ServicePilot.sln`、`rtk dotnet restore ServicePilot/ServicePilot.csproj -r win-x64`、`rtk dotnet publish ServicePilot/ServicePilot.csproj -c Release -r win-x64 --self-contained false -o dist`、真实配置 `doctor --json` 通过。

## 2026-06-10 打开工具脚本修复

- 用户反馈新加的 `LinkShelf`、`ServicePilot` 服务和新默认模板里的 `打开：...` 步骤打不开，而旧的 `web`、`h5` 等服务可以。
- 根因：新脚本直接在 PowerShell 子进程里 `Start-Process`，但 ServicePilot 会把运行步骤的进程放入 kill-on-close Job Object；步骤结束时直拉的子进程可能被一起关闭。
- 修复：真实配置里的 `LinkShelf`、`ServicePilot`、`默认开发动作模板`，以及从默认模板应用出来的 `web3默认开发动作模板`，都已改为 `.lnk + explorer.exe` 的脱离式打开方式。修改前备份为 `config.json.bak-20260610032703-detached-openers` 和 `config.json.bak-20260610033011-default-template-derived`。
- `ServiceTemplateService.CreateBuiltInTemplates()` 已同步改用 `DetachedOpenHeader()` / `Invoke-DetachedOpen` 生成首启默认模板。以后凡是打开 GUI 程序、终端或 exe，不要写普通 `Start-Process`；要用临时快捷方式交给 `explorer.exe` 打开。只打开文件夹时可用 COM `Shell.Application.Open`。
- 已实际验证：
  - 通过运行中托盘实例执行 `LinkShelf / 打开：项目目录`，日志显示 `Opened Explorer: C:\git\其他\LinkShelf`。
  - 执行 `LinkShelf / 打开：dist\LinkShelf.exe`，出现新的 `LinkShelf.exe` 进程，验证后已关闭。
  - 执行 `LinkShelf / 打开：CMD 当前目录`，出现新的 `cmd.exe` 进程，验证后已关闭。
  - 执行 `ServicePilot / 打开：dist\ServicePilot.exe`，出现新的 `ServicePilot.exe` 进程，验证后已关闭。

## 2026-06-10 模板应用和步骤弹日志更新

- `ScriptStep` 新增 `OpenLogOnRun`，服务/模板编辑器在 `启动执行` 后新增 `弹出日志` 复选框。
- 当步骤进入 `Running` 且 `OpenLogOnRun=true` 时，`App.OnProcessStepStateChanged` 会自动打开或激活该服务日志窗口。
- `ScriptDefinitionService.CloneStep()`、服务/模板编辑器克隆、CLI JSON 输出都保留 `OpenLogOnRun`。
- CLI `--step` 新增第 6 段：`Name|Type|UseVariable|RunOnStart|OpenLogOnRun|command`；`--content` 形式可用 `--open-log-on-run`。
- 应用模板时，如果目标服务已有名称，不再覆盖名称；只有名称为空时才使用模板名称。步骤和预设变量仍会按模板替换。
- README / README-en 和完整用户指南已补充：命令行能做的事通常都能包装成 ServicePilot 步骤；首次启动内置默认开发动作模板；推荐 AI 先读取 `ai-help`、`doctor --json`、`status --json` 再生成服务和模板。
- `AGENTS.md` 已同步记录新字段、运行时弹日志、模板应用名称保留和 CLI 规格。

## 后续有用检查

- 后续任何修改后重新构建。
- 只有准备发布或验证用户可运行 exe 时才发布到 `dist`。
- 发布后测试用户真实的 `screen` Vite 服务：
  - 按需结束 3000-3006 端口监听。
  - 启动 `screen`。
  - 多次停止/重启。
  - 确认 Vite 始终复用 3000，停止后 3000-3006 没有监听。
- 如果再次改命令执行逻辑，要重新测试中文 cmd 输出和 npm/Vite 日志。
