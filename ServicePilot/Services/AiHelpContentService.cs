using System.Diagnostics;
using System.IO;
using System.Text;

namespace ServicePilot.Services;

public static class AiHelpContentService
{
    private static readonly string[] InitialCommands =
    [
        "ai-help",
        "config-path",
        "doctor --json",
        "list --json",
        "status all --json"
    ];

    public static string GetCurrentExePath()
    {
        var candidates = new[]
        {
            Environment.ProcessPath,
            TryGetMainModuleFileName(),
            Path.Combine(AppContext.BaseDirectory, "ServicePilot.exe")
        };

        return candidates
                   .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                   ?.Trim()
               ?? "ServicePilot.exe";
    }

    public static IReadOnlyList<string> BuildCommandList(string exePath) =>
        InitialCommands.Select(command => $"{QuoteExePath(exePath)} {command}").ToList();

    public static string BuildPrompt(string exePath)
    {
        var commands = BuildCommandList(exePath);
        var commandBlock = string.Join(Environment.NewLine, commands);
        if (LocalizationService.Current.IsEnglish)
        {
            return $"""
                   You may use ServicePilot to manage my Windows local development services.

                   **IMPORTANT**: All configuration changes via the tray pipe take effect immediately — no restart is needed. After adding/editing a service or template, the running tray menu and open windows refresh instantly.

                   Current ServicePilot.exe path:
                   {QuoteExePath(exePath)}

                   Please run these commands first and use their real output as source of truth. Do not guess service names, action names, variables, templates, or paths:
                   {commandBlock}

                   Then decide the next step from JSON output. Before adding a service/template, inspect existing configuration. Before deleting, overwriting, or renaming anything, state the exact target name or id. When running actions, prefer step list, service get, template list/get, and logs --json to confirm state.
                   """;
        }

        return $"""
               你可以使用 ServicePilot 管理我的 Windows 本地开发服务。

               **重要提示**：通过托盘管道修改配置立即生效，无需重启托盘。添加/编辑服务或模板后，正在运行的托盘菜单及已打开的管理/日志窗口都会自动刷新。

               当前 ServicePilot.exe 路径：
               {QuoteExePath(exePath)}

               请先运行下面这些命令读取事实，并把真实输出作为依据。不要猜服务名、动作名、变量、模板或路径：
               {commandBlock}

               然后基于 JSON 输出再决定下一步。新增服务/模板前先检查现有配置；删除、覆盖或重命名前说明明确目标名称或 id；需要执行动作时优先使用 step list、service get、template list/get 和 logs --json 确认状态。
               """;
    }

    public static string BuildCliHelp()
    {
        return """
               ServicePilot AI 操作指南

               ServicePilot 2.3 是一个 Windows 托盘优先的本地服务和动作运行器。用户也可以在托盘右键菜单选择"复制给 AI 的帮助"，复制带有当前 ServicePilot.exe 绝对路径的提示词。
               2.x 模型:
                 - Action: 一个可执行脚本命令。
                 - Composite: 按顺序编排多个 Action；Composite 不能嵌套 Composite。

               推荐工作流:
                 1. 先读取帮助和配置路径:
                    ServicePilot.exe ai-help
                    ServicePilot.exe config-path
                    ServicePilot.exe doctor --json
                 2. 用 JSON 探测当前事实:
                    ServicePilot.exe list --json
                    ServicePilot.exe status all --json
                    ServicePilot.exe template list --json
                 3. 对某个服务继续探测:
                    ServicePilot.exe service get "SERVICE" --json
                    ServicePilot.exe step list "SERVICE" --json
                    ServicePilot.exe logs "SERVICE" --tail 200 --json
                 4. 再执行明确动作:
                    ServicePilot.exe start "SERVICE" --variable "VALUE"
                    ServicePilot.exe step run "SERVICE" "ACTION_OR_COMPOSITE" --variable "VALUE"
                    ServicePilot.exe stop "SERVICE"
                    ServicePilot.exe restart "SERVICE" --variable "VALUE"

               重要规则:
                 - 启动、停止、重启、执行动作、读取运行时日志和 shutdown 需要托盘实例正在运行。没有参数启动 ServicePilot.exe 可启动托盘实例。
                 - start SERVICE 会运行该服务的第一个 Composite。service get 和 status 输出中标注了 (default start) 或 DefaultStartStep 字段。
                 - step run 可以运行单个 Action，也可以运行一个 Composite。
                 - SERVICE、STEP、TEMPLATE 可以使用名称或 GUID；自动化优先使用名称或 GUID。
                 - 通过托盘管道修改配置立即生效，无需重启托盘实例。添加/编辑服务或模板后，正在运行的托盘菜单及已打开的管理/日志窗口都会自动刷新。
                 - 变量现在属于 Action 的 StepVariables；服务级预设变量只是 v1 迁移遗留。
                 - 维护 Action 变量:
                    ServicePilot.exe step variables "SERVICE" "STEP" --json
                    ServicePilot.exe step variable-add "SERVICE" "STEP" --variable "VALUE"
                    ServicePilot.exe step variable-remove "SERVICE" "STEP" --variable "VALUE"
                    ServicePilot.exe step variable-clear "SERVICE" "STEP"
                 - 维护 Action 本身（增、改、删、排序）:
                    ServicePilot.exe step add "SERVICE" --name "Check Node" --type Batch --script "node --version" --position after:"STEP"
                    ServicePilot.exe step add "SERVICE" --name "Build" --type Batch --script "npm run build" --into-composite "Start"
                    ServicePilot.exe step edit "SERVICE" "STEP" --name "New Name" --script "npm run dev"
                    ServicePilot.exe step remove "SERVICE" "STEP"
                    ServicePilot.exe step move "SERVICE" "STEP" --position end
                 - 维护 Composite 成员（增、改、删）:
                    ServicePilot.exe step set-members "SERVICE" "COMPOSITE" --member "STEP1" --member "STEP2"
                    ServicePilot.exe step add-member "SERVICE" "COMPOSITE" --member "STEP"
                    ServicePilot.exe step remove-member "SERVICE" "COMPOSITE" --member "STEP"
                 - 模板动作变量也可由 CLI 维护:
                    ServicePilot.exe template step-variables "TEMPLATE" "STEP" --json
                    ServicePilot.exe template step-variable-add "TEMPLATE" "STEP" --variable "VALUE"
                    ServicePilot.exe template step-variable-remove "TEMPLATE" "STEP" --variable "VALUE"
                    ServicePilot.exe template step-variable-clear "TEMPLATE" "STEP"
                 - 模板 Composite 成员也可由 CLI 维护:
                    ServicePilot.exe template step set-members "TEMPLATE" "COMPOSITE" --member "STEP1" --member "STEP2"
                    ServicePilot.exe template step add-member "TEMPLATE" "COMPOSITE" --member "STEP"
                    ServicePilot.exe template step remove-member "TEMPLATE" "COMPOSITE" --member "STEP"
                 - --variable 会注入环境变量 SERVICEPILOT_VARIABLE，并替换脚本里的 {{variable}} / {{变量}}，前提是目标 Action 启用了 UseVariable。
                 - 不要猜配置。修改前先用 --json 查看当前服务、动作、变量和模板。
                 - 修改配置前可以运行 doctor --json，先处理缺失目录、空动作、组合成员缺失、组合嵌套、重名等问题。
                 - 删除服务或模板时必须指定明确名称或 id。
                 - ServicePilot 没有 start all。可以 stop all，但批量启动必须由调用方逐个服务显式启动。
                 - 自动化测试请先设置 SERVICEPILOT_CONFIG_DIR，避免写入用户真实配置。
                 - 设置 SERVICEPILOT_CONFIG_DIR 后，CLI 默认不会连接正在运行的全局托盘实例；只有明确需要控制托盘管道时才设置 SERVICEPILOT_ALLOW_TRAY_PIPE=1。
                 - 通过托盘管道成功修改配置后，运行中的托盘菜单和已打开的管理/日志窗口会即时刷新，**不需要重启托盘实例**。
                 - 活跃配置是 config.v2.json；旧 config.json 只作为迁移来源保留。
                 - --json 输出使用 UTF-8 编码，即使命令有错误也走 stdout（exit code 保持语义），管道 | python / | jq 不会因中文乱码。
                 - edit 命令（service edit / template edit / step edit / template step edit）如果没有检测到实质变更，会返回"未检测到变更"而非"已更新"。

               常用新增/编辑:
                 ServicePilot.exe service add --name "Frontend" --dir "D:\app" --step "Set API|PowerShell|true|..." --step "Start|Batch|false|npm run dev"
                 ServicePilot.exe step variable-add "Frontend" "Set API" --variable "http://localhost:9000"
                 ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
                 ServicePilot.exe template apply "Vite Frontend" --service "Frontend"
                 ServicePilot.exe template export "Vite Frontend" --file ".\vite-frontend.servicepilot-template.json"
                 ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json"
                 ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json" --on-conflict overwrite
                 ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json" --on-conflict skip
                 ServicePilot.exe step add "MyService" --name "HealthCheck" --type PowerShell --script "curl localhost:8080/health" --position after:Start
                 ServicePilot.exe step edit "MyService" "HealthCheck" --use-variable false --open-log-on-run true
                 ServicePilot.exe step remove "MyService" "HealthCheck"
                 ServicePilot.exe step move "MyService" "HealthCheck" --position 1
                 ServicePilot.exe step set-members "MyService" "Start" --member "Set API" --member "Start Server"
                 ServicePilot.exe step add-member "MyService" "Start" --member "HealthCheck"
                 ServicePilot.exe step remove-member "MyService" "Start" --member "HealthCheck"
                 ServicePilot.exe template step add "MyTemplate" --name "LogStart" --type Batch --script "echo started" --position end
                 ServicePilot.exe template step edit "MyTemplate" "LogStart" --name "Init"
                 ServicePilot.exe template step remove "MyTemplate" "Init"
                 ServicePilot.exe template step move "MyTemplate" "Init" --position after:Start
                 ServicePilot.exe template step set-members "MyTemplate" "Start" --member "Init" --member "Build"
                 ServicePilot.exe template step add-member "MyTemplate" "Start" --member "LogStart"
                 ServicePilot.exe template step remove-member "MyTemplate" "Start" --member "LogStart"

               机器可读优先:
                 list --json
                 status all --json
                 doctor --json
                 service get SERVICE --json
                 step list SERVICE --json
                 logs SERVICE --tail N --json
                 template list --json
                 template get TEMPLATE --json
               """;
    }

    public static string QuoteExePath(string path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "ServicePilot.exe" : path.Trim();
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string? TryGetMainModuleFileName()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
