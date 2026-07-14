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
    private readonly Func<Task<CommandResponse>>? _reloadRequested;

    private sealed record DiagnosticIssue(string Severity, string Code, string Target, string Message);

    public ServiceCommandProcessor(
        ConfigService configService,
        AppConfig appConfig,
        ProcessManager? processManager = null,
        MainViewModel? mainViewModel = null,
        Func<Guid, IReadOnlyList<LogEntry>>? logProvider = null,
        Func<Task>? shutdownRequested = null,
        PresetVariableUsageStore? variableUsageStore = null,
        Func<Task<CommandResponse>>? reloadRequested = null)
    {
        _configService = configService;
        _appConfig = appConfig;
        _processManager = processManager;
        _mainViewModel = mainViewModel;
        _logProvider = logProvider;
        _shutdownRequested = shutdownRequested;
        _variableUsageStore = variableUsageStore;
        _reloadRequested = reloadRequested;
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
            "config" => await ConfigAsync(rest),
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

    private async Task<CommandResponse> ConfigAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: config reload | config apply --file PATH", 2);

        var subCommand = args[0].ToLowerInvariant();
        return subCommand switch
        {
            "reload" => await ConfigReloadAsync(),
            "apply" => await ConfigApplyAsync(args.Skip(1).ToArray()),
            _ => CommandResponse.Error($"未知 config 命令: {args[0]}\n用法: config reload | config apply --file PATH", 2)
        };
    }

    private async Task<CommandResponse> ConfigReloadAsync()
    {
        if (_reloadRequested == null)
            return TrayRequired();

        return await _reloadRequested();
    }

    private async Task<CommandResponse> ConfigApplyAsync(string[] args)
    {
        var filePath = ReadFileOption(args);
        if (string.IsNullOrWhiteSpace(filePath))
            return CommandResponse.Error("缺少 --file PATH。用法: config apply --file PATH", 2);

        if (!File.Exists(filePath))
            return CommandResponse.Error($"文件不存在: {filePath}", 2);

        // Read and validate the new config
        AppConfig newConfig;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            newConfig = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            }) ?? throw new JsonException("反序列化结果为 null");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return CommandResponse.Error($"配置文件校验不通过: {ex.Message}", 2);
        }

        // Cache current config before overwriting
        var cacheDir = Path.Combine(_configService.ConfigDirectory, "config-cache");
        Directory.CreateDirectory(cacheDir);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var cachePath = Path.Combine(cacheDir, $"config.v2.{timestamp}.json");

        try
        {
            // Cache the current config
            if (File.Exists(_configService.PathToConfig))
                File.Copy(_configService.PathToConfig, cachePath, overwrite: true);

            // Save the new config
            await _configService.SaveAsync(newConfig);
        }
        catch (Exception ex)
        {
            // Roll back if we managed to write but something went wrong
            if (File.Exists(cachePath))
            {
                try
                {
                    File.Copy(cachePath, _configService.PathToConfig, overwrite: true);
                }
                catch
                {
                    // Best-effort rollback
                }
            }
            return CommandResponse.Error($"写入配置失败: {ex.Message}", 2);
        }

        // Trigger tray reload
        if (_reloadRequested != null)
        {
            var reloadResult = await _reloadRequested();
            if (reloadResult.ExitCode != 0)
            {
                // Reload failed — roll back
                var failedTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var failedPath = Path.Combine(cacheDir, $"config.v2.{failedTimestamp}.failed.json");
                try
                {
                    if (File.Exists(_configService.PathToConfig))
                        File.Move(_configService.PathToConfig, failedPath);
                    if (File.Exists(cachePath))
                        File.Copy(cachePath, _configService.PathToConfig, overwrite: true);
                }
                catch
                {
                    // Best-effort rollback
                }
                return CommandResponse.Error($"校验不通过，已回滚到缓存配置，失败文件保存为 {failedPath}");
            }
        }

        return CommandResponse.Ok("已应用配置");
    }

    private static string HelpText() =>
        """
        ServicePilot command line

        Usage:
          ServicePilot.exe help
          ServicePilot.exe version
          ServicePilot.exe ai-help
          ServicePilot.exe config-path
          ServicePilot.exe config reload
          ServicePilot.exe config apply --file PATH
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
          ServicePilot.exe step add SERVICE --name NAME --type SCRIPT_TYPE --script \"...\" [--position end|N|after:STEP|before:STEP] [--use-variable true|false] [--open-log-on-run true|false] [--variable VALUE] [--into-composite COMPOSITE]
          ServicePilot.exe step edit SERVICE STEP [--name NAME] [--type ...] [--script \"...\"] [--use-variable true|false] [--open-log-on-run true|false]
          ServicePilot.exe step remove SERVICE STEP
          ServicePilot.exe step move SERVICE STEP --position 0|first|end|N|after:STEP|before:STEP
          ServicePilot.exe step set-members SERVICE COMPOSITE --member STEP [--member STEP ...]
          ServicePilot.exe step add-member SERVICE COMPOSITE --member STEP
          ServicePilot.exe step remove-member SERVICE COMPOSITE --member STEP

          ServicePilot.exe template list|get|add|edit|remove|apply|save-from-service ...
          ServicePilot.exe template export TEMPLATE --file FILE
          ServicePilot.exe template import --file FILE [--on-conflict rename|overwrite|skip]
          ServicePilot.exe template step-variables TEMPLATE STEP [--json]
          ServicePilot.exe template step-variable-add TEMPLATE STEP --variable VALUE
          ServicePilot.exe template step-variable-remove TEMPLATE STEP --variable VALUE
          ServicePilot.exe template step-variable-clear TEMPLATE STEP
          ServicePilot.exe template step list TEMPLATE [--json]
          ServicePilot.exe template step add TEMPLATE --name NAME --type SCRIPT_TYPE --script \"...\" [--position end|N|after:STEP|before:STEP] [--use-variable true|false] [--open-log-on-run true|false] [--variable VALUE] [--into-composite COMPOSITE]
          ServicePilot.exe template step edit TEMPLATE STEP [--name NAME] [--type ...] [--script \"...\"] [--use-variable true|false] [--open-log-on-run true|false]
          ServicePilot.exe template step remove TEMPLATE STEP
          ServicePilot.exe template step move TEMPLATE STEP --position 0|first|end|N|after:STEP|before:STEP
          ServicePilot.exe template step set-members TEMPLATE COMPOSITE --member STEP [--member STEP ...]
          ServicePilot.exe template step add-member TEMPLATE COMPOSITE --member STEP
          ServicePilot.exe template step remove-member TEMPLATE COMPOSITE --member STEP
          Legacy: add, remove, templates, template create
          ServicePilot.exe shutdown

        SERVICE, STEP, and TEMPLATE can be a name or id. STEP can also be its numeric order.
        ServicePilot 2.0 uses Action steps and Composite steps. start SERVICE runs the first Composite.
        Script types for Action steps: Batch, PowerShell, Python, Node.
        --step accepts Action specs: Name|Type|command, Name|Type|UseVariable|command, or Name|Type|UseVariable|RunOnStart|OpenLogOnRun|command.
        --variable injects SERVICEPILOT_VARIABLE and replaces {{variable}} / {{变量}} in scripts.
        Start, stop, restart, step run, logs, and shutdown require the tray instance to be running.
        """;

    private static CommandResponse AiHelp() => CommandResponse.Ok(AiHelpContentService.BuildCliHelp());

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
            return CommandResponse.Ok(json);
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
        return CommandResponse.Ok(output);
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
        }

        foreach (var template in _appConfig.ServiceTemplates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var target = $"template:{DisplayName(template.Name, template.Id)}";
            if (string.IsNullOrWhiteSpace(template.Name))
                issues.Add(new DiagnosticIssue("Error", "TEMPLATE_NAME_EMPTY", target, "模板名称不能为空。"));

            ValidateSteps(issues, template.ScriptSteps, target, requiresStartupStep: false);
        }

        return issues;
    }

    private static void ValidateSteps(List<DiagnosticIssue> issues, IReadOnlyList<ScriptStep> steps, string ownerTarget, bool requiresStartupStep)
    {
        if (steps.Count == 0)
        {
            issues.Add(new DiagnosticIssue("Error", "STEPS_EMPTY", ownerTarget, "至少需要一个脚本动作。"));
            return;
        }

        var byId = steps.ToDictionary(s => s.Id);
        if (requiresStartupStep && !steps.Any(s => s.Kind == StepKind.Composite))
            issues.Add(new DiagnosticIssue("Warning", "COMPOSITE_MISSING", ownerTarget, "没有组合动作；start SERVICE 无法找到默认启动动作。"));

        var duplicateOrders = steps
            .GroupBy(s => s.Order)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateOrders.Count > 0)
            issues.Add(new DiagnosticIssue("Warning", "STEP_ORDER_DUPLICATE", ownerTarget, $"动作顺序重复: {string.Join(", ", duplicateOrders)}"));

        AddDuplicateNameIssues(issues, steps, s => s.Name, "STEP_NAME_DUPLICATE", ownerTarget, "动作名称重复。");

        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var stepTarget = $"{ownerTarget}/step:{DisplayName(step.Name, step.Id)}";
            if (string.IsNullOrWhiteSpace(step.Name))
                issues.Add(new DiagnosticIssue("Warning", "STEP_NAME_EMPTY", stepTarget, "动作名称为空，建议填写可读名称。"));
            if (step.Kind == StepKind.Action && string.IsNullOrWhiteSpace(step.Content))
                issues.Add(new DiagnosticIssue("Error", "ACTION_CONTENT_EMPTY", stepTarget, "动作命令不能为空。"));
            if (step.Kind == StepKind.Composite)
            {
                var missingIds = step.MemberStepIds.Where(id => !byId.ContainsKey(id)).ToList();
                var members = step.MemberStepIds
                    .Where(byId.ContainsKey)
                    .Select(id => byId[id])
                    .ToList();
                if (members.Count == 0 && missingIds.Count == 0)
                    issues.Add(new DiagnosticIssue("Error", "COMPOSITE_MEMBERS_EMPTY", stepTarget, "组合动作至少需要一个有效成员动作。"));
                if (missingIds.Count > 0)
                    issues.Add(new DiagnosticIssue("Error", "COMPOSITE_MEMBER_MISSING", stepTarget, $"组合动作引用了不存在的成员动作: {string.Join(", ", missingIds)}"));
                if (members.Any(m => m.Kind == StepKind.Composite))
                    issues.Add(new DiagnosticIssue("Error", "COMPOSITE_NESTED", stepTarget, "组合动作不能包含另一个组合动作。"));
                if (members.Count(m => m.UseVariable) > 1)
                    issues.Add(new DiagnosticIssue("Warning", "COMPOSITE_VARIABLE_MEMBER_MULTIPLE", stepTarget, "组合动作中有多个使用变量的成员动作；编辑保存时需要收敛为最多一个。"));
            }
            if (step.Kind == StepKind.Action && step.UseVariable)
                AddDuplicateVariablesIssue(issues, step.StepVariables, stepTarget, "STEP_VARIABLE_DUPLICATE", "动作变量重复。");
            if (step.Kind == StepKind.Action && step.UseVariable && step.StepVariables.Count == 0)
                issues.Add(new DiagnosticIssue("Warning", "STEP_USEVARIABLE_NO_VARS", stepTarget, "启用了变量但变量列表为空，建议添加变量或关闭 UseVariable。"));
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
            return Json(services.Select(s =>
            {
                var firstComposite = s.ScriptSteps
                    .OrderBy(step => step.Order)
                    .FirstOrDefault(step => step.Kind == StepKind.Composite);
                return new
                {
                    s.Id,
                    s.Name,
                    s.WorkingDirectory,
                    s.AutoStart,
                    s.SortOrder,
                    StepCount = s.ScriptSteps.Count,
                    ActionCount = s.ScriptSteps.Count(step => step.Kind == StepKind.Action),
                    CompositeCount = s.ScriptSteps.Count(step => step.Kind == StepKind.Composite),
                    DefaultStartStep = firstComposite != null ? new { firstComposite.Id, firstComposite.Name } : null
                };
            }));

        if (services.Count == 0)
            return CommandResponse.Ok("没有配置服务。");

        var lines = services.Select(s =>
            $"{s.SortOrder}. {s.Name} ({s.Id}) actions={s.ScriptSteps.Count(step => step.Kind == StepKind.Action)} composites={s.ScriptSteps.Count(step => step.Kind == StepKind.Composite)} autostart={s.AutoStart} dir=\"{s.WorkingDirectory}\"");
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
        {
            var firstComposite = service.ScriptSteps
                .OrderBy(s => s.Order)
                .FirstOrDefault(s => s.Kind == StepKind.Composite);
            return Json(new
            {
                service.Id,
                service.Name,
                service.WorkingDirectory,
                service.AutoStart,
                ScriptSteps = service.ScriptSteps.OrderBy(s => s.Order).Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Kind,
                    s.ScriptType,
                    s.UseVariable,
                    s.OpenLogOnRun,
                    s.StepVariables,
                    s.MemberStepIds,
                    s.Order,
                    s.Content,
                    IsDefaultStartStep = s.Kind == StepKind.Composite && firstComposite != null && s.Id == firstComposite.Id
                })
            });
        }

        var firstCompositeId = service.ScriptSteps
            .OrderBy(s => s.Order)
            .FirstOrDefault(s => s.Kind == StepKind.Composite)?.Id;
        var steps = service.ScriptSteps
            .OrderBy(s => s.Order)
            .Select(s =>
            {
                var members = s.Kind == StepKind.Composite && s.MemberStepIds.Count > 0
                    ? $" members={string.Join(",", s.MemberStepIds)}"
                    : string.Empty;
                var defaultStart = s.Kind == StepKind.Composite && s.Id == firstCompositeId
                    ? " (default start)"
                    : string.Empty;
                return $"{s.Name} kind={s.Kind} type={s.ScriptType} useVariable={s.UseVariable} openLogOnRun={s.OpenLogOnRun} stepVariables={s.StepVariables.Count}{members}{defaultStart}";
            });
        return CommandResponse.Ok(
            $"{service.Name} ({service.Id})\ndir=\"{service.WorkingDirectory}\"\nautostart={service.AutoStart}\nactions={service.ScriptSteps.Count(s => s.Kind == StepKind.Action)} composites={service.ScriptSteps.Count(s => s.Kind == StepKind.Composite)}\nsteps:\n{string.Join(Environment.NewLine, steps)}");
    }

    private async Task<CommandResponse> ServiceEditAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("缺少服务名或 id。用法: service edit SERVICE [--name NAME] [--dir DIR] [--step ...] [--preset VALUE]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        bool changed = false;
        var newName = ReadOption(args, "--name");
        if (!string.IsNullOrWhiteSpace(newName))
        {
            var duplicate = _appConfig.Services.Any(s => s.Id != service.Id &&
                                                         string.Equals(s.Name, newName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (duplicate)
                return CommandResponse.Error($"服务名称已存在: {newName.Trim()}", 2);

            updated.Name = newName.Trim();
            changed = true;
        }

        var dir = ReadOption(args, "--dir") ?? ReadOption(args, "--working-directory");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            if (!Directory.Exists(dir))
                return CommandResponse.Error($"工作目录不存在: {dir}", 2);

            updated.WorkingDirectory = dir.Trim();
            changed = true;
        }

        var autoStart = ReadBoolOption(args, "--autostart");
        if (autoStart.HasValue)
        {
            updated.AutoStart = autoStart.Value;
            changed = true;
        }

        var steps = ParseSteps(args);
        if (steps.Count > 0)
        {
            updated.ScriptSteps = steps;
            changed = true;
        }

        if (HasAnyOption(args, "--preset", "--preset-variable") || HasFlag(args, "--clear-presets"))
            return CommandResponse.Error("ServicePilot 2.0 已移除服务级预设变量；请使用 step variable-add 维护动作变量。", 2);

        await UpdateConfigAsync(updated);
        return changed
            ? CommandResponse.Ok($"已更新服务: {updated.Name}")
            : CommandResponse.Ok("未检测到变更，服务保持不变");
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

        if (HasAnyOption(args, "--preset", "--preset-variable") || HasFlag(args, "--clear-presets"))
            return CommandResponse.Error("ServicePilot 2.0 已移除服务级预设变量；请使用 step variable-add 维护动作变量。", 2);

        var steps = ParseSteps(args);
        if (steps.Count == 0)
            return CommandResponse.Error("缺少脚本动作。使用 --step \"Name|Batch|command\" 或 --content。", 2);

        if (FindConfig(name.Trim()) != null)
            return CommandResponse.Error($"服务名称已存在: {name.Trim()}", 2);

        var config = new ServiceConfig
        {
            Name = name.Trim(),
            WorkingDirectory = dir.Trim(),
            AutoStart = HasFlag(args, "--autostart"),
            ScriptSteps = EnsureDefaultComposite(steps),
            PresetVariables = []
        };

        await AddConfigAsync(config);
        return CommandResponse.Ok($"已新增服务: {config.Name} ({config.Id})");
    }

    private async Task<CommandResponse> StepAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: step list|add|edit|remove|move|run|variables|variable-add|variable-remove|variable-clear|set-members|add-member|remove-member SERVICE ... [--json]等", 2);

        var subCommand = args[0].ToLowerInvariant();
        return subCommand switch
        {
            "list" or "ls" => StepList(args.Skip(1).ToArray()),
            "run" => StepRun(args.Skip(1).ToArray()),
            "variables" or "variable-list" or "vars" => StepVariables(args.Skip(1).ToArray()),
            "variable-add" or "var-add" => await StepVariableAddAsync(args.Skip(1).ToArray()),
            "variable-remove" or "variable-delete" or "var-remove" or "var-delete" => await StepVariableRemoveAsync(args.Skip(1).ToArray()),
            "variable-clear" or "var-clear" => await StepVariableClearAsync(args.Skip(1).ToArray()),
            "add" => await StepAddAsync(args.Skip(1).ToArray()),
            "edit" => await StepEditAsync(args.Skip(1).ToArray()),
            "remove" or "delete" => await StepRemoveAsync(args.Skip(1).ToArray()),
            "move" => await StepMoveAsync(args.Skip(1).ToArray()),
            "set-members" => await StepSetMembersAsync(args.Skip(1).ToArray()),
            "add-member" => await StepAddMemberAsync(args.Skip(1).ToArray()),
            "remove-member" => await StepRemoveMemberAsync(args.Skip(1).ToArray()),
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
                step.Kind,
                step.ScriptType,
                step.UseVariable,
                step.OpenLogOnRun,
                step.StepVariables,
                step.MemberStepIds,
                step.Order,
                HasContent = !string.IsNullOrWhiteSpace(step.Content),
                Runtime = runtime != null && runtime.StepStates.TryGetValue(step.Id, out var state) ? state : null
            }));

        return steps.Count == 0
            ? CommandResponse.Ok($"服务没有动作: {service.Name}")
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
            return CommandResponse.Error($"找不到动作: {args[1]}", 2);
        if (step.Kind == StepKind.Composite)
        {
            var variableMember = ScriptDefinitionService.FindVariableMember(service.Config, step);
            var compositeVariable = ReadVariable(args);
            RememberService(service.Config.Id);
            if (variableMember != null)
                RememberVariableForStep(service.Config, variableMember, compositeVariable);
            return _processManager.RunComposite(service.Config.Id, step.Id, compositeVariable)
                ? CommandResponse.Ok($"已发送组合动作命令: {service.Config.Name} / {step.Name}")
                : CommandResponse.Error($"组合动作执行失败: {service.Config.Name} / {step.Name}", 2);
        }

        if (string.IsNullOrWhiteSpace(step.Content))
            return CommandResponse.Error($"动作没有脚本内容，无法执行: {step.Name}", 2);

        var variable = ReadVariable(args);
        RememberService(service.Config.Id);
        if (step.UseVariable)
            RememberVariableForStep(service.Config, step, variable);
        return _processManager.RunStep(service.Config.Id, step.Id, variable)
            ? CommandResponse.Ok($"已发送执行动作命令: {service.Config.Name} / {step.Name}")
            : CommandResponse.Error($"执行动作失败: {service.Config.Name} / {step.Name}", 2);
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
            return CommandResponse.Error($"找不到动作: {args[1]}", 2);

        var variables = step.StepVariables;
        if (HasFlag(args, "--json"))
            return Json(new
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                StepId = step.Id,
                StepName = step.Name,
                step.Kind,
                VariableScope = "step",
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

        return result ?? CommandResponse.Ok($"已新增动作变量: {variable.Trim()}");
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

        return result ?? CommandResponse.Ok($"已删除动作变量: {variable.Trim()}");
    }

    private async Task<CommandResponse> StepVariableClearAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step variable-clear SERVICE STEP", 2);

        var result = await UpdateStepVariablesAsync(args[0], args[1], variables => variables.Clear());
        return result ?? CommandResponse.Ok("已清空动作变量。");
    }

    #region Step add / edit / remove / move

    private async Task<CommandResponse> StepAddAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: step add SERVICE --name NAME --type Batch|PowerShell|Python|Node --script \"...\" [--use-variable true|false] [--run-on-start true|false] [--open-log-on-run true|false] [--variable VALUE]... [--position end|N|after:STEP|before:STEP] [--into-composite COMPOSITE]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var name = ReadOption(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
            return CommandResponse.Error("缺少 --name。", 2);

        // Reject duplicate step names within the same service
        if (service.ScriptSteps.Any(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return CommandResponse.Error($"服务 \"{service.Name}\" 已存在同名动作: {name.Trim()}（请使用不同名称，或用 GUID 定位后 step edit/step remove）", 2);

        var script = ReadOption(args, "--script") ?? ReadOption(args, "--script-file");
        if (string.IsNullOrWhiteSpace(script))
            return CommandResponse.Error("缺少 --script 或 --script-file。", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var type = ReadScriptType(args);
        var useVariable = ReadBoolOption(args, "--use-variable") ?? false;
        var runOnStart = ReadBoolOption(args, "--run-on-start") ?? false;
        var openLogOnRun = ReadBoolOption(args, "--open-log-on-run") ?? false;

        var newStep = new ScriptStep
        {
            Name = name.Trim(),
            Kind = StepKind.Action,
            ScriptType = type,
            UseVariable = useVariable,
            OpenLogOnRun = openLogOnRun,
            Content = script,
            StepVariables = ReadOptions(args, "--variable").Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        // Apply position
        var position = ReadOption(args, "--position") ?? "end";
        var intoComposite = ReadOption(args, "--into-composite");
        InsertStepAtPosition(updated.ScriptSteps, newStep, position);
        ReorderSteps(updated.ScriptSteps);

        // Add to composite member list if requested
        if (!string.IsNullOrWhiteSpace(intoComposite))
        {
            var composite = FindStep(updated, intoComposite);
            if (composite == null)
                return CommandResponse.Error($"找不到组合动作: {intoComposite}", 2);
            if (composite.Kind != StepKind.Composite)
                return CommandResponse.Error($"目标动作不是组合动作: {intoComposite}", 2);
            composite.MemberStepIds.Add(newStep.Id);
        }

        await UpdateConfigAsync(updated);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                StepId = newStep.Id,
                StepName = newStep.Name,
                newStep.Kind,
                newStep.ScriptType,
                newStep.UseVariable,
                newStep.OpenLogOnRun,
                newStep.Order,
                newStep.StepVariables,
                IntoComposite = !string.IsNullOrWhiteSpace(intoComposite) ? intoComposite : null
            });
        return CommandResponse.Ok($"已新增动作: {newStep.Name} ({newStep.Id})");
    }

    private async Task<CommandResponse> StepEditAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step edit SERVICE STEP [--name NAME] [--type ...] [--script ...|--script-file FILE] [--use-variable true|false] [--run-on-start true|false] [--open-log-on-run true|false]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var step = FindStep(updated, args[1]);
        if (step == null)
            return HasAmbiguousNameMatch(updated.ScriptSteps, args[1])
                ? CommandResponse.Error($"动作名称 \"{args[1]}\" 匹配到多个，请使用 GUID 定位", 2)
                : CommandResponse.Error($"找不到动作: {args[1]}", 2);

        bool changed = false;
        var newName = ReadOption(args, "--name");
        if (!string.IsNullOrWhiteSpace(newName))
        {
            step.Name = newName.Trim();
            changed = true;
        }

        var typeText = ReadOption(args, "--type");
        if (!string.IsNullOrWhiteSpace(typeText) && Enum.TryParse<ScriptType>(typeText, ignoreCase: true, out var parsedType))
        {
            step.ScriptType = parsedType;
            changed = true;
        }

        var script = ReadOption(args, "--script") ?? ReadOption(args, "--script-file");
        if (!string.IsNullOrWhiteSpace(script))
        {
            step.Content = script;
            changed = true;
        }

        var useVariable = ReadBoolOption(args, "--use-variable");
        if (useVariable.HasValue)
        {
            step.UseVariable = useVariable.Value;
            changed = true;
        }

        var runOnStart = ReadBoolOption(args, "--run-on-start");
        if (runOnStart.HasValue)
        {
            step.RunOnStart = runOnStart.Value;
            changed = true;
        }

        var openLogOnRun = ReadBoolOption(args, "--open-log-on-run");
        if (openLogOnRun.HasValue)
        {
            step.OpenLogOnRun = openLogOnRun.Value;
            changed = true;
        }

        await UpdateConfigAsync(updated);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                StepId = step.Id,
                StepName = step.Name,
                step.Kind,
                step.ScriptType,
                step.UseVariable,
                step.OpenLogOnRun,
                step.Order,
                step.StepVariables
            });
        return changed
            ? CommandResponse.Ok($"已更新动作: {step.Name} ({step.Id}) UseVariable={step.UseVariable} Variables={step.StepVariables.Count} Type={step.ScriptType}")
            : CommandResponse.Ok("未检测到变更，动作保持不变");
    }

    private async Task<CommandResponse> StepRemoveAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step remove SERVICE STEP", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var step = FindStep(updated, args[1]);
        if (step == null)
            return HasAmbiguousNameMatch(updated.ScriptSteps, args[1])
                ? CommandResponse.Error($"动作名称 \"{args[1]}\" 匹配到多个，请使用 GUID 定位", 2)
                : CommandResponse.Error($"找不到动作: {args[1]}", 2);

        // Remove from ScriptSteps
        updated.ScriptSteps.RemoveAll(s => s.Id == step.Id);

        // Remove from all composite MemberStepIds
        foreach (var composite in updated.ScriptSteps.Where(s => s.Kind == StepKind.Composite))
            composite.MemberStepIds.RemoveAll(id => id == step.Id);

        ReorderSteps(updated.ScriptSteps);
        await UpdateConfigAsync(updated);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                StepId = step.Id,
                StepName = step.Name,
                Removed = true
            });
        return CommandResponse.Ok($"已删除动作: {step.Name} ({step.Id})");
    }

    private async Task<CommandResponse> StepMoveAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step move SERVICE STEP --position 0|first|end|N|after:STEP|before:STEP", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var position = ReadOption(args, "--position");
        if (string.IsNullOrWhiteSpace(position))
            return CommandResponse.Error("缺少 --position。用法: --position 0|first|end|N|after:STEP|before:STEP", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var step = FindStep(updated, args[1]);
        if (step == null)
            return CommandResponse.Error($"找不到动作: {args[1]}", 2);

        // Remove step from current position (keep a reference)
        updated.ScriptSteps.RemoveAll(s => s.Id == step.Id);

        // Re-insert at new position
        InsertStepAtPosition(updated.ScriptSteps, step, position);
        ReorderSteps(updated.ScriptSteps);

        await UpdateConfigAsync(updated);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                StepId = step.Id,
                StepName = step.Name,
                Position = position,
                step.Order
            });
        return CommandResponse.Ok($"已移动动作: {step.Name} 到位置 {position}");
    }

    private async Task<CommandResponse> StepSetMembersAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step set-members SERVICE COMPOSITE --member STEP [--member STEP ...]", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var composite = FindStep(updated.ScriptSteps, args[1]);
        if (composite == null)
            return CommandResponse.Error($"找不到动作: {args[1]}", 2);
        if (composite.Kind != StepKind.Composite)
            return CommandResponse.Error($"动作 \"{composite.Name}\" 不是组合动作，无法设置成员。", 2);

        var memberNames = ReadOptions(args, "--member");
        if (memberNames.Count == 0)
            return CommandResponse.Error("缺少 --member STEP。至少指定一个成员动作。", 2);

        var oldMembers = composite.MemberStepIds.Select(id => updated.ScriptSteps.FirstOrDefault(s => s.Id == id)).Where(s => s != null).Select(s => s!.Name).ToList();
        var newMemberIds = new List<Guid>();
        var resolvedNames = new List<string>();
        foreach (var name in memberNames)
        {
            var member = FindStep(updated.ScriptSteps, name);
            if (member == null)
                return CommandResponse.Error($"找不到成员动作: {name}", 2);
            if (member.Kind != StepKind.Action)
                return CommandResponse.Error($"成员动作 \"{member.Name}\" 不是可执行动作（类型为 {member.Kind}），组合动作只能包含可执行动作。", 2);
            if (member.Id == composite.Id)
                return CommandResponse.Error("组合动作不能包含自身。", 2);
            newMemberIds.Add(member.Id);
            resolvedNames.Add(member.Name);
        }

        composite.MemberStepIds = newMemberIds;
        await UpdateConfigAsync(updated);
        return CommandResponse.Ok($"已设置组合动作成员: {composite.Name} [{string.Join(", ", oldMembers)}] → [{string.Join(", ", resolvedNames)}]");
    }

    private async Task<CommandResponse> StepAddMemberAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step add-member SERVICE COMPOSITE --member STEP", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var composite = FindStep(updated.ScriptSteps, args[1]);
        if (composite == null)
            return CommandResponse.Error($"找不到动作: {args[1]}", 2);
        if (composite.Kind != StepKind.Composite)
            return CommandResponse.Error($"动作 \"{composite.Name}\" 不是组合动作，无法添加成员。", 2);

        var memberName = ReadOption(args, "--member");
        if (string.IsNullOrWhiteSpace(memberName))
            return CommandResponse.Error("缺少 --member STEP。", 2);

        var member = FindStep(updated.ScriptSteps, memberName);
        if (member == null)
            return CommandResponse.Error($"找不到成员动作: {memberName}", 2);
        if (member.Kind != StepKind.Action)
            return CommandResponse.Error($"成员动作 \"{member.Name}\" 不是可执行动作。", 2);
        if (member.Id == composite.Id)
            return CommandResponse.Error("组合动作不能包含自身。", 2);
        if (composite.MemberStepIds.Contains(member.Id))
            return CommandResponse.Error($"成员动作 \"{member.Name}\" 已在组合动作 \"{composite.Name}\" 中。", 2);

        composite.MemberStepIds = composite.MemberStepIds.Append(member.Id).ToList();
        await UpdateConfigAsync(updated);
        return CommandResponse.Ok($"已添加组合动作成员: {composite.Name} + {member.Name}");
    }

    private async Task<CommandResponse> StepRemoveMemberAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: step remove-member SERVICE COMPOSITE --member STEP", 2);

        var service = FindConfig(args[0]);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {args[0]}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var composite = FindStep(updated.ScriptSteps, args[1]);
        if (composite == null)
            return CommandResponse.Error($"找不到动作: {args[1]}", 2);
        if (composite.Kind != StepKind.Composite)
            return CommandResponse.Error($"动作 \"{composite.Name}\" 不是组合动作，无法移除成员。", 2);

        var memberName = ReadOption(args, "--member");
        if (string.IsNullOrWhiteSpace(memberName))
            return CommandResponse.Error("缺少 --member STEP。", 2);

        var member = FindStep(updated.ScriptSteps, memberName);
        if (member == null)
            return CommandResponse.Error($"找不到成员动作: {memberName}", 2);
        if (!composite.MemberStepIds.Contains(member.Id))
            return CommandResponse.Error($"成员动作 \"{member.Name}\" 不在组合动作 \"{composite.Name}\" 中。", 2);

        composite.MemberStepIds = composite.MemberStepIds.Where(id => id != member.Id).ToList();
        await UpdateConfigAsync(updated);
        return CommandResponse.Ok($"已移除组合动作成员: {composite.Name} - {member.Name}");
    }

    private static void InsertStepAtPosition(List<ScriptStep> steps, ScriptStep step, string position)
    {
        if (string.Equals(position, "end", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(step);
            return;
        }

        if (string.Equals(position, "first", StringComparison.OrdinalIgnoreCase))
        {
            steps.Insert(0, step);
            return;
        }

        if (int.TryParse(position, out var n))
        {
            var clamped = Math.Clamp(n, 0, steps.Count);
            steps.Insert(clamped, step);
            return;
        }

        if (position.StartsWith("after:", StringComparison.OrdinalIgnoreCase))
        {
            var target = position["after:".Length..].Trim();
            var targetStep = FindStepInList(steps, target);
            if (targetStep != null)
            {
                var index = steps.FindIndex(s => s.Id == targetStep.Id);
                steps.Insert(index + 1, step);
                return;
            }
        }

        if (position.StartsWith("before:", StringComparison.OrdinalIgnoreCase))
        {
            var target = position["before:".Length..].Trim();
            var targetStep = FindStepInList(steps, target);
            if (targetStep != null)
            {
                var index = steps.FindIndex(s => s.Id == targetStep.Id);
                steps.Insert(Math.Max(0, index), step);
                return;
            }
        }

        // Fallback: append
        steps.Add(step);
    }

    private static void ReorderSteps(List<ScriptStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
            steps[i].Order = i;
    }

    private static ScriptStep? FindStepInList(List<ScriptStep> steps, string selector)
    {
        if (Guid.TryParse(selector, out var id))
            return steps.FirstOrDefault(s => s.Id == id);
        return steps.FirstOrDefault(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    private async Task<CommandResponse?> UpdateStepVariablesAsync(string serviceSelector, string stepSelector, Action<List<string>> update)
    {
        var service = FindConfig(serviceSelector);
        if (service == null)
            return CommandResponse.Error($"找不到服务: {serviceSelector}", 2);

        var updated = ScriptDefinitionService.CloneService(service);
        var step = FindStep(updated, stepSelector);
        if (step == null)
            return CommandResponse.Error($"找不到动作: {stepSelector}", 2);

        if (step.Kind != StepKind.Action)
            return CommandResponse.Error("组合动作不直接维护变量；请维护其成员动作的变量。", 2);

        var variables = step.StepVariables;
        update(variables);
        await UpdateConfigAsync(updated);
        return null;
    }

    private CommandResponse Templates(string[] args)
    {
        if (HasFlag(args, "--json"))
            return Json(_appConfig.ServiceTemplates.Select(t =>
            {
                var firstComposite = t.ScriptSteps
                    .OrderBy(step => step.Order)
                    .FirstOrDefault(step => step.Kind == StepKind.Composite);
                return new
                {
                    t.Id,
                    t.Name,
                    t.Description,
                    t.CreatedAt,
                    t.UpdatedAt,
                    StepCount = t.ScriptSteps.Count,
                    ActionCount = t.ScriptSteps.Count(step => step.Kind == StepKind.Action),
                    CompositeCount = t.ScriptSteps.Count(step => step.Kind == StepKind.Composite),
                    DefaultStartStep = firstComposite != null ? new { firstComposite.Id, firstComposite.Name } : null
                };
            }));

        if (_appConfig.ServiceTemplates.Count == 0)
            return CommandResponse.Ok("没有服务模板。");

        var lines = _appConfig.ServiceTemplates
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => $"{t.Name} ({t.Id}) actions={t.ScriptSteps.Count(step => step.Kind == StepKind.Action)} composites={t.ScriptSteps.Count(step => step.Kind == StepKind.Composite)} - {t.Description}");
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
                "step" or "steps" => await TemplateStepAsync(rest),
                _ => CommandResponse.Error("未知模板命令。用法: template list|get|add|edit|remove|apply|save-from-service|export|import|step|step-variables|step-variable-add|step-variable-remove|step-variable-clear", 2)
            };
        }

        return await TemplateCreateAsync(args.Skip(1).ToArray());
    }

    private async Task<CommandResponse> TemplateStepAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: template step TEMPLATE add|edit|remove|move|list|set-members|add-member|remove-member ...", 2);

        var subCommand = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return subCommand switch
        {
            "list" or "ls" => TemplateStepList(rest),
            "add" => await TemplateStepAddAsync(rest),
            "edit" => await TemplateStepEditAsync(rest),
            "remove" or "delete" => await TemplateStepRemoveAsync(rest),
            "move" => await TemplateStepMoveAsync(rest),
            "set-members" => await TemplateStepSetMembersAsync(rest),
            "add-member" => await TemplateStepAddMemberAsync(rest),
            "remove-member" => await TemplateStepRemoveMemberAsync(rest),
            _ => CommandResponse.Error($"未知 template step 命令: {args[0]}", 2)
        };
    }

    #region Template step add / edit / remove / move

    private CommandResponse TemplateStepList(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: template step list TEMPLATE [--json]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var steps = template.ScriptSteps.OrderBy(s => s.Order).ToList();
        if (HasFlag(args, "--json"))
            return Json(steps);

        return steps.Count == 0
            ? CommandResponse.Ok($"模板没有动作: {template.Name}")
            : CommandResponse.Ok(string.Join(Environment.NewLine, FormatStepList(steps)));
    }

    private async Task<CommandResponse> TemplateStepAddAsync(string[] args)
    {
        if (args.Length == 0)
            return CommandResponse.Error("用法: template step add TEMPLATE --name NAME --type ... --script ... [--use-variable ...] [--position ...] [--into-composite ...]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var name = ReadOption(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
            return CommandResponse.Error("缺少 --name。", 2);

        // Reject duplicate step names within the same template
        if (template.ScriptSteps.Any(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return CommandResponse.Error($"模板 \"{template.Name}\" 已存在同名动作: {name.Trim()}（请使用不同名称，或用 GUID 定位后 template step edit/remove）", 2);

        var script = ReadOption(args, "--script") ?? ReadOption(args, "--script-file");
        if (string.IsNullOrWhiteSpace(script))
            return CommandResponse.Error("缺少 --script 或 --script-file。", 2);

        var type = ReadScriptType(args);
        var useVariable = ReadBoolOption(args, "--use-variable") ?? false;
        var openLogOnRun = ReadBoolOption(args, "--open-log-on-run") ?? false;

        var newStep = new ScriptStep
        {
            Name = name.Trim(),
            Kind = StepKind.Action,
            ScriptType = type,
            UseVariable = useVariable,
            OpenLogOnRun = openLogOnRun,
            Content = script,
            StepVariables = ReadOptions(args, "--variable").Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        var position = ReadOption(args, "--position") ?? "end";
        var intoComposite = ReadOption(args, "--into-composite");
        InsertStepAtPosition(template.ScriptSteps, newStep, position);
        ReorderSteps(template.ScriptSteps);

        if (!string.IsNullOrWhiteSpace(intoComposite))
        {
            var composite = FindStep(template.ScriptSteps, intoComposite);
            if (composite == null)
                return CommandResponse.Error($"找不到组合动作: {intoComposite}", 2);
            if (composite.Kind != StepKind.Composite)
                return CommandResponse.Error($"目标动作不是组合动作: {intoComposite}", 2);
            composite.MemberStepIds.Add(newStep.Id);
        }

        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                StepId = newStep.Id,
                StepName = newStep.Name,
                newStep.Kind,
                newStep.ScriptType,
                newStep.UseVariable,
                newStep.OpenLogOnRun,
                newStep.Order,
                newStep.StepVariables,
                IntoComposite = !string.IsNullOrWhiteSpace(intoComposite) ? intoComposite : null
            });
        return CommandResponse.Ok($"已新增模板动作: {newStep.Name} ({newStep.Id})");
    }

    private async Task<CommandResponse> TemplateStepEditAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step edit TEMPLATE STEP [--name NAME] [--type ...] [--script ...] [--use-variable ...] [--open-log-on-run ...]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var step = FindStep(template.ScriptSteps, args[1]);
        if (step == null)
            return HasAmbiguousNameMatch(template.ScriptSteps, args[1])
                ? CommandResponse.Error($"模板动作名称 \"{args[1]}\" 匹配到多个，请使用 GUID 定位", 2)
                : CommandResponse.Error($"找不到模板动作: {args[1]}", 2);

        bool changed = false;
        var newName = ReadOption(args, "--name");
        if (!string.IsNullOrWhiteSpace(newName))
        {
            step.Name = newName.Trim();
            changed = true;
        }

        var typeText = ReadOption(args, "--type");
        if (!string.IsNullOrWhiteSpace(typeText) && Enum.TryParse<ScriptType>(typeText, ignoreCase: true, out var parsedType))
        {
            step.ScriptType = parsedType;
            changed = true;
        }

        var script = ReadOption(args, "--script") ?? ReadOption(args, "--script-file");
        if (!string.IsNullOrWhiteSpace(script))
        {
            step.Content = script;
            changed = true;
        }

        var useVariable = ReadBoolOption(args, "--use-variable");
        if (useVariable.HasValue)
        {
            step.UseVariable = useVariable.Value;
            changed = true;
        }

        var openLogOnRun = ReadBoolOption(args, "--open-log-on-run");
        if (openLogOnRun.HasValue)
        {
            step.OpenLogOnRun = openLogOnRun.Value;
            changed = true;
        }

        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                StepId = step.Id,
                StepName = step.Name,
                step.Kind,
                step.ScriptType,
                step.UseVariable,
                step.OpenLogOnRun,
                step.Order,
                step.StepVariables
            });
        return changed
            ? CommandResponse.Ok($"已更新模板动作: {step.Name} ({step.Id}) UseVariable={step.UseVariable} Variables={step.StepVariables.Count} Type={step.ScriptType}")
            : CommandResponse.Ok("未检测到变更，动作保持不变");
    }

    private async Task<CommandResponse> TemplateStepRemoveAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step remove TEMPLATE STEP", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var step = FindStep(template.ScriptSteps, args[1]);
        if (step == null)
            return HasAmbiguousNameMatch(template.ScriptSteps, args[1])
                ? CommandResponse.Error($"模板动作名称 \"{args[1]}\" 匹配到多个，请使用 GUID 定位", 2)
                : CommandResponse.Error($"找不到模板动作: {args[1]}", 2);

        template.ScriptSteps.RemoveAll(s => s.Id == step.Id);
        foreach (var composite in template.ScriptSteps.Where(s => s.Kind == StepKind.Composite))
            composite.MemberStepIds.RemoveAll(id => id == step.Id);

        ReorderSteps(template.ScriptSteps);
        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                StepId = step.Id,
                StepName = step.Name,
                Removed = true
            });
        return CommandResponse.Ok($"已删除模板动作: {step.Name} ({step.Id})");
    }

    private async Task<CommandResponse> TemplateStepMoveAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step move TEMPLATE STEP --position 0|first|end|N|after:STEP|before:STEP", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var position = ReadOption(args, "--position");
        if (string.IsNullOrWhiteSpace(position))
            return CommandResponse.Error("缺少 --position。", 2);

        var step = FindStep(template.ScriptSteps, args[1]);
        if (step == null)
            return CommandResponse.Error($"找不到模板动作: {args[1]}", 2);

        template.ScriptSteps.RemoveAll(s => s.Id == step.Id);
        InsertStepAtPosition(template.ScriptSteps, step, position);
        ReorderSteps(template.ScriptSteps);

        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        if (HasFlag(args, "--json"))
            return Json(new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                StepId = step.Id,
                StepName = step.Name,
                Position = position,
                step.Order
            });
        return CommandResponse.Ok($"已移动模板动作: {step.Name} 到位置 {position}");
    }

    private async Task<CommandResponse> TemplateStepSetMembersAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step set-members TEMPLATE COMPOSITE --member STEP [--member STEP ...]", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var composite = FindStep(template.ScriptSteps, args[1]);
        if (composite == null)
            return CommandResponse.Error($"找不到模板动作: {args[1]}", 2);
        if (composite.Kind != StepKind.Composite)
            return CommandResponse.Error($"动作 \"{composite.Name}\" 不是组合动作，无法设置成员。", 2);

        var memberNames = ReadOptions(args, "--member");
        if (memberNames.Count == 0)
            return CommandResponse.Error("缺少 --member STEP。至少指定一个成员动作。", 2);

        var oldMembers = composite.MemberStepIds.Select(id => template.ScriptSteps.FirstOrDefault(s => s.Id == id)).Where(s => s != null).Select(s => s!.Name).ToList();
        var newMemberIds = new List<Guid>();
        var resolvedNames = new List<string>();
        foreach (var name in memberNames)
        {
            var member = FindStep(template.ScriptSteps, name);
            if (member == null)
                return CommandResponse.Error($"找不到成员动作: {name}", 2);
            if (member.Kind != StepKind.Action)
                return CommandResponse.Error($"成员动作 \"{member.Name}\" 不是可执行动作，组合动作只能包含可执行动作。", 2);
            if (member.Id == composite.Id)
                return CommandResponse.Error("组合动作不能包含自身。", 2);
            newMemberIds.Add(member.Id);
            resolvedNames.Add(member.Name);
        }

        composite.MemberStepIds = newMemberIds;
        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已设置组合动作成员: {composite.Name} [{string.Join(", ", oldMembers)}] → [{string.Join(", ", resolvedNames)}]");
    }

    private async Task<CommandResponse> TemplateStepAddMemberAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step add-member TEMPLATE COMPOSITE --member STEP", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var composite = FindStep(template.ScriptSteps, args[1]);
        if (composite == null)
            return CommandResponse.Error($"找不到模板动作: {args[1]}", 2);
        if (composite.Kind != StepKind.Composite)
            return CommandResponse.Error($"动作 \"{composite.Name}\" 不是组合动作，无法添加成员。", 2);

        var memberName = ReadOption(args, "--member");
        if (string.IsNullOrWhiteSpace(memberName))
            return CommandResponse.Error("缺少 --member STEP。", 2);

        var member = FindStep(template.ScriptSteps, memberName);
        if (member == null)
            return CommandResponse.Error($"找不到成员动作: {memberName}", 2);
        if (member.Kind != StepKind.Action)
            return CommandResponse.Error($"成员动作 \"{member.Name}\" 不是可执行动作。", 2);
        if (member.Id == composite.Id)
            return CommandResponse.Error("组合动作不能包含自身。", 2);
        if (composite.MemberStepIds.Contains(member.Id))
            return CommandResponse.Error($"成员动作 \"{member.Name}\" 已在组合动作 \"{composite.Name}\" 中。", 2);

        composite.MemberStepIds = composite.MemberStepIds.Append(member.Id).ToList();
        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已添加组合动作成员: {composite.Name} + {member.Name}");
    }

    private async Task<CommandResponse> TemplateStepRemoveMemberAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step remove-member TEMPLATE COMPOSITE --member STEP", 2);

        var template = FindTemplate(args[0]);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {args[0]}", 2);

        var composite = FindStep(template.ScriptSteps, args[1]);
        if (composite == null)
            return CommandResponse.Error($"找不到模板动作: {args[1]}", 2);
        if (composite.Kind != StepKind.Composite)
            return CommandResponse.Error($"动作 \"{composite.Name}\" 不是组合动作，无法移除成员。", 2);

        var memberName = ReadOption(args, "--member");
        if (string.IsNullOrWhiteSpace(memberName))
            return CommandResponse.Error("缺少 --member STEP。", 2);

        var member = FindStep(template.ScriptSteps, memberName);
        if (member == null)
            return CommandResponse.Error($"找不到成员动作: {memberName}", 2);
        if (!composite.MemberStepIds.Contains(member.Id))
            return CommandResponse.Error($"成员动作 \"{member.Name}\" 不在组合动作 \"{composite.Name}\" 中。", 2);

        composite.MemberStepIds = composite.MemberStepIds.Where(id => id != member.Id).ToList();
        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        return CommandResponse.Ok($"已移除组合动作成员: {composite.Name} - {member.Name}");
    }

    #endregion

    // template create handler
    private async Task<CommandResponse> TemplateCreateAsync(string[] args)
    {
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
        {
            var firstComposite = template.ScriptSteps
                .OrderBy(s => s.Order)
                .FirstOrDefault(s => s.Kind == StepKind.Composite);
            return Json(new
            {
                template.Id,
                template.Name,
                template.Description,
                template.CreatedAt,
                template.UpdatedAt,
                template.PresetVariables,
                ScriptSteps = template.ScriptSteps.OrderBy(s => s.Order).Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Kind,
                    s.ScriptType,
                    s.UseVariable,
                    s.OpenLogOnRun,
                    s.StepVariables,
                    s.MemberStepIds,
                    s.Order,
                    s.Content,
                    IsDefaultStartStep = s.Kind == StepKind.Composite && firstComposite != null && s.Id == firstComposite.Id
                })
            });
        }

        var firstCompositeId = template.ScriptSteps
            .OrderBy(s => s.Order)
            .FirstOrDefault(s => s.Kind == StepKind.Composite)?.Id;
        var steps = template.ScriptSteps
            .OrderBy(s => s.Order)
            .Select(s =>
            {
                var members = s.Kind == StepKind.Composite && s.MemberStepIds.Count > 0
                    ? $" members={string.Join(",", s.MemberStepIds)}"
                    : string.Empty;
                var defaultStart = s.Kind == StepKind.Composite && s.Id == firstCompositeId
                    ? " (default start)"
                    : string.Empty;
                return $"{s.Name} kind={s.Kind} type={s.ScriptType} useVariable={s.UseVariable} openLogOnRun={s.OpenLogOnRun} stepVariables={s.StepVariables.Count}{members}{defaultStart}";
            });
        return CommandResponse.Ok($"{template.Name} ({template.Id})\n{template.Description}\nactions={template.ScriptSteps.Count(s => s.Kind == StepKind.Action)} composites={template.ScriptSteps.Count(s => s.Kind == StepKind.Composite)}\nsteps:\n{string.Join(Environment.NewLine, steps)}");
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
            return CommandResponse.Error("缺少脚本动作。使用 --step \"Name|Batch|command\" 或 --content。", 2);

        var now = DateTime.Now;
        var template = new ServiceTemplate
        {
            Name = name.Trim(),
            Description = ReadOption(args, "--description") ?? string.Empty,
            ScriptSteps = EnsureDefaultComposite(steps),
            PresetVariables = [],
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

        bool changed = false;
        var name = ReadOption(args, "--name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (_appConfig.ServiceTemplates.Any(t => t.Id != template.Id &&
                                                      string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return CommandResponse.Error($"模板名称已存在: {name.Trim()}", 2);
            template.Name = name.Trim();
            changed = true;
        }

        var description = ReadOption(args, "--description");
        if (description != null)
        {
            template.Description = description;
            changed = true;
        }

        var steps = ParseSteps(args);
        if (steps.Count > 0)
        {
            template.ScriptSteps = EnsureDefaultComposite(steps);
            changed = true;
        }

        if (HasAnyOption(args, "--preset", "--preset-variable") || HasFlag(args, "--clear-presets"))
            return CommandResponse.Error("ServicePilot 2.0 已移除模板级预设变量；请使用 template step-variable-add 维护动作变量。", 2);

        template.UpdatedAt = DateTime.Now;
        await _configService.SaveAsync(_appConfig);
        return changed
            ? CommandResponse.Ok($"已更新模板: {template.Name}")
            : CommandResponse.Ok("未检测到变更，模板保持不变");
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
            return CommandResponse.Error("用法: template import --file FILE [--on-conflict rename|overwrite|skip]", 2);

        var conflictMode = ReadOption(args, "--on-conflict")?.ToLowerInvariant() switch
        {
            "overwrite" => ImportConflictMode.Overwrite,
            "skip" => ImportConflictMode.Skip,
            _ => ImportConflictMode.Rename // Default: backward compatible
        };

        try
        {
            var (imported, skipped) = await TemplateExchangeService.ImportAsync(file, _appConfig.ServiceTemplates, conflictMode);
            _appConfig.ServiceTemplates.AddRange(imported.Where(i => i.Mode != ImportConflictMode.Overwrite).Select(i => i.Template));
            await _configService.SaveAsync(_appConfig);

            var fullPath = Path.GetFullPath(file);
            var lines = new List<string>();

            foreach (var info in imported)
            {
                if (info.Mode == ImportConflictMode.Overwrite)
                {
                    lines.Add($"已导入模板（覆盖）: {info.Template.Name} → 替换原 Id: {info.ReplacedId}");
                }
                else if (info.WasRenamed)
                {
                    lines.Add($"已导入模板（新建，重命名）: {info.Template.Name} → 原名 \"{info.OriginalName}\" 已存在，重命名为 \"{info.Template.Name}\" → 分配新 Id: {info.Template.Id}");
                }
                else
                {
                    lines.Add($"已导入模板（新建）: {info.Template.Name} → 分配新 Id: {info.Template.Id}");
                }
            }

            foreach (var s in skipped)
            {
                lines.Add($"已跳过同名模板: {s.Name}");
            }

            lines.Add($"← 文件: {fullPath}");
            return CommandResponse.Ok(string.Join(Environment.NewLine, lines));
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
            return CommandResponse.Error($"找不到模板动作: {args[1]}", 2);

        var variables = step.StepVariables;
        if (HasFlag(args, "--json"))
            return Json(new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                StepId = step.Id,
                StepName = step.Name,
                step.Kind,
                VariableScope = "step",
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

        return result ?? CommandResponse.Ok($"已新增模板动作变量: {variable.Trim()}");
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

        return result ?? CommandResponse.Ok($"已删除模板动作变量: {variable.Trim()}");
    }

    private async Task<CommandResponse> TemplateStepVariableClearAsync(string[] args)
    {
        if (args.Length < 2)
            return CommandResponse.Error("用法: template step-variable-clear TEMPLATE STEP", 2);

        var result = await UpdateTemplateStepVariablesAsync(args[0], args[1], variables => variables.Clear());
        return result ?? CommandResponse.Ok("已清空模板动作变量。");
    }

    private async Task<CommandResponse?> UpdateTemplateStepVariablesAsync(string templateSelector, string stepSelector, Action<List<string>> update)
    {
        var template = FindTemplate(templateSelector);
        if (template == null)
            return CommandResponse.Error($"找不到模板: {templateSelector}", 2);

        var step = FindStep(template.ScriptSteps, stepSelector);
        if (step == null)
            return CommandResponse.Error($"找不到模板动作: {stepSelector}", 2);

        if (step.Kind != StepKind.Action)
            return CommandResponse.Error("组合动作不直接维护变量；请维护其成员动作的变量。", 2);

        var variables = step.StepVariables;
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
                Kind = StepKind.Action,
                ScriptType = type,
                UseVariable = parts.Length >= 4 && bool.TryParse(parts[2], out var useVariable) && useVariable,
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
                Kind = StepKind.Action,
                ScriptType = ReadScriptType(args),
                UseVariable = ReadBoolOption(args, "--use-variable") ?? false,
                OpenLogOnRun = ReadBoolOption(args, "--open-log-on-run") ?? false,
                Content = content,
                Order = 0
            });
        }

        return result;
    }

    private static List<ScriptStep> EnsureDefaultComposite(List<ScriptStep> steps)
    {
        if (steps.Any(step => step.Kind == StepKind.Composite))
            return steps.Select((step, index) =>
            {
                step.Order = index;
                return step;
            }).ToList();

        var actions = steps.Where(step => step.Kind == StepKind.Action).ToList();
        if (actions.Count == 0)
            return steps;

        var composite = new ScriptStep
        {
            Name = ConfigMigrationService.StartCompositeName,
            Kind = StepKind.Composite,
            UseVariable = false,
            OpenLogOnRun = false,
            MemberStepIds = actions.Select(step => step.Id).ToList(),
            Order = 0
        };

        var result = new List<ScriptStep> { composite };
        for (var i = 0; i < actions.Count; i++)
        {
            actions[i].Order = i + 1;
            result.Add(actions[i]);
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
                var orderedSteps = steps
                    .OrderBy(s => s.Order)
                    .ToList();
                if (order <= orderedSteps.Count)
                    return orderedSteps[order - 1];
            }

            return steps.FirstOrDefault(s => s.Order == order);
        }

        // When matching by name, reject ambiguous matches (multiple steps with same name)
        var nameMatches = steps
            .Where(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (nameMatches.Count > 1)
            return null; // Ambiguous — caller should report and ask for GUID
        return nameMatches.FirstOrDefault();
    }

    /// <summary>Checks if a name selector matches multiple steps (for better error messages when FindStep returns null).</summary>
    private static bool HasAmbiguousNameMatch(IReadOnlyList<ScriptStep> steps, string selector) =>
        !Guid.TryParse(selector, out _) &&
        !int.TryParse(selector, out _) &&
        steps.Count(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase)) > 1;

    private static object ToStatusDto(ServiceRuntimeState service)
    {
        var firstComposite = service.Config.ScriptSteps
            .OrderBy(s => s.Order)
            .FirstOrDefault(s => s.Kind == StepKind.Composite);
        return new
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
            ActionCount = service.Config.ScriptSteps.Count(step => step.Kind == StepKind.Action),
            CompositeCount = service.Config.ScriptSteps.Count(step => step.Kind == StepKind.Composite),
            DefaultStartStep = firstComposite != null ? new { firstComposite.Name, firstComposite.Id } : null,
            StepStates = service.Config.ScriptSteps.OrderBy(s => s.Order).Select(step => new
            {
                step.Id,
                step.Name,
                step.Kind,
                step.UseVariable,
                step.OpenLogOnRun,
                step.StepVariables,
                step.MemberStepIds,
                step.Order,
                Runtime = service.StepStates.TryGetValue(step.Id, out var state) ? state : null
            })
        };
    }

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

        _variableUsageStore?.Remember(step.Id, variable.Trim());
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
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var members = step.Kind == StepKind.Composite && step.MemberStepIds.Count > 0
                ? $" members={string.Join(",", step.MemberStepIds)}"
                : string.Empty;
            var stepVariables = step.StepVariables.Count > 0
                ? $" stepVariables={step.StepVariables.Count}"
                : string.Empty;
            yield return $"{step.Name} ({step.Id}) kind={step.Kind} type={step.ScriptType} useVariable={step.UseVariable} openLogOnRun={step.OpenLogOnRun}{stepVariables}{members}";
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
