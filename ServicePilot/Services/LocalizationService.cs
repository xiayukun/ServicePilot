using System.Globalization;

namespace ServicePilot.Services;

public sealed class LocalizationService
{
    public const string Auto = "auto";
    public const string Chinese = "zh-CN";
    public const string English = "en-US";
    private static readonly string SystemLanguage = DetectSystemLanguage();

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["Language"] = "语言",
        ["LanguageAuto"] = "跟随系统",
        ["LanguageChinese"] = "中文",
        ["LanguageEnglish"] = "English",
        ["LanguageCurrent"] = "当前: {0}",
        ["AddService"] = "新增服务",
        ["ManageServices"] = "管理服务",
        ["ManageTemplates"] = "管理模板",
        ["StopAllServices"] = "停止全部服务",
        ["Exit"] = "退出程序",
        ["Start"] = "启动",
        ["Stop"] = "停止",
        ["Restart"] = "重启",
        ["ViewLogs"] = "查看日志",
        ["Edit"] = "编辑",
        ["Delete"] = "删除",
        ["Export"] = "导出",
        ["Import"] = "导入",
        ["ExportTemplate"] = "导出模板",
        ["ImportTemplate"] = "导入模板",
        ["TemplateFileFilter"] = "ServicePilot 模板 (*.servicepilot-template.json)|*.servicepilot-template.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
        ["SelectTemplatePrompt"] = "请先选择一个模板。",
        ["TemplateExported"] = "模板已导出:\n{0}",
        ["TemplateImported"] = "已导入 {0} 个模板。",
        ["TemplateExportFailed"] = "模板导出失败:\n{0}",
        ["TemplateImportFailed"] = "模板导入失败:\n{0}",
        ["ImportedTemplateSuffix"] = "导入",
        ["SaveAsTemplate"] = "存为模板",
        ["RunStep"] = "执行动作",
        ["RunAction"] = "运行动作",
        ["RunComposite"] = "运行组合动作",
        ["AllLogs"] = "全部",
        ["ServiceLogs"] = "服务",
        ["StartupSteps"] = "启动执行",
        ["ManualSteps"] = "不启动执行",
        ["Add"] = "新增",
        ["Clear"] = "清空",
        ["SearchLogs"] = "搜索日志",
        ["FindPrevious"] = "上一个",
        ["FindNext"] = "下一个",
        ["AutoScroll"] = "自动滚动",
        ["CopySelected"] = "复制选中",
        ["CopyAll"] = "复制全部",
        ["CopyCommands"] = "复制命令",
        ["Close"] = "关闭",
        ["CopyHelpForAi"] = "复制给 AI 的帮助",
        ["CopyServicePilotHelpForAi"] = "复制给 AI 的 ServicePilot 帮助",
        ["AiHelpIntro"] = "把下面内容复制给个人 AI 助手，AI 就能直接找到当前 ServicePilot.exe 并先读取真实状态。",
        ["AiHelpExePath"] = "程序路径",
        ["AiHelpCommands"] = "建议先运行",
        ["AiHelpPrivacyNote"] = "提示：这段内容包含本机 exe 绝对路径，建议只发送给你信任的个人 AI 助手，不要公开贴到网页或 Issue。",
        ["Status"] = "状态",
        ["Name"] = "名称",
        ["Description"] = "说明",
        ["ServiceConfig"] = "服务配置",
        ["AddServiceTitle"] = "新增服务",
        ["EditServiceTitle"] = "编辑服务",
        ["Template"] = "模板",
        ["AddTemplateTitle"] = "新增模板",
        ["EditTemplateTitle"] = "编辑模板",
        ["SaveTemplateTitle"] = "存为模板",
        ["ServiceName"] = "服务名称",
        ["TemplateName"] = "模板名称",
        ["Browse"] = "浏览",
        ["SelectWorkingDirectory"] = "选择工作目录",
        ["ScriptSteps"] = "脚本动作",
        ["Actions"] = "动作",
        ["Action"] = "动作",
        ["Composite"] = "组合动作",
        ["StepKind"] = "动作类型",
        ["Members"] = "成员",
        ["AddMember"] = "添加成员",
        ["NoActions"] = "无动作",
        ["ApplyTemplate"] = "应用模板",
        ["Apply"] = "应用",
        ["TemplateSummary"] = "动作 {0}，组合 {1}",
        ["DeleteShort"] = "删",
        ["StepName"] = "动作名称",
        ["ScriptType"] = "脚本类型",
        ["UseVariable"] = "使用变量",
        ["RunOnStart"] = "启动执行",
        ["OpenLogOnRun"] = "弹出日志",
        ["ScriptContent"] = "脚本内容",
        ["AutoStartService"] = "启动 ServicePilot 时自动启动此服务",
        ["Save"] = "保存",
        ["Prompt"] = "提示",
        ["EnterServiceName"] = "请输入服务名称。",
        ["EnterTemplateName"] = "请输入模板名称。",
        ["SelectDirectoryPrompt"] = "请选择工作目录。",
        ["AddOneStepPrompt"] = "请至少添加一个动作。",
        ["StepContentRequired"] = "动作内容不能为空。",
        ["ActionContentRequired"] = "动作命令不能为空。",
        ["CompositeMembersRequired"] = "组合动作至少需要一个成员动作。",
        ["CompositeCannotNest"] = "组合动作不能包含另一个组合动作。",
        ["CompositeOneVariableMember"] = "一个组合动作最多只能包含一个使用变量的成员动作。",
        ["NoTemplatesAvailable"] = "还没有可用模板。可以从服务存为模板，或在模板管理中新增。",
        ["DefaultStepName"] = "动作 {0}",
        ["UnnamedStep"] = "未命名动作",
        ["PresetVariables"] = "预设变量",
        ["StartupVariablesHelp"] = "一行一个启动变量",
        ["ManualStepVariables"] = "手动执行变量",
        ["ManualStepVariablesHelp"] = "一行一个动作变量",
        ["ManualStepVariablesTooltip"] = "仅用于当前手动动作。运行该动作时可选择一个变量；它会注入为 SERVICEPILOT_VARIABLE，并替换脚本中的 {{variable}} / {{变量}}。",
        ["StartupVariablesTooltip"] = "用于启动动作。启动、重启或运行启动动作时可选择一个变量；它会注入为 SERVICEPILOT_VARIABLE，并替换脚本中的 {{variable}} / {{变量}}。",
        ["StepVariables"] = "动作变量",
        ["StepVariablesHelp"] = "一行一个动作变量",
        ["StepVariablesTooltip"] = "运行该动作时可选择一个变量；它会注入为 SERVICEPILOT_VARIABLE，并替换脚本中的 {{variable}} / {{变量}}。",
        ["NoStepVariablesHelp"] = "当前动作不使用变量，或组合动作本身不直接设置变量。",
        ["WorkingDirectory"] = "工作目录",
        ["Steps"] = "动作",
        ["Variables"] = "变量",
        ["AutoStart"] = "自启",
        ["UpdatedAt"] = "更新时间",
        ["Ok"] = "确定",
        ["Cancel"] = "取消",
        ["AddVariableTitle"] = "新增变量",
        ["EnterVariable"] = "输入本次使用的变量",
        ["VariableRequired"] = "变量不能为空。",
        ["ConfirmDeleteTemplate"] = "确定删除模板“{0}”？",
        ["LogTitle"] = "日志 - {0}",
        ["TrayStatusEmpty"] = "ServicePilot: 0/{0} 运行中，{1} 失败",
        ["TrayStatusActive"] = "ServicePilot: {0}/{1} 运行中，{2} 失败",
        ["EmptyVariable"] = "(空变量)",
        ["StateStopped"] = "已停止",
        ["StateStarting"] = "启动中",
        ["StateRunning"] = "运行中",
        ["StateStopping"] = "停止中",
        ["StateError"] = "出错",
        ["StateStartFailed"] = "启动失败",
        ["StateCompleted"] = "已完成",
        ["StateUnknown"] = "未知",
        ["StepNotRun"] = "未执行",
        ["StepRunning"] = "运行中",
        ["StepSucceeded"] = "已完成",
        ["StepFailed"] = "失败",
        ["StepSkipped"] = "已跳过",
        ["StepCancelled"] = "已取消",
        ["AlreadyRunning"] = "ServicePilot 已经在任务栏通知区域运行。",
        ["StartupFailed"] = "启动失败:\n{0}",
        ["StartupErrorTitle"] = "ServicePilot 启动错误",
        ["ServicePilotErrorTitle"] = "ServicePilot 错误",
        ["TrayCreateFailed"] = "系统托盘创建失败:\n{0}",
        ["UiThreadException"] = "UI 线程异常:\n{0}",
        ["FatalThreadException"] = "非 UI 线程异常:\n{0}",
        ["AsyncTaskException"] = "异步任务异常:\n{0}",
        ["ServiceNameExists"] = "服务名称已存在: {0}",
        ["TemplateNameExists"] = "模板名称已存在: {0}",
        ["ConfirmDeleteService"] = "确定删除服务“{0}”？",
        ["NoTemplateSteps"] = "服务没有可保存的脚本动作。",
        ["ServiceErrorTitle"] = "ServicePilot - {0} 出错",
        ["RunFailedOpenLogs"] = "运行失败，请查看日志。"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["Language"] = "Language",
        ["LanguageAuto"] = "Follow system",
        ["LanguageChinese"] = "中文",
        ["LanguageEnglish"] = "English",
        ["LanguageCurrent"] = "Current: {0}",
        ["AddService"] = "Add service",
        ["ManageServices"] = "Manage services",
        ["ManageTemplates"] = "Manage templates",
        ["StopAllServices"] = "Stop all services",
        ["Exit"] = "Exit",
        ["Start"] = "Start",
        ["Stop"] = "Stop",
        ["Restart"] = "Restart",
        ["ViewLogs"] = "View logs",
        ["Edit"] = "Edit",
        ["Delete"] = "Delete",
        ["Export"] = "Export",
        ["Import"] = "Import",
        ["ExportTemplate"] = "Export template",
        ["ImportTemplate"] = "Import template",
        ["TemplateFileFilter"] = "ServicePilot template (*.servicepilot-template.json)|*.servicepilot-template.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
        ["SelectTemplatePrompt"] = "Select a template first.",
        ["TemplateExported"] = "Template exported:\n{0}",
        ["TemplateImported"] = "Imported {0} template(s).",
        ["TemplateExportFailed"] = "Template export failed:\n{0}",
        ["TemplateImportFailed"] = "Template import failed:\n{0}",
        ["ImportedTemplateSuffix"] = "imported",
        ["SaveAsTemplate"] = "Save as template",
        ["RunStep"] = "Run step",
        ["RunAction"] = "Run action",
        ["RunComposite"] = "Run composite",
        ["AllLogs"] = "All",
        ["ServiceLogs"] = "Service",
        ["StartupSteps"] = "Startup steps",
        ["ManualSteps"] = "Manual steps",
        ["Add"] = "Add",
        ["Clear"] = "Clear",
        ["SearchLogs"] = "Search logs",
        ["FindPrevious"] = "Prev",
        ["FindNext"] = "Next",
        ["AutoScroll"] = "Auto scroll",
        ["CopySelected"] = "Copy selected",
        ["CopyAll"] = "Copy all",
        ["CopyCommands"] = "Copy commands",
        ["Close"] = "Close",
        ["CopyHelpForAi"] = "Copy help for AI",
        ["CopyServicePilotHelpForAi"] = "Copy ServicePilot help for AI",
        ["AiHelpIntro"] = "Copy the text below to your personal AI assistant so it can find the current ServicePilot.exe and inspect real state first.",
        ["AiHelpExePath"] = "Exe path",
        ["AiHelpCommands"] = "Run first",
        ["AiHelpPrivacyNote"] = "Note: this text contains your local exe path. Share it only with a trusted personal AI assistant, not in public web pages or issues.",
        ["Status"] = "Status",
        ["Name"] = "Name",
        ["Description"] = "Description",
        ["ServiceConfig"] = "Service configuration",
        ["AddServiceTitle"] = "Add service",
        ["EditServiceTitle"] = "Edit service",
        ["Template"] = "Template",
        ["AddTemplateTitle"] = "Add template",
        ["EditTemplateTitle"] = "Edit template",
        ["SaveTemplateTitle"] = "Save as template",
        ["ServiceName"] = "Service name",
        ["TemplateName"] = "Template name",
        ["Browse"] = "Browse",
        ["SelectWorkingDirectory"] = "Select working directory",
        ["ScriptSteps"] = "Script steps",
        ["Actions"] = "Actions",
        ["Action"] = "Action",
        ["Composite"] = "Composite",
        ["StepKind"] = "Action type",
        ["Members"] = "Members",
        ["AddMember"] = "Add member",
        ["NoActions"] = "No actions",
        ["ApplyTemplate"] = "Apply template",
        ["Apply"] = "Apply",
        ["TemplateSummary"] = "{0} actions, {1} composites",
        ["DeleteShort"] = "Del",
        ["StepName"] = "Action name",
        ["ScriptType"] = "Script type",
        ["UseVariable"] = "Use variable",
        ["RunOnStart"] = "Run on start",
        ["OpenLogOnRun"] = "Open log",
        ["ScriptContent"] = "Script content",
        ["AutoStartService"] = "Autostart this service when ServicePilot starts",
        ["Save"] = "Save",
        ["Prompt"] = "Prompt",
        ["EnterServiceName"] = "Enter a service name.",
        ["EnterTemplateName"] = "Enter a template name.",
        ["SelectDirectoryPrompt"] = "Select a working directory.",
        ["AddOneStepPrompt"] = "Add at least one action.",
        ["StepContentRequired"] = "Action content cannot be empty.",
        ["ActionContentRequired"] = "Action command cannot be empty.",
        ["CompositeMembersRequired"] = "A composite action needs at least one member action.",
        ["CompositeCannotNest"] = "A composite action cannot contain another composite action.",
        ["CompositeOneVariableMember"] = "A composite action can contain at most one variable-enabled member action.",
        ["NoTemplatesAvailable"] = "No templates are available. Save a service as a template or add one in template manager.",
        ["DefaultStepName"] = "Action {0}",
        ["UnnamedStep"] = "Unnamed action",
        ["PresetVariables"] = "Preset variables",
        ["StartupVariablesHelp"] = "One startup variable per line",
        ["ManualStepVariables"] = "Manual step variables",
        ["ManualStepVariablesHelp"] = "One step variable per line",
        ["ManualStepVariablesTooltip"] = "Only used by the current manual-only step. When the step runs, one selected variable is injected as SERVICEPILOT_VARIABLE and replaces {{variable}} / {{变量}}.",
        ["StartupVariablesTooltip"] = "Used by startup steps. When start, restart, or a startup step runs, one selected variable is injected as SERVICEPILOT_VARIABLE and replaces {{variable}} / {{变量}}.",
        ["StepVariables"] = "Action variables",
        ["StepVariablesHelp"] = "One action variable per line",
        ["StepVariablesTooltip"] = "When this action runs, one selected variable is injected as SERVICEPILOT_VARIABLE and replaces {{variable}} / {{变量}}.",
        ["NoStepVariablesHelp"] = "The current action does not use variables, or this is a composite action.",
        ["WorkingDirectory"] = "Working directory",
        ["Steps"] = "Actions",
        ["Variables"] = "Variables",
        ["AutoStart"] = "Autostart",
        ["UpdatedAt"] = "Updated at",
        ["Ok"] = "OK",
        ["Cancel"] = "Cancel",
        ["AddVariableTitle"] = "Add variable",
        ["EnterVariable"] = "Enter the variable for this run",
        ["VariableRequired"] = "Variable cannot be empty.",
        ["ConfirmDeleteTemplate"] = "Delete template \"{0}\"?",
        ["LogTitle"] = "Logs - {0}",
        ["TrayStatusEmpty"] = "ServicePilot: 0/{0} running, {1} failed",
        ["TrayStatusActive"] = "ServicePilot: {0}/{1} running, {2} failed",
        ["EmptyVariable"] = "(empty variable)",
        ["StateStopped"] = "Stopped",
        ["StateStarting"] = "Starting",
        ["StateRunning"] = "Running",
        ["StateStopping"] = "Stopping",
        ["StateError"] = "Error",
        ["StateStartFailed"] = "Start failed",
        ["StateCompleted"] = "Completed",
        ["StateUnknown"] = "Unknown",
        ["StepNotRun"] = "Not run",
        ["StepRunning"] = "Running",
        ["StepSucceeded"] = "Succeeded",
        ["StepFailed"] = "Failed",
        ["StepSkipped"] = "Skipped",
        ["StepCancelled"] = "Cancelled",
        ["AlreadyRunning"] = "ServicePilot is already running in the notification area.",
        ["StartupFailed"] = "Startup failed:\n{0}",
        ["StartupErrorTitle"] = "ServicePilot startup error",
        ["ServicePilotErrorTitle"] = "ServicePilot error",
        ["TrayCreateFailed"] = "Failed to create system tray icon:\n{0}",
        ["UiThreadException"] = "UI thread exception:\n{0}",
        ["FatalThreadException"] = "Non-UI thread exception:\n{0}",
        ["AsyncTaskException"] = "Async task exception:\n{0}",
        ["ServiceNameExists"] = "Service name already exists: {0}",
        ["TemplateNameExists"] = "Template name already exists: {0}",
        ["ConfirmDeleteService"] = "Delete service \"{0}\"?",
        ["NoTemplateSteps"] = "This service has no script steps to save.",
        ["ServiceErrorTitle"] = "ServicePilot - {0} error",
        ["RunFailedOpenLogs"] = "Run failed. Check logs."
    };

    public static LocalizationService Current { get; } = new();

    public string LanguageSetting { get; private set; } = Auto;
    public string EffectiveLanguage { get; private set; } = SystemLanguage;
    public bool IsEnglish => EffectiveLanguage == English;

    public event EventHandler? LanguageChanged;

    public void Configure(string? languageSetting, bool raiseChanged = false)
    {
        LanguageSetting = NormalizeLanguageSetting(languageSetting);
        EffectiveLanguage = LanguageSetting == Auto ? SystemLanguage : LanguageSetting;
        ApplyCulture();

        if (raiseChanged)
            LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string T(string key)
    {
        var table = IsEnglish ? En : Zh;
        return table.TryGetValue(key, out var value) ? value : key;
    }

    public string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentUICulture, T(key), args);

    public string DisplayLanguageName(string setting)
    {
        var normalized = NormalizeLanguageSetting(setting);
        return normalized switch
        {
            Chinese => T("LanguageChinese"),
            English => T("LanguageEnglish"),
            _ => $"{T("LanguageAuto")} ({(SystemLanguage == English ? T("LanguageEnglish") : T("LanguageChinese"))})"
        };
    }

    public static string NormalizeLanguageSetting(string? languageSetting)
    {
        if (string.Equals(languageSetting, Chinese, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(languageSetting, "zh", StringComparison.OrdinalIgnoreCase))
            return Chinese;

        if (string.Equals(languageSetting, English, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(languageSetting, "en", StringComparison.OrdinalIgnoreCase))
            return English;

        return Auto;
    }

    private static string DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
               culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? Chinese
            : English;
    }

    private void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(EffectiveLanguage);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}
