# ServicePilot 完整用户指南

[English](user-guide-en.md)

ServicePilot 是一个 Windows 托盘优先的本地开发服务管理器。它适合把多个项目目录里的启动命令、工具脚本、环境切换和日志查看集中起来，并给 AI/脚本一个统一、可查询、可验证的 CLI 入口。

## 基本工作流

1. 双击启动 `ServicePilot.exe`。
2. 右键任务栏通知区域的数字图标。
3. 通过 `新增服务` 或 `管理服务` 添加服务。
4. 为服务设置工作目录、脚本步骤和变量。
5. 从托盘、管理服务窗口、日志窗口或 CLI 启动、停止、重启、执行步骤。

托盘数字显示当前处于 `Running` 或 `Starting` 的服务数量。没有活动服务时显示 `0`。

## 服务模型

每个服务包含：

- **名称**：托盘、GUI、CLI 中显示的服务名。
- **工作目录**：脚本执行的根目录。
- **脚本步骤**：按顺序执行的 Batch、PowerShell、Python 或 Node.js 脚本。
- **预设变量**：启动、重启和启动步骤使用的一行一个字符串。
- **步骤变量**：不启动执行步骤自己的变量列表。
- **自启**：ServicePilot 启动时自动启动该服务。

普通启动只执行勾选 `启动执行` 的步骤。未勾选的步骤显示在 `不启动执行` 分组，可作为工具动作单独运行。

步骤必须返回退出码 `0` 才会继续下一步。最后一个命令如果持续运行，服务会保持运行状态；如果退出码为 `0`，服务会显示完成；非零退出会显示启动失败。

## 变量

服务预设变量和步骤变量都是普通字符串，不要求 `key=value`。

运行时：

- 变量会注入到进程环境变量 `SERVICEPILOT_VARIABLE`。
- 脚本中的 `{{variable}}` 和 `{{变量}}` 会被替换为选中的变量。
- 如果步骤未勾选 `使用变量`，该步骤不会收到环境变量，也不会替换占位符。

变量菜单会按最近使用排序。最近使用缓存位于：

```text
%APPDATA%\ServicePilot\variable-usage-cache.json
```

这个文件只是缓存，可删除重建。

## 模板

模板是“没有工作目录的完整服务”：

- 名称
- 说明
- 脚本步骤
- 预设变量

应用模板时，ServicePilot 会替换目标服务的名称、脚本步骤和预设变量，并保留目标服务的工作目录、服务 id、自启设置和排序。

首次启动时，ServicePilot 会创建一个可编辑的默认开发动作模板，用来承接打开工具、Git 操作、依赖安装、启动命令等常用动作。用户删除后不会在每次启动时重新创建。

## 日志窗口

日志窗口支持：

- 启动、执行步骤、停止、重启。
- 按变量启动或执行步骤。
- 编辑当前服务。
- 搜索、上一个/下一个匹配。
- 复制选中日志、复制全部日志。
- 横向滚动长行。
- 有界内存缓存，避免长时间运行占用无限内存。

启动失败、步骤失败或系统错误会记录在日志中，并尽量弹出托盘冒泡提示。通知是最佳努力，不依赖 Windows 通知中心开启。

## CLI / AI 工作流

推荐让 AI 先执行：

```powershell
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
```

然后再根据 JSON 结果操作，不猜服务名、步骤名或变量。

常用命令：

```powershell
ServicePilot.exe help
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
ServicePilot.exe step variables SERVICE STEP [--json]
ServicePilot.exe step variable-add SERVICE STEP --variable VALUE
ServicePilot.exe step variable-remove SERVICE STEP --variable VALUE
ServicePilot.exe step variable-clear SERVICE STEP

ServicePilot.exe template list|get|add|edit|remove|apply|save-from-service ...
ServicePilot.exe template step-variables TEMPLATE STEP [--json]
ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
ServicePilot.exe template step-variable-clear TEMPLATE STEP
ServicePilot.exe shutdown
```

`SERVICE`、`STEP`、`TEMPLATE` 可以是名称或 GUID。`STEP` 也可以是数字：`1..N` 表示启动执行步骤的显示编号，`0` 保留给旧内部顺序兼容。

脚本类型支持：

```text
Batch
PowerShell
Python
Node
```

CLI 步骤规格支持：

```text
Name|Type|command
Name|Type|UseVariable|command
Name|Type|UseVariable|RunOnStart|command
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
```

## 配置与隔离测试

真实用户配置位于：

```text
%APPDATA%\ServicePilot\config.json
%APPDATA%\ServicePilot\variable-usage-cache.json
```

如果旧版本或手工运行目录里存在 `config.json`，ServicePilot 会在 Roaming 目标配置不存在时复制一份过去。旧文件不会被自动删除。

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

- PM2 更适合 Node.js 生产进程管理；ServicePilot 更偏 Windows 本地开发、托盘 GUI 和跨脚本步骤。
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

```text
你可以使用 ServicePilot 管理我的 Windows 本地开发服务。请先运行：

ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json

然后基于 JSON 结果行动，不要猜服务名、步骤名或变量。启动、停止、重启、执行步骤、查看日志时优先使用 ServicePilot.exe 的 CLI。删除或覆盖配置前必须说明目标服务/模板名称。需要测试时先设置 SERVICEPILOT_CONFIG_DIR，避免影响我的真实配置。
```
