# ServicePilot 完整用户指南

[English](user-guide-en.md)

ServicePilot 是一个 Windows 托盘优先的本地开发服务管理器。它适合把多个项目目录里的启动命令、工具脚本、环境切换和日志查看集中起来，并给 AI/脚本一个统一、可查询、可验证的 CLI 入口。

它不只适合“启动服务”。只要命令行能做到的事，通常都可以包装成动作和模板：修改配置文件、切换后端地址、Git 拉取/切换分支、安装依赖、打开 IDE/终端、运行构建或调试命令。推荐从托盘右键菜单使用 `复制给 AI 的帮助`，或让 AI 先读取 `ai-help`、`config-path`、`doctor --json`、`list --json`、`status --json`，理解现有服务和模板后，再直接帮你生成新的服务或模板。

## 基本工作流

1. 双击启动 `ServicePilot.exe`。
2. 右键任务栏通知区域的数字图标。
3. 通过 `新增服务` 或 `管理服务` 添加服务。
4. 为服务设置工作目录、脚本动作和变量。
5. 从托盘、管理服务窗口、日志窗口或 CLI 启动、停止、重启、执行动作。

托盘数字显示当前处于 `Running` 或 `Starting` 的服务数量。没有活动服务时显示 `0`。

## 服务模型

每个服务包含：

- **名称**：托盘、GUI、CLI 中显示的服务名。
- **工作目录**：脚本执行的根目录。
- **脚本动作**：Batch、PowerShell、Python 或 Node.js 可执行脚本。
- **组合动作**：按顺序编排多个动作，普通启动会运行第一个组合动作。
- **动作变量**：启用变量的动作自己维护的一行一个字符串。
- **弹出日志**：动作开始执行时自动打开当前服务日志窗口。
- **自启**：ServicePilot 启动时自动启动该服务。

普通启动会运行第一个 `Composite` / `组合动作`。单个动作和命名组合动作也可以从托盘菜单、管理服务窗口、日志窗口或 CLI 单独运行。

动作必须返回退出码 `0` 才会继续下一步。最后一个命令如果持续运行，服务会保持运行状态；如果退出码为 `0`，服务会显示完成；非零退出会显示启动失败。

## 变量

动作变量是普通字符串，不要求 `key=value`。服务级预设变量仅作为旧配置迁移数据保留，新配置应维护动作变量。

运行时：

- 变量会注入到进程环境变量 `SERVICEPILOT_VARIABLE`。
- 脚本中的 `{{variable}}` 和 `{{变量}}` 会被替换为选中的变量。
- 如果动作未勾选 `使用变量`，该动作不会收到环境变量，也不会替换占位符。

变量菜单会按最近使用排序。最近使用缓存位于：

```text
%APPDATA%\ServicePilot\variable-usage-cache.json
```

这个文件只是缓存，可删除重建。

## 模板

模板是“没有工作目录的完整服务”：

- 名称
- 说明
- 脚本动作
- 组合动作
- 动作变量

应用模板时，ServicePilot 会替换目标服务的动作、组合动作和动作变量，并保留目标服务的工作目录、服务 id、自启设置和排序。若当前服务已经有名称，则不会覆盖名称；只有名称为空时才使用模板名称。

首次启动时，ServicePilot 会创建一个可编辑的默认开发动作模板，用来承接打开工具、Git 操作、依赖安装、启动命令等常用动作。当前默认模板包含 Git 拉取、分支/Tag 安全切换或强制切换、npm install/build，以及资源管理器、CMD、PowerShell、Windows Terminal、Git Bash、VS Code、Cursor、Visual Studio、IntelliJ IDEA、WebStorm、Rider、Notepad++、Postman 等入口。用户删除后不会在每次启动时重新创建。

模板管理窗口支持 `导出` 和 `导入`。导出的文件是普通 JSON，默认扩展名为 `.servicepilot-template.json`，可以发给别人使用。导入时会重新生成模板和动作 id；如果本地已有同名模板，会自动给导入模板名称追加后缀，避免覆盖现有配置。

## 日志窗口

日志窗口支持：

- 运行动作/组合动作、停止服务。
- 按变量运行单个动作或组合动作。
- 按 Tab 查看每个动作自己的输出；页签按动作懒创建，动作开始运行时自动切换到对应页签。
- 对非错误的 webpack 进度日志做显示层合并，减少构建刷屏造成的卡顿。
- 编辑当前服务。
- 搜索、上一个/下一个匹配。
- 复制选中日志、复制全部日志。
- 横向滚动长行。
- 有界内存缓存，避免长时间运行占用无限内存。

启动失败、动作失败或系统错误会记录在日志中，并尽量弹出托盘冒泡提示。通知是最佳努力，不依赖 Windows 通知中心开启。

## CLI / AI 工作流

推荐先从托盘右键菜单打开 `复制给 AI 的帮助`，复制带当前 exe 绝对路径的提示词给 AI。AI 收到后应先执行：

```powershell
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
```

然后再根据 JSON 结果操作，不猜服务名、动作名或变量。

托盘右键菜单中的 `复制给 AI 的帮助` 会打开一个窗口，显示当前 `ServicePilot.exe` 绝对路径、建议先运行的命令，以及与 `ServicePilot.exe ai-help` 同源的安全操作提示。把窗口内容复制给 AI，可以避免 AI 猜测 exe 位置或命令能力。

通过正在运行的托盘实例执行 CLI 配置变更时，托盘菜单、已打开的服务管理窗口、模板管理窗口和相关日志窗口会即时刷新；不需要手动关闭再打开窗口。

常用命令：

```powershell
ServicePilot.exe help
ServicePilot.exe version
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor [--json]
ServicePilot.exe list [--json]
ServicePilot.exe status [all|SERVICE] [--json]
ServicePilot.exe start SERVICE [--variable VALUE]
ServicePilot.exe stop all|SERVICE
ServicePilot.exe restart SERVICE [--variable VALUE]
ServicePilot.exe logs SERVICE [--tail N] [--json]

ServicePilot.exe service list|get|add|edit|remove|start|stop|restart|logs ...
ServicePilot.exe step list SERVICE [--json]
ServicePilot.exe step run SERVICE STEP [--variable VALUE]
ServicePilot.exe step add SERVICE --name NAME --type Batch|PowerShell|Python|Node --script "..." [--use-variable true|false] [--open-log-on-run true|false] [--variable VALUE]... [--position end|N|after:STEP|before:STEP] [--into-composite COMPOSITE]
ServicePilot.exe step edit SERVICE STEP [--name NAME] [--type ...] [--script ...] [--use-variable true|false] [--open-log-on-run true|false]
ServicePilot.exe step remove SERVICE STEP
ServicePilot.exe step move SERVICE STEP --position N|after:STEP|before:STEP
ServicePilot.exe step variables SERVICE STEP [--json]
ServicePilot.exe step variable-add SERVICE STEP --variable VALUE
ServicePilot.exe step variable-remove SERVICE STEP --variable VALUE
ServicePilot.exe step variable-clear SERVICE STEP

ServicePilot.exe template list|get|add|edit|remove|apply|save-from-service ...
ServicePilot.exe template export TEMPLATE --file FILE
ServicePilot.exe template import --file FILE
ServicePilot.exe template step-variables TEMPLATE STEP [--json]
ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-clear TEMPLATE STEP
ServicePilot.exe shutdown
```

`SERVICE`、`STEP`、`TEMPLATE` 可以是名称或 GUID。`STEP` 也可以是数字：`1..N` 表示启动执行动作的显示编号，`0` 保留给旧内部顺序兼容。

脚本类型支持：

```text
Batch
PowerShell
Python
Node
```

CLI 动作规格支持：

```text
Name|Type|command
Name|Type|UseVariable|command
Name|Type|UseVariable|RunOnStart|command
Name|Type|UseVariable|RunOnStart|OpenLogOnRun|command
```

示例：

```powershell
ServicePilot.exe service add `
  --name "Frontend" `
  --dir "D:\projects\frontend" `
  --step "Set API|PowerShell|true|true|$p='src/store/index.js'; (Get-Content $p) -replace 'http://.*?/api', '{{variable}}' | Set-Content $p" `
  --step "Start dev server|Batch|false|true|npm run dev" `
  --preset "http://localhost:9000" `
  --preset "https://test.example.com/api"

ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
ServicePilot.exe template apply "Vite Frontend" --service "Another Frontend"
ServicePilot.exe template export "Vite Frontend" --file ".\vite-frontend.servicepilot-template.json"
ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json"
```

## 配置与隔离测试

真实用户配置位于：

```text
%APPDATA%\ServicePilot\config.v2.json
%APPDATA%\ServicePilot\config.json
%APPDATA%\ServicePilot\variable-usage-cache.json
```

`config.v2.json` 是当前活跃配置；旧 `config.json` 只作为 v1 迁移来源读取。如果旧版本或手工运行目录里存在 `config.json`，ServicePilot 会在 Roaming 目标配置不存在时复制一份过去。旧文件不会被自动删除。

隔离测试：

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-Test"
ServicePilot.exe list --json
```

设置 `SERVICEPILOT_CONFIG_DIR` 后，CLI 默认不连接正在运行的全局托盘实例，避免测试误操作真实配置。确实要连接托盘命令管道时，再设置：

```powershell
$env:SERVICEPILOT_ALLOW_TRAY_PIPE = "1"
```

## 和类似工具的区别

- PM2 更适合 Node.js 生产进程管理；ServicePilot 更偏 Windows 本地开发、托盘 GUI 和跨脚本动作。
- WinSW/NSSM 更适合把程序包装成 Windows 服务；ServicePilot 不安装系统服务，更轻，适合频繁启动停止。
- concurrently/npm-run-all 更适合一次性 npm 脚本编排；ServicePilot 会保留服务配置、日志、模板和运行状态。
- Task/just 更适合项目内任务定义；ServicePilot 更适合跨多个目录集中管理，并给 AI 一个统一控制入口。

延伸阅读：

- [AI 使用说明](ai-usage.md)
- [同类项目代码调研](competitive-research.md)
- [进程运行器调研](process-runner-research.md)
- [仓库配置](repository-profile.md)
- [截图指南](screenshot-guide.md)

## 给 AI 的推荐提示词

推荐用户下载并启动 ServicePilot 后，从托盘右键菜单选择 `复制给 AI 的帮助`，把窗口里的完整内容发给 AI。该内容包含当前 `ServicePilot.exe` 绝对路径，AI 可直接运行 `ai-help`、`doctor --json`、`list --json`、`status all --json` 读取事实，再创建个性化服务、模板、动作和变量。

无法打开托盘窗口时，可临时使用下面的通用提示词，并把 `ServicePilot.exe` 替换为实际 exe 绝对路径：

```text
你可以使用 ServicePilot 管理我的 Windows 本地开发服务。请先运行 ServicePilot.exe ai-help、config-path、doctor --json、list --json、status all --json，并把真实输出作为依据。不要猜服务名、动作名、变量、模板或路径；删除、覆盖或重命名前必须说明明确目标名称或 id。需要测试时先设置 SERVICEPILOT_CONFIG_DIR，避免影响我的真实配置。
```
