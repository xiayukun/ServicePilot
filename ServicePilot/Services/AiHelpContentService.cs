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
        var quotedExe = QuoteExePath(exePath);

        if (LocalizationService.Current.IsEnglish)
        {
            return $"""
                   You may use ServicePilot to manage my Windows local development services.

                   ServicePilot CLI executable: {quotedExe}
                   Run `{quotedExe} ai-help` to print this full guide at any time.

                   **IMPORTANT**: Configuration changes take effect immediately with no restart. Changes via the tray pipe (CLI while the tray is running) refresh the running menu and open windows instantly. Editing %APPDATA%/ServicePilot/config.v2.json directly on disk is also picked up automatically — the tray watches the file and hot-reloads it (running services are kept alive).

                   Configuration file path: %APPDATA%/ServicePilot/config.v2.json
                   JSON structure overview:
                     - Root object has a "Services" array and a "ServiceTemplates" array.
                     - Each Service has: Id (GUID), Name, WorkingDirectory, AutoStart, SortOrder, CreatedAt, PresetVariables (string[]), ScriptSteps (array).
                     - Each ScriptStep has: Id (GUID), Name, Kind ("Action" or "Composite"), Order, ScriptType ("Batch"/"PowerShell"/"Python"/"Node"), Content (script text), UseVariable (bool), OpenLogOnRun (bool), StepVariables (string[]), MemberStepIds (GUID[], only for Composite), LogMergeScript (string?, log merge script).
                     - Action steps carry executable scripts; Composite steps orchestrate multiple Actions via MemberStepIds.
                     - Composite cannot nest another Composite.

                   Use their real output as source of truth. Do not guess service names, action names, variables, templates, or paths. Before adding a service/template, inspect existing configuration. Before deleting, overwriting, or renaming anything, state the exact target name or id. When running actions, prefer step list, service get, template list/get, and logs --json to confirm state.

                   Log Merge Script:
                     Each Action may have a LogMergeScript field — a Roslyn C# script that transforms log lines at runtime for merging, collapsing, and coloring.
                     Globals (available as locals):
                       - CurrentLine (string?), PreviousLine (string?): CONTRACT: both are the FULL formatted log line
                           "HH:mm:ss [Level] message" (NOT the raw message; timestamp+level prefix included, but process text
                           such as "<s> [webpack.Progress] ..." stays inside the message part).
                       - PreviousResult (MergeResult?): the result returned for the previous line (null if none). Read
                           PreviousResult.State to carry state forward. Runtime only; NOT restored on tab rebuild.
                       - PreviousWasCollapsed (bool): was the previous line folded?
                       - InCollapseGroup (bool): is a fold group currently open (a header exists to fold into)?
                     Return MergeResult, or null to keep the original line untouched:
                       - MergedMessage (string?): on a group header (Collapse=false) this is the summary shown on the folded
                           one line; on a collapsed line (Collapse=true) it refreshes the header's live summary (e.g. "compiling 67%").
                           Raw lines are ALWAYS kept — nothing is overwritten — so a group can be expanded again.
                       - Color (string?): any WPF color — named (Gray, Yellow, OrangeRed, LimeGreen, DodgerBlue, ...) or hex (#FF8800). Invalid values are ignored.
                       - Collapse (bool): fold this line into the group started by the previous non-collapsed line. The group
                           becomes a real expandable fold: it shows one summary line with a left-side ">" toggle; clicking it (or
                           the "Summary" button, or a search hit inside it) reveals every folded raw line.
                           The FIRST line of a group must return Collapse=false (it is the header/anchor); later lines return Collapse=true.
                           Collapse only takes effect when the previous entry was also produced by this script.
                       - State (Dictionary<string, object?>?): cross-line carry state handed to the NEXT line as
                           PreviousResult.State. Runtime only, never persisted, NOT restored on rebuild; store simple values only
                           (string/int/double/bool). Use it for counters, last-progress, run detection, conditional folding.
                       - Children (List<MergeResult>?): reserved for future tree display; not rendered yet.
                     Live/hot: the script is read from the current config on EVERY line. Editing it via merge-script set (or an
                       external config edit picked up by the file watcher) takes effect on the next log line — no service restart needed.
                     Validation: merge-script set compile-checks the script and refuses to save on error (override with --skip-validate).
                       A runtime compile failure is surfaced once in the service log as an error, never silently swallowed.
                     Preview without running a service: merge-script test SERVICE STEP --file lines.txt [--json]
                       feeds each line as CurrentLine and prints hit/MergedMessage/Color/Collapse plus the final rendered view.
                     Typical use: collapse repeated webpack/vite build progress lines, fold duplicate warnings.

                   ── Full CLI reference ──
                   {BuildCliHelp()}
                   """;
        }

        return $"""
               你可以使用 ServicePilot 管理我的 Windows 本地开发服务。

               ServicePilot CLI 可执行文件：{quotedExe}
               任何时候都可运行 `{quotedExe} ai-help` 打印这份完整指南。

               **重要提示**：修改配置立即生效，无需重启。通过托盘管道修改（托盘运行时执行 CLI）会即时刷新运行中的菜单和已打开的窗口。直接编辑磁盘上的 %APPDATA%/ServicePilot/config.v2.json 也会被自动识别——托盘会监听该文件并热加载（正在运行的服务不会被中断）。

               配置文件路径：%APPDATA%/ServicePilot/config.v2.json
               JSON 结构概要：
                 - 根对象包含 "Services" 数组和 "ServiceTemplates" 数组。
                 - 每个 Service 有：Id (GUID)、Name、WorkingDirectory、AutoStart、SortOrder、CreatedAt、PresetVariables (string[])、ScriptSteps (数组)。
                 - 每个 ScriptStep 有：Id (GUID)、Name、Kind ("Action" 或 "Composite")、Order、ScriptType ("Batch"/"PowerShell"/"Python"/"Node")、Content (脚本内容)、UseVariable (bool)、OpenLogOnRun (bool)、StepVariables (string[])、MemberStepIds (GUID[]，仅 Composite 有)、LogMergeScript (string?，日志合并脚本)。
                 - Action 步骤承载可执行脚本；Composite 步骤通过 MemberStepIds 编排多个 Action。
                 - Composite 不能嵌套另一个 Composite。

               把真实输出作为依据，不要猜服务名、动作名、变量、模板或路径。新增服务/模板前先检查现有配置；删除、覆盖或重命名前说明明确目标名称或 id；需要执行动作时优先使用 step list、service get、template list/get 和 logs --json 确认状态。

               日志合并函数（Log Merge Script）：
                 每个 Action 可设置 LogMergeScript 字段，用 Roslyn C# 脚本对日志行做实时合并/折叠/着色。
                 全局变量（可直接作为局部变量使用）：
                   - CurrentLine (string?)、PreviousLine (string?)：契约：两者都是【完整格式化整行】
                       "HH:mm:ss [Level] message"（含时间戳与级别前缀，不是裸消息；进程输出的 "<s> [webpack.Progress] ..." 属于 message 部分）。
                   - PreviousResult (MergeResult?)：上一行返回的结果（无则 null）。读 PreviousResult.State 可把状态传给下一行。仅运行期，重建 tab 不恢复。
                   - PreviousWasCollapsed (bool)：上一行是否被折叠。
                   - InCollapseGroup (bool)：当前是否已有打开的折叠组（存在可折叠进的组头）。
                 返回 MergeResult，或返回 null 表示保留原文不处理：
                   - MergedMessage (string?)：作为组头行（Collapse=false）时，是折叠后单行显示的摘要；作为被折叠行（Collapse=true）时，
                       会刷新组头的实时摘要（如"编译中 67%"）。原始行【始终保留】、不会被覆盖，因此折叠组可再次展开。
                   - Color (string?)：任意 WPF 颜色，命名色（Gray/Yellow/OrangeRed/LimeGreen/DodgerBlue…）或十六进制（#FF8800）；非法值忽略。
                   - Collapse (bool)：将本行折叠进【上一条非折叠行开启的组】。该组会变成真正可展开的折叠区：只显示一行摘要，
                       左侧有 ">" 展开符号；点击它（或点"摘要"按钮、或搜索命中折叠区内）即可展开查看所有被折叠的原始行。
                       一组的【第一行必须 Collapse=false】（作为组头/锚点），后续行返回 Collapse=true。
                       仅当上一行也是本脚本产出的行时，折叠才生效。
                   - State (Dictionary<string, object?>?)：跨行状态，本行返回后作为下一行的 PreviousResult.State。仅运行期、不落盘、
                       重建不恢复；只存简单类型（string/int/double/bool）。可用于累计计数、记住上次进度、连续行检测、条件折叠。
                   - Children (List<MergeResult>?)：预留给未来树形展示，当前尚未渲染。
                 实时热更新：脚本在【每一行】都从当前配置实时读取。用 merge-script set 修改（或外部改配置被文件监视捕获）后，
                   下一行日志即生效，无需重启服务。
                 校验：merge-script set 会先编译校验，出错则拒绝保存（可加 --skip-validate 强制）。
                   运行时若编译失败，会在服务日志里以错误形式提示一次，绝不静默吞掉。
                 不跑服务即可预览：merge-script test SERVICE STEP --file lines.txt [--json]
                   逐行以 CurrentLine 喂入，输出命中/MergedMessage/Color/Collapse 及最终渲染结果。
                 典型用例：webpack/vite 编译进度行（重复行合并为单条实时进度）、大量重复告警折叠。

               ── 完整 CLI 命令参考 ──
               {BuildCliHelp()}
               """;
    }

    public static string BuildCliHelp()
    {
        return """
               ServicePilot AI 操作指南

               ServicePilot 3.0.0 是一个 Windows 托盘优先的本地服务和动作运行器。用户也可以在托盘右键菜单选择"复制给 AI 的帮助"，复制带有当前 ServicePilot.exe 绝对路径的提示词。
               2.x 模型:
                 - Action: 一个可执行脚本命令。
                 - Composite: 按顺序编排多个 Action；Composite 不能嵌套 Composite。

               术语说明：step = Action = 动作，指同一个概念。CLI 命令用 step，模型层叫 Action，中文界面叫动作。

               配置文件路径：%APPDATA%/ServicePilot/config.v2.json
               JSON 结构概要：
                 - 根对象包含 "Services" 数组和 "ServiceTemplates" 数组。
                 - 每个 Service 有：Id (GUID)、Name、WorkingDirectory、AutoStart、SortOrder、CreatedAt、PresetVariables (string[])、ScriptSteps (数组)。
                 - 每个 ScriptStep 有：Id (GUID)、Name、Kind ("Action" 或 "Composite")、Order、ScriptType ("Batch"/"PowerShell"/"Python"/"Node")、Content (脚本内容)、UseVariable (bool)、OpenLogOnRun (bool)、StepVariables (string[])、MemberStepIds (GUID[]，仅 Composite 有)、LogMergeScript (string?，日志合并脚本)。
                 - Action 步骤承载可执行脚本；Composite 步骤通过 MemberStepIds 编排多个 Action。
                 - Composite 不能嵌套另一个 Composite。

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
                 - 修改配置立即生效，无需重启托盘实例。通过托盘管道修改（托盘运行时执行 CLI）会即时刷新托盘菜单和已打开的管理/日志窗口。直接编辑磁盘上的 config.v2.json 也会被托盘自动监听并热加载，正在运行的服务不会被中断。
                 - 变量现在属于 Action 的 StepVariables；服务级预设变量只是 v1 迁移遗留。
                 - 维护 Action 变量:
                    ServicePilot.exe step variables "SERVICE" "STEP" --json
                    ServicePilot.exe step variable-add "SERVICE" "STEP" --variable "VALUE"
                    ServicePilot.exe step variable-remove "SERVICE" "STEP" --variable "VALUE"
                    ServicePilot.exe step variable-clear "SERVICE" "STEP"
                 - 维护 Action 本身（增、改、删、排序）:
                    ServicePilot.exe step add "SERVICE" --name "Check Node" --type Batch --script "node --version" --position after:"STEP"
                    合法 --type 值：Batch、PowerShell、Python、Node。
                    --use-variable 默认为 false。
                    step add 同名动作会被拒绝，不允许重名。
                    ServicePilot.exe step add "SERVICE" --name "Build" --type Batch --script "npm run build" --into-composite "Start"
                    ServicePilot.exe step edit "SERVICE" "STEP" --name "New Name" --script "npm run dev"
                    ServicePilot.exe step remove "SERVICE" "STEP"
                    ServicePilot.exe step move "SERVICE" "STEP" --position end
                 - 日志合并脚本（Log Merge Script）:
                   合并脚本用 Roslyn C# 对日志行做实时合并、折叠、着色。全局变量：CurrentLine/PreviousLine（均为【完整整行】"HH:mm:ss [Level] message"）、
                   PreviousResult (MergeResult?)、PreviousWasCollapsed (bool)、InCollapseGroup (bool)；返回 MergeResult（MergedMessage?, Color?, Collapse, State?, Children?）或 null。
                   Collapse=true 折叠进上一组，组内第一行须 Collapse=false；State 传状态给下一行（仅运行期、只存简单类型）。
                    脚本每行实时读取，改后下一行即生效，无需重启。set 会编译校验，出错拒绝保存（--skip-validate 强制）。
                    ServicePilot.exe merge-script list [--json]
                    ServicePilot.exe merge-script list "SERVICE" [--json]
                    ServicePilot.exe merge-script get "SERVICE" "STEP" [--json]
                    ServicePilot.exe merge-script set "SERVICE" "STEP" --inline "return new MergeResult { Collapse = true };"
                    ServicePilot.exe merge-script set "SERVICE" "STEP" --file .\merge.csx
                    ServicePilot.exe merge-script test "SERVICE" "STEP" --file .\lines.txt [--json]
                    ServicePilot.exe merge-script remove "SERVICE" "STEP"
                 - 维护 Composite（增、添成员、改成员、删成员）:
                    ServicePilot.exe step add-composite "SERVICE" --name "启动" --member "Action1" --member "Action2" --position first
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
                 - doctor 成功产出报告即 exit 0，体检结果通过 JSON 的 Counts.Errors/Warnings 表达。
                 - 用名称定位 edit/remove 时，如果同名有多个，会报错要求用 GUID。
                 - 删除服务或模板时必须指定明确名称或 id。
                 - ServicePilot 没有 start all。可以 stop all，但批量启动必须由调用方逐个服务显式启动。
                 - 自动化测试请先设置 SERVICEPILOT_CONFIG_DIR，避免写入用户真实配置。
                 - 设置 SERVICEPILOT_CONFIG_DIR 后，CLI 默认不会连接正在运行的全局托盘实例；只有明确需要控制托盘管道时才设置 SERVICEPILOT_ALLOW_TRAY_PIPE=1。
                 - 直接改磁盘上的 config.v2.json 的官方推荐姿势：写入合法 JSON 即可，托盘会自动热加载，无需重启，也不需要任何 reload 命令；正在运行的服务会被保留。若要用外部文件整体替换配置，可用 config apply --file PATH（会自动备份旧配置到 config-cache/ 并热加载）。
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
                 ServicePilot.exe step add-composite "MyService" --name "启动" --member "Set API" --member "Start Server" --position first
                 ServicePilot.exe step set-members "MyService" "Start" --member "Set API" --member "Start Server"
                 ServicePilot.exe step add-member "MyService" "Start" --member "HealthCheck"
                 ServicePilot.exe step remove-member "MyService" "Start" --member "HealthCheck"
                 ServicePilot.exe template step add "MyTemplate" --name "LogStart" --type Batch --script "echo started" --position end
                 ServicePilot.exe template step edit "MyTemplate" "LogStart" --name "Init"
                 ServicePilot.exe template step remove "MyTemplate" "Init"
                 ServicePilot.exe template step move "MyTemplate" "Init" --position after:Start
                 ServicePilot.exe template step add-composite "MyTemplate" --name "启动" --member "Init" --member "Build" --position first
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
