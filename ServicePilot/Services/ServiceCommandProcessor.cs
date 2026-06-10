using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServicePilot.Models;
using ServicePilot.ViewModels;

namespace ServicePilot.Services;

public class ServiceCommandProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConfigService _configService;
    private readonly AppConfig _appConfig;
    private readonly ProcessManager? _processManager;
    private readonly MainViewModel? _mainViewModel;
    private readonly Func<Guid, IReadOnlyList<LogEntry>>? _logProvider;
    private readonly Func<Task>? _shutdownRequested;
    private readonly PresetVariableUsageStore? _variableUsageStore;

    private sealed record DiagnosticIssue(string Severity, string Code, string Target, string Message);

    public ServiceCommandProcessor(
        ConfigService configService,
        AppConfig appConfig,
        ProcessManager? processManager = null,
        MainViewModel? mainViewModel = null,
        Func<Guid, IReadOnlyList<LogEntry>>? logProvider = null,
        Func<Task>? shutdownRequested = null,
        PresetVariableUsageStore? variableUsageStore = null)
    {
        _configService = configService;
        _appConfig = appConfig;
        _processManager = processManager;
        _mainViewModel = mainViewModel;
        _logProvider = logProvider;
        _shutdownRequested = shutdownRequested;
        _variableUsageStore = variableUsageStore;
    }

    public async Task<CommandResponse> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
            return Help();

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return command switch
        {
            "help" or "-h" or "--help" => Help(),
            "version" or "--version" or "-v" => Version(),
            "ai-help" or "agent-help" => AiHelp(),
            "config-path" => CommandResponse.Ok(_configService.PathToConfig),
            "doctor" or "check" => Doctor(rest),
            "list" or "ls" => List(rest),
            "status" or "state" => Status(rest),
            "start" => Start(rest),
            "stop" => await StopAsync(rest),
            "restart" => await RestartAsync(rest),
            "logs" or "log" => Logs(rest),
            "service" => await ServiceAsync(rest),
            "step" => await StepAsync(rest),
            "add" => await AddAsync(rest),
            "remove" or "delete" => await RemoveAsync(rest),
            "templates" => Templates(rest),
            "template" => await TemplateAsync(rest),
            "subservice" => CommandResponse.Error("子服务功能已移除。请使用预设变量和 step run。", 2),
            "shutdown" or "exit" or "quit" => Shutdown(),
            _ => CommandResponse.Error($"未知命令: {args[0]}\n\n{HelpText()}", 2)
        };
    }

    private static CommandResponse Help() => CommandResponse.Ok(HelpText());

    private static string HelpText() =>
        """
        ServicePilot command line

        Usage:
          ServicePilot.exe help
          ServicePilot.exe version
          ServicePilot.exe ai-help
          ServicePilot.exe config-path
          ServicePilot.exe doctor [--json]
          ServicePilot.exe list [--json]
          ServicePilot.exe status [all|SERVICE] [--json]
          ServicePilot.exe start SERVICE [--variable VALUE]
          ServicePilot.exe stop all|SERVICE
          ServicePilot.exe restart all|SERVICE [--variable VALUE]
          ServicePilot.exe logs SERVICE [--tail N] [--json]

          ServicePilot.exe service list|get|add|edit|remove|start|stop|restart|logs ...
          ServicePilot.exe step list SERVICE [--json]
          ServicePilot.exe step run SERVICE STEP [--variable VALUE]
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
          Legacy: add, remove, templates, template create
          ServicePilot.exe shutdown

        SERVICE, STEP, and TEMPLATE can be a name or id. STEP can also be its numeric order.
        Script types: Batch, PowerShell, Python, Node.
        --step accepts Name|Type|command, Name|Type|UseVariable|command, Name|Type|UseVariable|RunOnStart|command, or Name|Type|UseVariable|RunOnStart|OpenLogOnRun|command.
        --variable injects SERVICEPILOT_VARIABLE and replaces {{variable}} / {{变量}} in scripts.
        Start, stop, restart, step run, logs, and shutdown require the tray instance to be running.
        """;

    private static CommandResponse AiHelp() => CommandResponse.Ok(
        """
        ServicePilot AI 操作指南

        你正在操作一个 Windows 托盘优先的本地开发服务管理器。ServicePilot 适合启动、停止、重启和查看本机项目服务，例如 npm/Vite、dotnet、Python、Batch、PowerShell、Node.js 脚本。

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
             ServicePilot.exe step run "SERVICE" "STEP" --variable "VALUE"
             ServicePilot.exe stop "SERVICE"
             ServicePilot.exe restart "SERVICE" --variable "VALUE"

        重要规则:
          - 启动、停止、重启、执行步骤、读取运行时日志和 shutdown 需要托盘实例正在运行。没有参数启动 ServicePilot.exe 可启动托盘实例。
          - SERVICE、STEP、TEMPLATE 可以使用名称或 GUID。STEP 也可以用数字: 1..N 表示启动执行步骤的显示编号；0 是旧内部顺序兼容入口。
          - 服务级预设变量用于启动、重启和启动执行步骤。
          - 不启动执行步骤可以有自己的步骤变量。维护命令:
             ServicePilot.exe step variables "SERVICE" "STEP" --json
             ServicePilot.exe step variable-add "SERVICE" "STEP" --variable "VALUE"
             ServicePilot.exe step variable-remove "SERVICE" "STEP" --variable "VALUE"
             ServicePilot.exe step variable-clear "SERVICE" "STEP"
          - 模板步骤变量也可由 CLI 维护:
             ServicePilot.exe template step-variables "TEMPLATE" "STEP" --json
             ServicePilot.exe template step-variable-add "TEMPLATE" "STEP" --variable "VALUE"
             ServicePilot.exe template step-variable-remove "TEMPLATE" "STEP" --variable "VALUE"
             ServicePilot.exe template step-variable-clear "TEMPLATE" "STEP"
          - --variable 会注入环境变量 SERVICEPILOT_VARIABLE，并替换脚本里的 {{variable}} / {{变量}}，前提是步骤启用了 UseVariable。
          - 不要猜配置。修改前先用 --json 查看当前服务、步骤、变量和模板。
          - 修改配置前可以运行 doctor --json，先处理缺失目录、空步骤、重名等问题。
          - 删除服务或模板时必须指定明确名称或 id。
          - ServicePilot 没有 start all。可以 stop all，但批量启动必须由调用方逐个服务显式启动。
          - 自动化测试请先设置 SERVICEPILOT_CONFIG_DIR，避免写入用户真实 %APPDATA%\ServicePilot\config.json。

        常用新增/编辑:
          ServicePilot.exe service add --name "Frontend" --dir "D:\app" --step "Set API|PowerShell|true|true|..." --step "Start|Batch|false|true|npm run dev" --preset "http://localhost:9000"
          ServicePilot.exe service edit "Frontend" --preset "http://localhost:9000" --preset "https://example.test/api"
          ServicePilot.exe template save-from-service --service "Frontend" --name "Vite Frontend"
          ServicePilot.exe template apply "Vite Frontend" --service "Frontend"
          ServicePilot.exe template export "Vite Frontend" --file ".\vite-frontend.servicepilot-template.json"
          ServicePilot.exe template import --file ".\vite-frontend.servicepilot-template.json"

        机器可读优先:
          list --json
          status all --json
          doctor --json
          service get SERVICE --json
          step list SERVICE --json
          logs SERVICE --tail N --json
          template list --json
          template get TEMPLATE --json
        """);

    private static CommandResponse Version()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        return CommandResponse.Ok($"ServicePilot {version}");
    }

    private CommandResponse Doctor(string[] args)
    {
        var issues = BuildDiagnostics();
        var errorCount = issues.Count(i => i.Severity == "Error");
        var warningCount = issues.Count(i => i.Severity == "Warning");
        var infoCount = issues.Count(i => i.Severity == "Info");

        var payload = new
        {
            ConfigPath = _configService.PathToConfig,
            ConfigDirectory = _configService.ConfigDirectory,
            ServiceCount = _appConfig.Services.Count,
            TemplateCount = _appConfig.ServiceTemplates.Count,
            Counts = new
            {
                Errors = errorCount,
                Warnings = warningCount,
                Info = infoCount
            },
            Issues = issues
        };

        if (HasFlag(args, "--json"))
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            return errorCount > 0
                ? CommandResponse.Error(json, 2)
                : CommandResponse.Ok(json);
        }

        if (issues.Count == 0)
            return CommandResponse.Ok($"配置体检通过。services={_appConfig.Services.Count} templates={_appConfig.ServiceTemplates.Count} config=\"{_configService.PathToConfig}\"");

        var lines = new List<string>
        {
            $"配置体检发现问题: errors={errorCount} warnings={warningCount} info={infoCount}",
            $"config=\"{_configService.PathToConfig}\""
        };
        lines.AddRange(issues.Select(issue => $"[{issue.Severity}] {issue.Code} {issue.Target} - {issue.Message}"));
        var output = string.Join(Environment.NewLine, lines);
        return errorCount > 0
            ? CommandResponse.Error(output, 2)
            : CommandResponse.Ok(output);
    }

    private List<DiagnosticIssue> BuildDiagnostics()
    {
        var issues = new List<DiagnosticIssue>();

        AddDuplicateNameIssues(issues, _appConfig.Services, s => s.Name, "SERVICE_NAME_DUPLICATE", "service", "服务名称重复。");
        AddDuplicateNameIssues(issues, _appConfig.ServiceTemplates, t => t.Name, "TEMPLATE_NAME_DUPLICATE", "template", "模板名称重复。");

        foreach (var service in _appConfig.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var target = $"service:{DisplayName(service.Name, service.Id)}";
            if (string.IsNullOrWhiteSpace(service.Name))
                issues.Add(new DiagnosticIssue("Error", "SERVICE_NAME_EMPTY", target, "服务名称不能为空。"));
            if (string.IsNullOrWhiteSpace(service.WorkingDirectory))
                issues.Add(new DiagnosticIssue("Error", "SERVICE_DIR_EMPTY", target, "服务工作目录不能为空。"));
            else if (!Directory.Exists(service.WorkingDirectory))
                issues.Add(new DiagnosticIssue("Error", "SERVICE_DIR_MISSING", target, $"服务工作目录不存在: {service.WorkingDirectory}"));

            ValidateSteps(issues, service.ScriptSteps, target, requiresStartupStep: true);
            AddDuplicateVariablesIssue(issues, service.PresetVariables, target, "SERVICE_PRESET_DUPLICATE", "服务预设变量重复。");
        }

        foreach (var template in _appConfig.ServiceTemplates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var target = $"template:{DisplayName(template.Name, template.Id)}";
            if (string.IsNullOrWhiteSpace(template.Name))
                issues.Add(new DiagnosticIssue("Error", "TEMPLATE_NAME_EMPTY", target, "模板名称不能为空。"));

            ValidateSteps(issues, template.ScriptSteps, target, requiresStartupStep: false);
            AddDuplicateVariablesIssue(issues, template.PresetVariables, target, "TEMPLATE_PRESET_DUPLICATE", "模板预设变量重复。");
        }

        return issues;
    }

    private static void ValidateSteps(List<DiagnosticIssue> issues, IReadOnlyList<ScriptStep> steps, string ownerTarget, bool requiresStartupStep)
    {
        if (steps.Count == 0)
        {
            issues.Add(new DiagnosticIssue("Error", "STEPS_EMPTY", ownerTarget, "至少需要一个脚本步骤。"));
            return;
        }

        if (requiresStartupStep && !steps.Any(s => s.RunOnStart && !string.IsNullOrWhiteSpace(s.Content)))
            issues.Add(new DiagnosticIssue("Warning", "STARTUP_STEPS_EMPTY", ownerTarget, "没有可启动执行的脚本步骤；普通启动不会运行任何命令。"));

        var duplicateOrders = steps
            .GroupBy(s => s.Order)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateOrders.Count > 0)
            issues.Add(new DiagnosticIssue("Warning", "STEP_ORDER_DUPLICATE", ownerTarget, $"步骤顺序重复: {string.Join(", ", duplicateOrders)}"));

        AddDuplicateNameIssues(issues, steps, s => s.Name, "STEP_NAME_DUPLICATE", ownerTarget, "步骤名称重复。");

        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var stepTarget = $"{ownerTarget}/step:{DisplayName(step.Name, step.Id)}";
            if (string.IsNullOrWhiteSpace(step.Name))
                issues.Add(new DiagnosticIssue("Warning", "STEP_NAME_EMPTY", stepTarget, "步骤名称为空，建议填写可读名称。"));
            if (string.IsNullOrWhiteSpace(step.Content))
                issues.Add(new DiagnosticIssue("Error", "STEP_CONTENT_EMPTY", stepTarget, "脚本内容不能为空。"));
            if (!step.RunOnStart && step.UseVariable)
                AddDuplicateVariablesIssue(issues, step.StepVariables, stepTarget, "STEP_VARIABLE_DUPLICATE", "步骤变量重复。");
        }
    }

    private static void AddDuplicateNameIssues<T>(
        List<DiagnosticIssue> issues,
        IEnumerable<T> items,
        Func<T, string> nameSelector,
        string code,
        string targetPrefix,
        string message)
    {
        foreach (var group in items
                     .Select(item => nameSelector(item).Trim())
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new DiagnosticIssue("Error", code, $"{targetPrefix}:{group.Key}", $"{message} count={group.Count()}"));
        }
    }

    private static void AddDuplicateVariablesIssue(List<DiagnosticIssue> issues, IReadOnlyList<string> variables, string target, string code, string message)
    {
        var duplicates = variables
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            issues.Add(new DiagnosticIssue("Warning", code, target, $"{message} values={string.Join(", ", duplicates)}"));
    }

    private static string DisplayName(string name, Guid id) =>
        string.IsNullOrWhiteSpace(name) ? id.ToString() : name.Trim();

    private CommandResponse List(string[] args)
    {
        var services = ConfigServices();
        if (HasFlag(args, "--json"))
            return Json(services.Select(s => new
            {
                s.Id,
                s.Name,
                s.WorkingDirectory,
                s.AutoStart,
                s.SortOrder,
                StepCount = s.ScriptSteps.Count,
                PresetVariables = s.PresetVariables
            }));

        if (services.Count == 0)
            return CommandResponse.Ok("没有配置服务。");

        var lines = services.Select(s =>
            $"{s.SortOrder}. {s.Name} ({s.Id}) steps={s.ScriptSteps.Count} presets={s.PresetVariables.Count} autostart={s.AutoStart} dir=\"{s.WorkingDirectory}\"");
        return CommandResponse.Ok(string.Join(Environment.NewLine, lines));
    }

    private CommandResponse Status(string[] args)
    {
        var json = HasFlag(args, "--json");
        var selector = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var services = RuntimeServices();

        if (!string.IsNullOrWhiteSpace(selector) && !IsAll(selector))
        {
            var service = FindRuntime(selector);
            if (service == null)
                return CommandResponse.Error($"找不到服务: {selector}", 2);

            return json ? Json(ToStatusDto(service)) : CommandResponse.Ok(FormatStatus(service));
        }

        if (json)
            return Json(new
            {
                Total = services.Count,
                Counts = services.GroupBy(s => s.State).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                Services = services.Select(ToStatusDto)
            });

        if (services.Count == 0)
            return CommandResponse.Ok("没有配置服务。");

        var header = $"服务总数: {services.Count}, 运行中: {services.Count(s => s.State == ProcessState.Running)}, 启动中: {services.Count(s => s.State == ProcessState.Starting)}, 已停止: {services.Count(s => s.State == ProcessState.Stopped)}, 启动失败: {services.Count(s => s.State == ProcessState.StartFailed)}, 出错: {services.Count(s => s.State == ProcessState.Error)}";
        return CommandResponse.Ok(header + Environment.NewLine + string.Join(Environment.NewLine, services.Select(FormatStatus)));
    }

    private CommandResponse Start(string[] args)
    {
        if (_processManager == null)
            return TrayRequired();

        if (args.Length == 0)
            return CommandResponse.Error("缺少目标。用法: start SERVICE [--variable VALUE]", 2);

        var options = ParseStartOptions(args);
        if (IsAll(args[0]))
            return CommandResponse.Error("启动全部服务功能已移除，请逐个启动服务。", 2);

        var service = FindRuntime(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        RememberService(service.Config.Id);
        RememberVariable(service.Config.Id, options.Variable);
        return _processManager.StartService(service.Config.Id, options)
            ? CommandResponse.Ok($"已发送启动命令: {service.Config.Name}")
            : CommandResponse.Error($"启动失败: {service.Config.Name}", 2);
    }

    private async Task<CommandResponse> StopAsync(string[] args)
    {
        if (_processManager == null)
            return TrayRequired();

        if (args.Length == 0)
            return CommandResponse.Error("缺少目标。用法: stop all|SERVICE", 2);

        if (IsAll(args[0]))
        {
            await _processManager.StopAllAsync();
            return CommandResponse.Ok("已停止全部正在运行的服务。");
        }

        var service = FindRuntime(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        RememberService(service.Config.Id);
        await _processManager.StopServiceAsync(service.Config.Id);
        return CommandResponse.Ok($"已停止服务: {service.Config.Name}");
    }

    private async Task<CommandResponse> RestartAsync(string[] args)
    {
        if (_processManager == null)
            return TrayRequired();

        if (args.Length == 0)
            return CommandResponse.Error("缺少目标。用法: restart all|SERVICE [--variable VALUE]", 2);

        var options = ParseStartOptions(args);
        if (IsAll(args[0]))
        {
            foreach (var service in _processManager.Snapshot())
            {
                RememberService(service.Config.Id);
                RememberVariable(service.Config.Id, options.Variable);
                await _processManager.RestartServiceAsync(service.Config.Id, options);
            }

            return CommandResponse.Ok("已发送重启全部服务命令。");
        }

        var target = FindRuntime(args[0]);
        if (target == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        RememberService(target.Config.Id);
        RememberVariable(target.Config.Id, options.Variable);
        await _processManager.RestartServiceAsync(target.Config.Id, options);
        return CommandResponse.Ok($"已重启服务: {target.Config.Name}");
    }

    private CommandResponse Logs(string[] args)
    {
        if (_processManager == null || _logProvider == null)
            return TrayRequired();

        var selector = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !int.TryParse(a, out _));
        if (selector == null)
            return CommandResponse.Error("缺少服务名或 id。用法: logs SERVICE [--tail N] [--json]", 2);

        var service = FindRuntime(selector);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {selector}", 2);

        RememberService(service.Config.Id);
        var tail = ReadIntOption(args, "--tail") ?? 100;
        var entries = _logProvider(service.Config.Id).TakeLast(Math.Max(1, tail)).ToList();

        if (HasFlag(args, "--json"))
            return Json(entries);

        if (entries.Count == 0)
            return CommandResponse.Ok($"服务没有日志: {service.Config.Name}");

        var lines = entries.Select(e => $"{e.Timestamp:HH:mm:ss} [{e.Level}] {e.Message}");
        return CommandResponse.Ok(string.Join(Environment.NewLine, lines));
    }

    private async Task<CommandResponse> ServiceAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: service list|get|add|edit|remove|start|stop|restart|logs", 2);

        var subCommand = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return subCommand switch
        {
            "list" or "ls" => List(rest),
            "get" => ServiceGet(rest),
            "add" => await AddAsync(rest),
            "edit" => await ServiceEditAsync(rest),
            "remove" or "delete" => await RemoveAsync(rest),
            "start" => Start(rest),
            "stop" => await StopAsync(rest),
            "restart" => await RestartAsync(rest),
            "logs" or "log" => Logs(rest),
            "step" => await StepAsync(rest),
            _ => CommandResponse.Error($"未知 service 命令: {args[0]}", 2)
        };
    }

    private CommandResponse ServiceGet(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少服务名或 id。用法: service get SERVICE [--json]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        if (HasFlag(args, "--json"))
            return Json(service);

        var startupNumber = 1;
        var steps = service.ScriptSteps
            .OrderBy(s => s.Order)
            .Select(s =>
            {
                var label = s.RunOnStart ? $"{startupNumber++}. {s.Name}" : s.Name;
                return $"{label} type={s.ScriptType} useVariable={s.UseVariable} runOnStart={s.RunOnStart} openLogOnRun={s.OpenLogOnRun}";
            });
        var presets = service.PresetVariables.Count == 0 ? "(none)" : string.Join(", ", service.PresetVariables);
        return CommandResponse.Ok(
            $"{service.Name} ({service.Id})\ndir=\"{service.WorkingDirectory}\"\nautostart={service.AutoStart}\npresets={presets}\nsteps:\n{string.Join(Environment.NewLine, steps)}");
    }

    private async Task<CommandResponse> ServiceEditAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少服务名或 id。用法: service edit SERVICE [--name NAME] [--dir DIR] [--step ...] [--preset VALUE]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var newName = ReadOption(args, "--name");
        if (!string.IsNullOrWhiteSpace(newName))
        {
            var duplicate = _appConfig.Services.Any(s => s.Id != service.Id &&
                                                         string.Equals(s.Name, newName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (duplicate)
                return CommandResponse.Error($"服务名称已存在: {newName.Trim()}", 2);

            updated.Name = newName.Trim();
        }

        var dir = ReadOption(args, "--dir") ?? ReadOption(args, "--working-directory");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            if (!Directory.Exists(dir))
                return CommandResponse.Error($"工作目录不存在: {dir}", 2);

            updated.WorkingDirectory = dir.Trim();
        }

        var autoStart = ReadBoolOption(args, "--autostart");
        if (autoStart.HasValue)
            updated.AutoStart = autoStart.Value;

        var steps = ParseSteps(args);
        if (steps.Count > 0)
            updated.ScriptSteps = steps;

        if (HasAnyOption(args, "--preset", "--preset-variable") || HasFlag(args, "--clear-presets"))
            updated.PresetVariables = HasFlag(args, "--clear-presets") ? [] : ReadPresets(args);

        await UpdateConfigAsync(updated);
        return CommandResponse.Ok($"已更新服务: {updated.Name}");
    }

    private async Task<CommandResponse> AddAsync(string[] args)
    {
        var name = ReadOption(args, "--name");
        var dir = ReadOption(args, "--dir") ?? ReadOption(args, "--working-directory");

        if (string.IsNullOrWhiteSpace(name))
            return CommandResponse.Error("缺少 --name。", 2);

        if (string.IsNullOrWhiteSpace(dir))
            return CommandResponse.Error("缺少 --dir。", 2);

        if (!Directory.Exists(dir))
            return CommandResponse.Error($"工作目录不存在: {dir}", 2);

        var steps = ParseSteps(args);
        if (steps.Count == 0)
            return CommandResponse.Error("缺少脚本步骤。使用 --step \"Name|Batch|command\" 或 --content。", 2);

        if (FindConfig(name.Trim()) != null)
            return CommandResponse.Error($"服务名称已存在: {name.Trim()}", 2);

        var config = new ServiceConfig
        {
            Name = name.Trim(),
            WorkingDirectory = dir.Trim(),
            AutoStart = HasFlag(args, "--autostart"),
            ScriptSteps = steps,
            PresetVariables = ReadPresets(args)
        };

        await AddConfigAsync(config);
        return CommandResponse.Ok($"已新增服务: {config.Name} ({config.Id})");
    }

    private async Task<CommandResponse> StepAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: step list SERVICE [--json] | step run SERVICE STEP [--variable VALUE] | step variables|variable-add|variable-remove|variable-clear ...", 2);

        var subCommand = args[0].ToLowerInvariant();
        return subCommand switch
        {
            "list" or "ls" => StepList(args.Skip(1).ToArray()),
            "run" => StepRun(args.Skip(1).ToArray()),
            "variables" or "variable-list" or "vars" => StepVariables(args.Skip(1).ToArray()),
            "variable-add" or "var-add" => await StepVariableAddAsync(args.Skip(1).ToArray()),
            "variable-remove" or "variable-delete" or "var-remove" or "var-delete" => await StepVariableRemoveAsync(args.Skip(1).ToArray()),
            "variable-clear" or "var-clear" => await StepVariableClearAsync(args.Skip(1).ToArray()),
            _ => CommandResponse.Error($"未知 step 命令: {args[0]}", 2)
        };
    }

    private CommandResponse StepList(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: step list SERVICE [--json]", 2);

        var runtime = FindRuntime(args[0]);
        var service = runtime?.Config ?? FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var steps = service.ScriptSteps.OrderBy(s => s.Order).ToList();
        if (HasFlag(args, "--json"))
            return Json(steps.Select(step => new
            {
                step.Id,
                step.Name,
                step.ScriptType,
                step.UseVariable,
                step.RunOnStart,
                step.OpenLogOnRun,
                step.StepVariables,
                step.Order,
                DisplayOrder = step.RunOnStart
                    ? steps.TakeWhile(s => s.Id != step.Id).Count(s => s.RunOnStart) + 1
                    : (int?)null,
                HasContent = !string.IsNullOrWhiteSpace(step.Content),
                Runtime = runtime != null && runtime.StepStates.TryGetValue(step.Id, out var state) ? state : null
            }));

        return steps.Count == 0
            ? CommandResponse.Ok($"服务没有步骤: {service.Name}")
            : CommandResponse.Ok(string.Join(Environment.NewLine, FormatStepList(steps)));
    }

    private CommandResponse StepRun(string[] args)
    {
        if (_processManager == null)
            return TrayRequired();

        if (args.Length < 2)
            return CommandResponse.Error("用法: step run SERVICE STEP [--variable VALUE]", 2);

        var service = FindRuntime(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var step = FindStep(service.Config, args[1]);
        if (step == null)
            return CommandResponse.Error($"找不到步骤: {args[1]}", 2);
        if (string.IsNullOrWhiteSpace(step.Content))
            return CommandResponse.Error($"步骤没有脚本内容，无法执行: {step.Name}", 2);

        var variable = ReadVariable(args);
        RememberService(service.Config.Id);
        if (step.UseVariable)
            RememberVariableForStep(service.Config, step, variable);
        return _processManager.RunStep(service.Config.Id, step.Id, variable)
            ? CommandResponse.Ok($"已发送执行步骤命令: {service.Config.Name} / {step.Name}")
            : CommandResponse.Error($"执行步骤失败: {service.Config.Name} / {step.Name}", 2);
    }

    private CommandResponse StepVariables(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step variables SERVICE STEP [--json]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var step = FindStep(service, args[1]);
        if (step == null)
            return CommandResponse.Error($"找不到步骤: {args[1]}", 2);

        var variables = step.RunOnStart ? service.PresetVariables : step.StepVariables;
        if (HasFlag(args, "--json"))
            return Json(new
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                StepId = step.Id,
                StepName = step.Name,
                step.RunOnStart,
                VariableScope = step.RunOnStart ? "service" : "step",
                Variables = variables
            });

        if (variables.Count == 0)
            return CommandResponse.Ok($"{service.Name} / {step.Name} 没有配置变量。");

        return CommandResponse.Ok(string.Join(Environment.NewLine, variables));
    }

    private async Task<CommandResponse> StepVariableAddAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step variable-add SERVICE STEP --variable VALUE", 2);

        var variable = ReadVariable(args);
        if (string.IsNullOrWhiteSpace(variable))
            return CommandResponse.Error("缺少 --variable VALUE。", 2);

        var result = await UpdateStepVariablesAsync(args[0], args[1], variables =>
        {
            var normalized = variable.Trim();
            if (!variables.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)))
                variables.Add(normalized);
        });

        return result ?? CommandResponse.Ok($"已新增步骤变量: {variable.Trim()}");
    }

    private async Task<CommandResponse> StepVariableRemoveAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step variable-remove SERVICE STEP --variable VALUE", 2);

        var variable = ReadVariable(args);
        if (string.IsNullOrWhiteSpace(variable))
            return CommandResponse.Error("缺少 --variable VALUE。", 2);

        var result = await UpdateStepVariablesAsync(args[0], args[1], variables =>
            variables.RemoveAll(v => string.Equals(v, variable.Trim(), StringComparison.OrdinalIgnoreCase)));

        return result ?? CommandResponse.Ok($"已删除步骤变量: {variable.Trim()}");
    }

    private async Task<CommandResponse> StepVariableClearAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step variable-clear SERVICE STEP", 2);

        var result = await UpdateStepVariablesAsync(args[0], args[1], variables => variables.Clear());
        return result ?? CommandResponse.Ok("已清空步骤变量。");
    }

    private async Task<CommandResponse?> UpdateStepVariablesAsync(string serviceSelector, string stepSelector, Action<List<string>> update)
    {
        var service = FindConfig(serviceSelector);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {serviceSelector}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var step = FindStep(updated, stepSelector);
        if (step == null)
            return CommandResponse.Error($"找不到步骤: {stepSelector}", 2);

        var variables = step.RunOnStart ? updated.PresetVariables : step.StepVariables;
        update(variables);
        await UpdateConfigAsync(updated);
        return null;
    }

    private CommandResponse Templates(string[] args)
    {
        if (HasFlag(args, "--json"))
            return Json(_appConfig.ServiceTemplates);

        if (_appConfig.ServiceTemplates.Count == 0)
            return CommandResponse.Ok("没有服务模板。");

        var lines = _appConfig.ServiceTemplates
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => $"{t.Name} ({t.Id}) steps={t.ScriptSteps.Count} presets={t.PresetVariables.Count} - {t.Description}");
        return CommandResponse.Ok(string.Join(Environment.NewLine, lines));
    }

    private async Task<CommandResponse> TemplateAsync(string[] args)
    {
        if (args.Length == 0 || string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
            return Templates(args.Skip(1).ToArray());

        var subCommand = args[0].ToLowerInvariant();
        if (subCommand != "create")
        {
            var rest = args.Skip(1).ToArray();
            return subCommand switch
            {
                "get" => TemplateGet(rest),
                "add" => await TemplateAddAsync(rest),
                "edit" => await TemplateEditAsync(rest),
            "remove" or "delete" => await TemplateRemoveAsync(rest),
            "apply" => await TemplateApplyAsync(rest),
            "save-from-service" => await TemplateSaveFromServiceAsync(rest),
            "export" => await TemplateExportAsync(rest),
            "import" => await TemplateImportAsync(rest),
            "step-variables" or "step-variable-list" or "step-vars" => TemplateStepVariables(rest),
            "step-variable-add" or "step-var-add" => await TemplateStepVariableAddAsync(rest),
            "step-variable-remove" or "step-variable-delete" or "step-var-remove" or "step-var-delete" => await TemplateStepVariableRemoveAsync(rest),
            "step-variable-clear" or "step-var-clear" => await TemplateStepVariableClearAsync(rest),
            _ => CommandResponse.Error("未知模板命令。用法: template list|get|add|edit|remove|apply|save-from-service|export|import|step-variables|step-variable-add|step-variable-remove|step-variable-clear", 2)
        };
    }

        var templateId = ReadOption(args, "--template") ?? "auto";
        var dir = ReadOption(args, "--dir") ?? ReadOption(args, "--working-directory");
        var name = ReadOption(args, "--name");

        if (string.IsNullOrWhiteSpace(dir))
            return CommandResponse.Error("缺少 --dir。", 2);

        try
        {
            var config = ServiceTemplateService.Create(templateId, dir, name, HasFlag(args, "--autostart"));
            if (FindConfig(config.Name) != null)
                return CommandResponse.Error($"服务名称已存在: {config.Name}", 2);

            await AddConfigAsync(config);
            return CommandResponse.Ok($"已按内置模板创建服务: {config.Name} ({config.Id})");
        }
        catch (Exception ex)
        {
            return CommandResponse.Error($"内置模板创建失败: {ex.Message}", 2);
        }
    }

    private CommandResponse TemplateGet(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少模板名或 id。用法: template get TEMPLATE [--json]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        if (HasFlag(args, "--json"))
            return Json(template);

        var steps = FormatStepList(template.ScriptSteps.OrderBy(s => s.Order).ToList());
        var presets = template.PresetVariables.Count == 0 ? "(none)" : string.Join(", ", template.PresetVariables);
        return CommandResponse.Ok($"{template.Name} ({template.Id})\n{template.Description}\npresets={presets}\nsteps:\n{string.Join(Environment.NewLine, steps)}");
    }

    private async Task<CommandResponse> TemplateAddAsync(string[] args)
    {
        var name = ReadOption(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
            return CommandResponse.Error("缺少 --name。", 2);
        if (FindTemplate(name) != null)
            return CommandResponse.Error($"模板名称已存在: {name.Trim()}", 2);

        var steps = ParseSteps(args);
        if (steps.Count == 0)
            return CommandResponse.Error("缺少脚本步骤。使用 --step \"Name|Batch|command\" 或 --content。", 2);

        var now = DateTime.Now;
        var template = new ServiceTemplate
        {
            Name = name.Trim(),
            Description = ReadOption(args, "--description") ?? string.Empty,
            ScriptSteps = steps,
            PresetVariables = ReadPresets(args),
            CreatedAt = now,
            UpdatedAt = now
        };

        _appConfig.ServiceTemplates.Add(template);
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已新增模板: {template.Name} ({template.Id})");
    }

    private async Task<CommandResponse> TemplateEditAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少模板名或 id。用法: template edit TEMPLATE [--name NAME] [--step ...] [--preset VALUE]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var name = ReadOption(args, "--name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (_appConfig.ServiceTemplates.Any(t => t.Id != template.Id &&
                                                      string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return CommandResponse.Error($"模板名称已存在: {name.Trim()}", 2);
            template.Name = name.Trim();
        }

        var description = ReadOption(args, "--description");
        if (description != null)
            template.Description = description;

        var steps = ParseSteps(args);
        if (steps.Count > 0)
            template.ScriptSteps = steps;

        if (HasAnyOption(args, "--preset", "--preset-variable") || HasFlag(args, "--clear-presets"))
            template.PresetVariables = HasFlag(args, "--clear-presets") ? [] : ReadPresets(args);

        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已更新模板: {template.Name}");
    }

    private async Task<CommandResponse> TemplateRemoveAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少模板名或 id。用法: template remove TEMPLATE", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        _appConfig.ServiceTemplates.RemoveAll(t => t.Id == template.Id);
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已删除模板: {template.Name}");
    }

    private async Task<CommandResponse> TemplateApplyAsync(string[] args)
    {
        var templateSelector = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var serviceSelector = ReadOption(args, "--service");
        if (string.IsNullOrWhiteSpace(templateSelector) || string.IsNullOrWhiteSpace(serviceSelector))
            return CommandResponse.Error("用法: template apply TEMPLATE --service SERVICE", 2);

        var template = FindTemplate(templateSelector);
        var service = FindConfig(serviceSelector);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {templateSelector}", 2);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {serviceSelector}", 2);

        var updated = ScriptDefinitionService.ApplyTemplateToService(service, template);
        await UpdateConfigAsync(updated);
        return CommandResponse.Ok($"已应用模板 {template.Name} 到服务 {updated.Name}");
    }

    private async Task<CommandResponse> TemplateSaveFromServiceAsync(string[] args)
    {
        var serviceSelector = ReadOption(args, "--service");
        var name = ReadOption(args, "--name");
        if (string.IsNullOrWhiteSpace(serviceSelector) || string.IsNullOrWhiteSpace(name))
            return CommandResponse.Error("用法: template save-from-service --service SERVICE --name NAME [--description TEXT]", 2);

        if (FindTemplate(name) != null)
            return CommandResponse.Error($"模板名称已存在: {name.Trim()}", 2);

        var service = FindConfig(serviceSelector);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {serviceSelector}", 2);

        var template = ScriptDefinitionService.CreateTemplateFromService(service, name.Trim(), ReadOption(args, "--description") ?? string.Empty);
        _appConfig.ServiceTemplates.Add(template);
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已保存模板: {template.Name} ({template.Id})");
    }

    private async Task<CommandResponse> TemplateExportAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: template export TEMPLATE --file FILE", 2);

        var templateSelector = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
        var file = ReadFileOption(args);
        if (string.IsNullOrWhiteSpace(templateSelector) || string.IsNullOrWhiteSpace(file))
            return CommandResponse.Error("用法: template export TEMPLATE --file FILE", 2);

        var template = FindTemplate(templateSelector);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {templateSelector}", 2);

        try
        {
            await TemplateExchangeService.ExportAsync(template, file);
            return CommandResponse.Ok($"已导出模板: {template.Name} -> {Path.GetFullPath(file)}");
        }
        catch (Exception ex)
        {
            return CommandResponse.Error($"模板导出失败: {ex.Message}", 2);
        }
    }

    private async Task<CommandResponse> TemplateImportAsync(string[] args)
    {
        var file = ReadFileOption(args) ??
                   args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(file))
            return CommandResponse.Error("用法: template import --file FILE", 2);

        try
        {
            var imported = await TemplateExchangeService.ImportAsync(file, _appConfig.ServiceTemplates);
            _appConfig.ServiceTemplates.AddRange(imported);
            await _configService.SaveAsync(_appConfig);
            var names = string.Join(", ", imported.Select(template => template.Name));
            return CommandResponse.Ok($"已导入 {imported.Count} 个模板: {names}");
        }
        catch (Exception ex)
        {
            return CommandResponse.Error($"模板导入失败: {ex.Message}", 2);
        }
    }

    private CommandResponse TemplateStepVariables(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step-variables TEMPLATE STEP [--json]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var step = FindStep(template.ScriptSteps, args[1]);
        if (step == null)
            return CommandResponse.Error($"找不到模板步骤: {args[1]}", 2);

        var variables = step.RunOnStart ? template.PresetVariables : step.StepVariables;
        if (HasFlag(args, "--json"))
            return Json(new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                StepId = step.Id,
                StepName = step.Name,
                step.RunOnStart,
                VariableScope = step.RunOnStart ? "template" : "step",
                Variables = variables
            });

        if (variables.Count == 0)
            return CommandResponse.Ok($"{template.Name} / {step.Name} 没有配置变量。");

        return CommandResponse.Ok(string.Join(Environment.NewLine, variables));
    }

    private async Task<CommandResponse> TemplateStepVariableAddAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step-variable-add TEMPLATE STEP --variable VALUE", 2);

        var variable = ReadVariable(args);
        if (string.IsNullOrWhiteSpace(variable))
            return CommandResponse.Error("缺少 --variable VALUE。", 2);

        var result = await UpdateTemplateStepVariablesAsync(args[0], args[1], variables =>
        {
            var normalized = variable.Trim();
            if (!variables.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)))
                variables.Add(normalized);
        });

        return result ?? CommandResponse.Ok($"已新增模板步骤变量: {variable.Trim()}");
    }

    private async Task<CommandResponse> TemplateStepVariableRemoveAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step-variable-remove TEMPLATE STEP --variable VALUE", 2);

        var variable = ReadVariable(args);
        if (string.IsNullOrWhiteSpace(variable))
            return CommandResponse.Error("缺少 --variable VALUE。", 2);

        var result = await UpdateTemplateStepVariablesAsync(args[0], args[1], variables =>
            variables.RemoveAll(v => string.Equals(v, variable.Trim(), StringComparison.OrdinalIgnoreCase)));

        return result ?? CommandResponse.Ok($"已删除模板步骤变量: {variable.Trim()}");
    }

    private async Task<CommandResponse> TemplateStepVariableClearAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step-variable-clear TEMPLATE STEP", 2);

        var result = await UpdateTemplateStepVariablesAsync(args[0], args[1], variables => variables.Clear());
        return result ?? CommandResponse.Ok("已清空模板步骤变量。");
    }

    private async Task<CommandResponse?> UpdateTemplateStepVariablesAsync(string templateSelector, string stepSelector, Action<List<string>> update)
    {
        var template = FindTemplate(templateSelector);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {templateSelector}", 2);

        var step = FindStep(template.ScriptSteps, stepSelector);
        if (step == null)
            return CommandResponse.Error($"找不到模板步骤: {stepSelector}", 2);

        var variables = step.RunOnStart ? template.PresetVariables : step.StepVariables;
        update(variables);
        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        return null;
    }

    private async Task<CommandResponse> RemoveAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少服务名或 id。用法: remove SERVICE", 2);

        var target = FindConfig(args[0]);
        if (target == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        if (_mainViewModel != null)
        {
            await _mainViewModel.RemoveServiceAsync(target.Id);
        }
        else if (_processManager != null)
        {
            await _processManager.RemoveServiceAsync(target.Id);
            _appConfig.Services.Remove(target);
            await _configService.SaveAsync(_appConfig);
        }
        else
        {
            _appConfig.Services.Remove(target);
            await _configService.SaveAsync(_appConfig);
        }

        return CommandResponse.Ok($"已删除服务: {target.Name}");
    }

    private CommandResponse Shutdown()
    {
        if (_shutdownRequested == null)
            return TrayRequired();

        _ = _shutdownRequested();
        return CommandResponse.Ok("已请求 ServicePilot 托盘实例退出。");
    }

    private List<ScriptStep> ParseSteps(string[] args)
    {
        var result = new List<ScriptStep>();
        var rawSteps = ReadOptions(args, "--step");

        foreach (var raw in rawSteps)
        {
            var parts = raw.Split('|', 6);
            if (parts.Length is not (3 or 4 or 5 or 6))
                continue;

            if (!Enum.TryParse<ScriptType>(parts[1], ignoreCase: true, out var type))
                type = ScriptType.Batch;

            result.Add(new ScriptStep
            {
                Name = parts[0],
                ScriptType = type,
                UseVariable = parts.Length < 4 || !bool.TryParse(parts[2], out var useVariable) || useVariable,
                RunOnStart = parts.Length < 5 || !bool.TryParse(parts[3], out var runOnStart) || runOnStart,
                OpenLogOnRun = parts.Length >= 6 && bool.TryParse(parts[4], out var openLogOnRun) && openLogOnRun,
                Content = parts.Length == 6 ? parts[5] : parts.Length == 5 ? parts[4] : parts.Length == 4 ? parts[3] : parts[2],
                Order = result.Count
            });
        }

        var content = ReadOption(args, "--content");
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            result.Add(new ScriptStep
            {
                Name = ReadOption(args, "--step-name") ?? "Main",
                ScriptType = ReadScriptType(args),
                UseVariable = ReadBoolOption(args, "--use-variable") ?? true,
                RunOnStart = ReadBoolOption(args, "--run-on-start") ?? true,
                OpenLogOnRun = ReadBoolOption(args, "--open-log-on-run") ?? false,
                Content = content,
                Order = 0
            });
        }

        return result;
    }

    private async Task AddConfigAsync(ServiceConfig config)
    {
        config.SortOrder = ConfigServices().Count;

        if (_mainViewModel != null)
        {
            var vm = _mainViewModel.AddService(config);
            await _mainViewModel.SaveConfigAsync();
            if (config.AutoStart)
                _processManager?.StartService(vm.Config.Id);
            return;
        }

        _appConfig.Services.Add(config);
        await _configService.SaveAsync(_appConfig);
        _processManager?.AddService(config);
        if (config.AutoStart)
            _processManager?.StartService(config.Id);
    }

    private async Task UpdateConfigAsync(ServiceConfig config)
    {
        if (_mainViewModel != null)
        {
            await _mainViewModel.UpdateServiceAsync(config);
            return;
        }

        var index = _appConfig.Services.FindIndex(s => s.Id == config.Id);
        if (index >= 0)
            _appConfig.Services[index] = config;

        _processManager?.UpdateService(config);
        await _configService.SaveAsync(_appConfig);
    }

    private IReadOnlyList<ServiceConfig> ConfigServices() =>
        _appConfig.Services
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<ServiceRuntimeState> RuntimeServices()
    {
        if (_processManager != null)
            return _processManager.Snapshot();

        return ConfigServices()
            .Select(config => new ServiceRuntimeState { Config = config, State = ProcessState.Stopped })
            .ToList();
    }

    private ServiceRuntimeState? FindRuntime(string selector)
    {
        if (_processManager != null)
            return _processManager.FindService(selector);

        var config = FindConfig(selector);
        return config == null ? null : new ServiceRuntimeState { Config = config, State = ProcessState.Stopped };
    }

    private ServiceConfig? FindConfig(string selector)
    {
        if (Guid.TryParse(selector, out var id))
            return _appConfig.Services.FirstOrDefault(s => s.Id == id);

        return _appConfig.Services
            .FirstOrDefault(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase));
    }

    private ServiceTemplate? FindTemplate(string selector)
    {
        if (Guid.TryParse(selector, out var id))
            return _appConfig.ServiceTemplates.FirstOrDefault(t => t.Id == id);

        return _appConfig.ServiceTemplates
            .FirstOrDefault(t => string.Equals(t.Name, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static ScriptStep? FindStep(ServiceConfig service, string selector) =>
        FindStep(service.ScriptSteps, selector);

    private static ScriptStep? FindStep(IReadOnlyList<ScriptStep> steps, string selector)
    {
        if (Guid.TryParse(selector, out var id))
            return steps.FirstOrDefault(s => s.Id == id);

        if (int.TryParse(selector, out var order))
        {
            if (order == 0)
                return steps.FirstOrDefault(s => s.Order == order);

            if (order > 0)
            {
                var startupSteps = steps
                    .Where(s => s.RunOnStart)
                    .OrderBy(s => s.Order)
                    .ToList();
                if (order <= startupSteps.Count)
                    return startupSteps[order - 1];
            }

            return steps.FirstOrDefault(s => s.Order == order);
        }

        return steps
            .FirstOrDefault(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static object ToStatusDto(ServiceRuntimeState service) => new
    {
        service.Config.Id,
        service.Config.Name,
        service.State,
        service.StartTime,
        service.LastError,
        service.ActiveVariable,
        service.Config.WorkingDirectory,
        service.Config.AutoStart,
        StepCount = service.Config.ScriptSteps.Count,
        service.Config.PresetVariables,
        StepStates = service.Config.ScriptSteps.OrderBy(s => s.Order).Select(step => new
        {
            step.Id,
            step.Name,
            step.UseVariable,
            step.RunOnStart,
            step.OpenLogOnRun,
            step.StepVariables,
            step.Order,
            DisplayOrder = step.RunOnStart
                ? service.Config.ScriptSteps
                    .Where(s => s.RunOnStart)
                    .OrderBy(s => s.Order)
                    .TakeWhile(s => s.Id != step.Id)
                    .Count() + 1
                : (int?)null,
            Runtime = service.StepStates.TryGetValue(step.Id, out var state) ? state : null
        })
    };

    private static string FormatStatus(ServiceRuntimeState service)
    {
        var uptime = service.StartTime == null ? "" : $" uptime={DateTime.Now - service.StartTime:hh\\:mm\\:ss}";
        var error = string.IsNullOrWhiteSpace(service.LastError) ? "" : $" error=\"{service.LastError}\"";
        var variable = string.IsNullOrWhiteSpace(service.ActiveVariable) ? "" : $" variable=\"{service.ActiveVariable}\"";
        return $"{service.Config.Name} ({service.Config.Id}) state={service.State}{variable}{uptime}{error}";
    }

    private static ServiceStartOptions ParseStartOptions(string[] args) =>
        new() { Variable = ReadVariable(args) };

    private void RememberVariable(Guid serviceId, string? variable) =>
        _variableUsageStore?.Remember(serviceId, variable);

    private void RememberService(Guid serviceId) =>
        _variableUsageStore?.RememberService(serviceId);

    private void RememberVariableForStep(ServiceConfig service, ScriptStep step, string? variable)
    {
        if (string.IsNullOrWhiteSpace(variable))
            return;

        _variableUsageStore?.Remember(step.RunOnStart ? service.Id : step.Id, variable.Trim());
    }

    private static string? ReadVariable(IReadOnlyList<string> args) =>
        ReadOption(args, "--variable") ?? ReadOption(args, "--var");

    private static string? ReadFileOption(IReadOnlyList<string> args) =>
        ReadOption(args, "--file") ?? ReadOption(args, "--path") ?? ReadOption(args, "--out") ?? ReadOption(args, "-o");

    private static List<string> ReadPresets(IReadOnlyList<string> args) =>
        ReadOptions(args, "--preset")
            .Concat(ReadOptions(args, "--preset-variable"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ScriptType ReadScriptType(string[] args)
    {
        var typeText = ReadOption(args, "--type") ?? "Batch";
        return Enum.TryParse<ScriptType>(typeText, ignoreCase: true, out var type)
            ? type
            : ScriptType.Batch;
    }

    private static CommandResponse Json(object value) =>
        CommandResponse.Ok(JsonSerializer.Serialize(value, JsonOptions));

    private static CommandResponse TrayRequired() =>
        CommandResponse.Error("这个命令需要 ServicePilot 托盘实例正在运行。请先不带参数启动 ServicePilot.exe。", 3);

    private static IEnumerable<string> FormatStepList(IReadOnlyList<ScriptStep> steps)
    {
        var startupNumber = 1;
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var label = step.RunOnStart ? $"{startupNumber++}. {step.Name}" : step.Name;
            var stepVariables = !step.RunOnStart && step.StepVariables.Count > 0
                ? $" stepVariables={step.StepVariables.Count}"
                : string.Empty;
            yield return $"{label} ({step.Id}) type={step.ScriptType} useVariable={step.UseVariable} runOnStart={step.RunOnStart} openLogOnRun={step.OpenLogOnRun}{stepVariables}";
        }
    }

    private static bool IsAll(string value) =>
        string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "*", StringComparison.OrdinalIgnoreCase);

    private static bool HasFlag(IEnumerable<string> args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static bool HasAnyOption(IReadOnlyList<string> args, params string[] names) =>
        names.Any(name => ReadOption(args, name) != null);

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static List<string> ReadOptions(IReadOnlyList<string> args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                values.Add(args[i + 1]);
        }

        return values;
    }

    private static int? ReadIntOption(IReadOnlyList<string> args, string name)
    {
        var value = ReadOption(args, name);
        return int.TryParse(value, out var result) ? result : null;
    }

    private static bool? ReadBoolOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return true;

            if (bool.TryParse(args[i + 1], out var result))
                return result;

            if (string.Equals(args[i + 1], "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i + 1], "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i + 1], "true", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(args[i + 1], "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i + 1], "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i + 1], "false", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return null;
    }
}
