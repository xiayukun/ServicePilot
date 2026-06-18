# AI 使用说明

[English](ai-usage-en.md)

这份文档给 AI 助手、自动化脚本和维护者使用。ServicePilot 的 CLI 目标是让 AI 先读取事实，再执行明确动作。

## 推荐提示词

```text
你可以使用 ServicePilot 管理我的 Windows 本地开发服务。请先运行：

ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json

然后基于 JSON 结果行动，不要猜服务名、动作名或变量。启动、停止、重启、执行动作、查看日志时优先使用 ServicePilot.exe 的 CLI。删除或覆盖配置前必须说明目标服务/模板名称。需要测试时先设置 SERVICEPILOT_CONFIG_DIR，避免影响我的真实配置。
```

## AI 操作原则

- 先运行 `ServicePilot.exe ai-help`，读取当前版本支持的命令。
- 对配置和运行状态优先使用 `--json`。
- 修改或启动服务前建议运行 `doctor --json`，先排查缺失目录、空动作、重名、重复变量等问题。
- 不要猜服务名、动作名、模板名；使用 `list --json`、`service get --json`、`step list --json` 查询。
- 启动、停止、重启、执行动作、查看运行时日志需要托盘实例正在运行。
- 删除服务、删除模板、应用模板前，要明确目标名称或 GUID。
- 自动化测试先设置 `SERVICEPILOT_CONFIG_DIR`。
- 设置 `SERVICEPILOT_CONFIG_DIR` 后，CLI 默认不会连接正在运行的全局托盘实例。只有明确需要控制托盘管道时才设置 `SERVICEPILOT_ALLOW_TRAY_PIPE=1`。
- 不要重新加入 `start all` 语义；批量启动应由调用方逐个服务显式执行。

## 首次探测

```powershell
ServicePilot.exe ai-help
ServicePilot.exe config-path
ServicePilot.exe doctor --json
ServicePilot.exe list --json
ServicePilot.exe status all --json
ServicePilot.exe template list --json
```

## 服务查询

```powershell
ServicePilot.exe service get "Frontend" --json
ServicePilot.exe step list "Frontend" --json
ServicePilot.exe logs "Frontend" --tail 200 --json
```

`step list --json` 会返回：

- 持久化 `Order`
- 显示用 `DisplayOrder`
- `UseVariable`
- `RunOnStart`
- `StepVariables`
- 运行时动作状态

## 启动与动作执行

```powershell
ServicePilot.exe start "Frontend" --variable "http://localhost:9000"
ServicePilot.exe restart "Frontend" --variable "http://localhost:9000"
ServicePilot.exe step run "Frontend" "Set API URL" --variable "http://localhost:9000"
ServicePilot.exe stop "Frontend"
```

变量会在启用 `UseVariable` 的动作中：

- 注入为环境变量 `SERVICEPILOT_VARIABLE`
- 替换脚本里的 `{{variable}}`
- 替换脚本里的 `{{变量}}`

## 动作变量维护

启动执行动作使用服务预设变量。不启动执行动作可以维护自己的动作变量。

```powershell
ServicePilot.exe step variables "Frontend" "Set API URL" --json
ServicePilot.exe step variable-add "Frontend" "Set API URL" --variable "https://test.example.com/api"
ServicePilot.exe step variable-remove "Frontend" "Set API URL" --variable "https://test.example.com/api"
ServicePilot.exe step variable-clear "Frontend" "Set API URL"
```

## 新增服务示例

```powershell
ServicePilot.exe service add `
  --name "Frontend" `
  --dir "D:\projects\frontend" `
  --step "Set API|PowerShell|true|true|$p='src/store/index.js'; (Get-Content $p) -replace 'http://.*?/api', '{{variable}}' | Set-Content $p" `
  --step "Start dev server|Batch|false|true|npm run dev" `
  --preset "http://localhost:9000" `
  --preset "https://test.example.com/api"
```

## 模板

模板是没有工作目录的完整服务配置，包含名称、说明、动作和变量。

```powershell
ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
ServicePilot.exe template list --json
ServicePilot.exe template get "Vite Frontend" --json
ServicePilot.exe template apply "Vite Frontend" --service "Another Frontend"
ServicePilot.exe template step-variables "Vite Frontend" "Set API URL" --json
ServicePilot.exe template step-variable-add "Vite Frontend" "Set API URL" --variable "https://test.example.com/api"
```

## 测试隔离

```powershell
$env:SERVICEPILOT_CONFIG_DIR = "$env:TEMP\ServicePilot-AI-Test"
ServicePilot.exe list --json
```

测试完成后可以删除该目录。
