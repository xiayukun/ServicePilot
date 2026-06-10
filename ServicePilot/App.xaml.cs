using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels;
using ServicePilot.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ServicePilot;

public partial class App : Application
{
    private const int MaxBufferedLogEntries = 20000;

    private const string InstanceMutexName = "ServicePilot.TrayInstance.v1";
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private readonly Dictionary<Guid, LogWindow> _logWindows = new();
    private readonly Dictionary<Guid, List<LogEntry>> _logBuffers = new();
    private readonly Dictionary<Guid, DateTime> _lastErrorNotificationAt = new();

    private Forms.NotifyIcon? _trayIcon;
    private MainViewModel? _mainViewModel;
    private ProcessManager? _processManager;
    private ConfigService _configService = null!;
    private AppConfig _appConfig = null!;
    private PresetVariableUsageStore _variableUsageStore = null!;
    private ServiceCommandProcessor? _commandProcessor;
    private CommandPipeServer? _commandPipeServer;
    private Mutex? _singleInstanceMutex;
    private Drawing.Icon? _trayRuntimeIcon;
    private bool _ownsMutex;
    private bool _isExiting;

    private static readonly Drawing.Image RunningStatusDot = CreateStatusDotImage(Drawing.Color.FromArgb(255, 24, 155, 96));
    private static readonly Drawing.Image FailedStatusDot = CreateStatusDotImage(Drawing.Color.FromArgb(255, 220, 53, 69));
    private static readonly Drawing.Image WarningStatusDot = CreateStatusDotImage(Drawing.Color.FromArgb(255, 245, 158, 11));

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        if (e.Args.Length > 0)
        {
            var exitCode = await CommandLineHost.RunAsync(e.Args);
            Environment.Exit(exitCode);
            return;
        }

        FreeConsole();
        RegisterExceptionHandlers();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var ownsMutex);
        _ownsMutex = ownsMutex;
        if (!ownsMutex)
        {
            MessageBox.Show(LocalizationService.Current.T("AlreadyRunning"), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _configService = new ConfigService();
            _appConfig = await _configService.LoadAsync();
            _appConfig.Settings ??= new AppSettings();
            await EnsureBuiltInTemplatesSeededAsync();
            LocalizationService.Current.Configure(_appConfig.Settings.Language);
            _variableUsageStore = new PresetVariableUsageStore(_configService.ConfigDirectory);

            _processManager = new ProcessManager();
            _processManager.LoadConfigs(_appConfig.Services);
            _processManager.ServiceOutput += OnServiceOutput;
            _processManager.ServiceStateChanged += (_, _) => Dispatcher.BeginInvoke(RebuildTrayMenu);
            _processManager.StepStateChanged += OnProcessStepStateChanged;

            _mainViewModel = new MainViewModel(_processManager, _configService, _appConfig);
            _mainViewModel.AddServiceRequested += OnAddServiceRequested;
            _mainViewModel.ServiceAdded += OnServiceAdded;
            _mainViewModel.ServiceRemoved += OnServiceRemoved;

            foreach (var vm in _mainViewModel.Services)
                vm.LogRequested += OnViewLogRequested;

            _commandProcessor = new ServiceCommandProcessor(
                _configService,
                _appConfig,
                _processManager,
                _mainViewModel,
                GetBufferedLogs,
                RequestExitFromCommandAsync,
                _variableUsageStore);
            _commandPipeServer = new CommandPipeServer(HandlePipeCommandAsync);
            _commandPipeServer.Start();

            CreateTrayIcon();

            foreach (var state in _processManager.Services.Where(s => s.Config.AutoStart))
                _processManager.StartService(state.Config.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationService.Current.F("StartupFailed", ex), LocalizationService.Current.T("StartupErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(LocalizationService.Current.F("UiThreadException", args.Exception), LocalizationService.Current.T("ServicePilotErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(LocalizationService.Current.F("FatalThreadException", ex), LocalizationService.Current.T("ServicePilotErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            MessageBox.Show(LocalizationService.Current.F("AsyncTaskException", args.Exception?.InnerException ?? args.Exception),
                LocalizationService.Current.T("ServicePilotErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.SetObserved();
        };
    }

    private async Task EnsureBuiltInTemplatesSeededAsync()
    {
        if (_appConfig.Settings.BuiltInTemplatesSeeded)
            return;

        foreach (var template in ServiceTemplateService.CreateBuiltInTemplates())
        {
            var exists = _appConfig.ServiceTemplates.Any(existing =>
                existing.Id == template.Id ||
                string.Equals(existing.Name, template.Name, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                _appConfig.ServiceTemplates.Add(template);
        }

        _appConfig.Settings.BuiltInTemplatesSeeded = true;
        await _configService.SaveAsync(_appConfig);
    }

    private void CreateTrayIcon()
    {
        try
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "ServicePilot",
                Visible = true,
                Icon = CreateTrayIconWithBadge(0)
            };

            _trayIcon.DoubleClick += (_, _) =>
            {
                RebuildTrayMenu();
                _trayIcon.ContextMenuStrip?.Show(Forms.Cursor.Position);
            };

            RebuildTrayMenu();
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocalizationService.Current.F("TrayCreateFailed", ex.Message),
                LocalizationService.Current.T("ServicePilotErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RebuildTrayMenu()
    {
        if (_isExiting || _trayIcon == null || _mainViewModel == null || _processManager == null)
            return;

        var menu = new Forms.ContextMenuStrip();

        foreach (var service in GetSortedServicesForDisplay())
        {
            var state = service.RuntimeState.State;
            var serviceMenu = new Forms.ToolStripMenuItem(service.Name)
            {
                Image = GetServiceStatusDot(service),
                ToolTipText = FormatStatusText(state)
            };

            AddStartMenu(serviceMenu, service);
            AddRunStepMenuV2(serviceMenu, service);

            var stop = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Stop"))
            {
                Enabled = service.RuntimeState.State is ProcessState.Running or ProcessState.Starting
            };
            stop.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await StopServiceQuietlyAsync(service.Config.Id);
            };
            serviceMenu.DropDownItems.Add(stop);

            AddRestartMenu(serviceMenu, service);

            var viewLog = new Forms.ToolStripMenuItem(LocalizationService.Current.T("ViewLogs"));
            viewLog.Click += (_, _) => OnViewLogRequested(service);
            serviceMenu.DropDownItems.Add(viewLog);

            serviceMenu.DropDownItems.Add(new Forms.ToolStripSeparator());

            var edit = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Edit"));
            edit.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await OnEditServiceRequested(service);
            };
            serviceMenu.DropDownItems.Add(edit);

            var delete = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Delete"));
            delete.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await OnDeleteServiceRequested(service);
            };
            serviceMenu.DropDownItems.Add(delete);

            var saveAsTemplate = new Forms.ToolStripMenuItem(LocalizationService.Current.T("SaveAsTemplate"));
            saveAsTemplate.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await OnSaveServiceAsTemplateRequested(service);
            };
            serviceMenu.DropDownItems.Add(saveAsTemplate);

            menu.Items.Add(serviceMenu);
        }

        if (_mainViewModel.Services.Count > 0)
            menu.Items.Add(new Forms.ToolStripSeparator());

        var addService = new Forms.ToolStripMenuItem(LocalizationService.Current.T("AddService"));
        addService.Click += (_, _) => OnAddServiceRequested();
        menu.Items.Add(addService);

        var manageServices = new Forms.ToolStripMenuItem(LocalizationService.Current.T("ManageServices"));
        manageServices.Click += (_, _) => OnManageServicesRequested();
        menu.Items.Add(manageServices);

        var manageTemplates = new Forms.ToolStripMenuItem(LocalizationService.Current.T("ManageTemplates"));
        manageTemplates.Click += (_, _) => OnManageTemplatesRequested();
        menu.Items.Add(manageTemplates);

        var stopAll = new Forms.ToolStripMenuItem(LocalizationService.Current.T("StopAllServices"))
        {
            Enabled = _mainViewModel.Services.Any(s => s.RuntimeState.State is ProcessState.Running or ProcessState.Starting ||
                                                       s.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running))
        };
        stopAll.Click += async (_, _) => await StopAllQuietlyAsync();
        menu.Items.Add(stopAll);

        menu.Items.Add(new Forms.ToolStripSeparator());

        AddLanguageMenu(menu);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var status = new Forms.ToolStripMenuItem(GetTrayStatusText()) { Enabled = false };
        menu.Items.Add(status);

        var exit = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Exit"));
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(exit);

        var oldMenu = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
        _trayIcon.Text = ShortTrayText(GetTrayStatusText());
        UpdateTrayIconBadge();
    }

    private void OnProcessStepStateChanged(Guid serviceId, StepRuntimeState stepState)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RebuildTrayMenu();
            OpenLogWindowForStepIfRequested(serviceId, stepState);
        });
    }

    private void OpenLogWindowForStepIfRequested(Guid serviceId, StepRuntimeState stepState)
    {
        if (_isExiting ||
            stepState.State != StepRunState.Running ||
            _mainViewModel == null)
        {
            return;
        }

        var service = _mainViewModel.Services.FirstOrDefault(vm => vm.Config.Id == serviceId);
        var step = service?.Config.ScriptSteps.FirstOrDefault(item => item.Id == stepState.StepId);
        if (service == null || step is not { OpenLogOnRun: true })
            return;

        OnViewLogRequested(service);
    }

    private IReadOnlyList<ServiceItemViewModel> GetSortedServicesForDisplay()
    {
        if (_mainViewModel == null)
            return [];

        return _variableUsageStore.SortServices(
            _mainViewModel.Services,
            service => service.Config.Id,
            service => service.Config.SortOrder,
            service => service.Name);
    }

    private void RememberServiceUse(ServiceItemViewModel service)
    {
        _variableUsageStore.RememberService(service.Config.Id);
    }

    private void AddStartMenu(Forms.ToolStripMenuItem parent, ServiceItemViewModel service)
    {
        var state = service.RuntimeState.State;
        var enabled = state is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed;
        var variables = GetSortedPresetVariables(service);
        if (variables.Count == 0)
        {
            var start = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Start")) { Enabled = enabled };
            start.Click += (_, _) =>
            {
                RememberServiceUse(service);
                _processManager?.StartService(service.Config.Id);
                RebuildTrayMenu();
            };
            parent.DropDownItems.Add(start);
            return;
        }

        var startMenu = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Start")) { Enabled = enabled };
        foreach (var variable in variables)
        {
            var item = new Forms.ToolStripMenuItem(FormatVariableLabel(variable));
            item.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await RememberPresetVariableAsync(service.Config, variable, addIfMissing: false);
                _processManager?.StartService(service.Config.Id, new ServiceStartOptions { Variable = variable });
                RebuildTrayMenu();
            };
            startMenu.DropDownItems.Add(item);
        }
        AddNewVariableMenuItem(startMenu, service, variable =>
        {
            RememberServiceUse(service);
            _processManager?.StartService(service.Config.Id, new ServiceStartOptions { Variable = variable });
            return Task.CompletedTask;
        });
        parent.DropDownItems.Add(startMenu);
    }

    private void AddLanguageMenu(Forms.ContextMenuStrip menu)
    {
        var languageMenu = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Language"));
        AddLanguageOption(languageMenu, LocalizationService.Auto);
        AddLanguageOption(languageMenu, LocalizationService.Chinese);
        AddLanguageOption(languageMenu, LocalizationService.English);
        languageMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        languageMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(
            LocalizationService.Current.F("LanguageCurrent", LocalizationService.Current.DisplayLanguageName(LocalizationService.Current.LanguageSetting)))
        {
            Enabled = false
        });
        menu.Items.Add(languageMenu);
    }

    private void AddLanguageOption(Forms.ToolStripMenuItem parent, string languageSetting)
    {
        var item = new Forms.ToolStripMenuItem(LocalizationService.Current.DisplayLanguageName(languageSetting))
        {
            Checked = string.Equals(LocalizationService.Current.LanguageSetting, languageSetting, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += async (_, _) => await SetLanguageAsync(languageSetting);
        parent.DropDownItems.Add(item);
    }

    private async Task SetLanguageAsync(string languageSetting)
    {
        if (_appConfig == null)
            return;

        var normalized = LocalizationService.NormalizeLanguageSetting(languageSetting);
        if (string.Equals(_appConfig.Settings.Language, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _appConfig.Settings.Language = normalized;
        LocalizationService.Current.Configure(normalized, raiseChanged: true);
        if (_configService != null)
            await _configService.SaveAsync(_appConfig);

        if (_mainViewModel != null)
        {
            foreach (var vm in _mainViewModel.Services)
                vm.RefreshLanguage();
        }

        RebuildTrayMenu();
    }

    private void AddRestartMenu(Forms.ToolStripMenuItem parent, ServiceItemViewModel service)
    {
        var state = service.RuntimeState.State;
        var enabled = state is not ProcessState.Starting and not ProcessState.Stopping;
        var variables = GetSortedPresetVariables(service);
        if (variables.Count == 0)
        {
            var restart = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Restart")) { Enabled = enabled };
            restart.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await RestartServiceQuietlyAsync(service.Config.Id);
            };
            parent.DropDownItems.Add(restart);
            return;
        }

        var restartMenu = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Restart")) { Enabled = enabled };
        foreach (var variable in variables)
        {
            var item = new Forms.ToolStripMenuItem(FormatVariableLabel(variable));
            item.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await RememberPresetVariableAsync(service.Config, variable, addIfMissing: false);
                await RestartServiceQuietlyAsync(service.Config.Id, variable);
            };
            restartMenu.DropDownItems.Add(item);
        }
        AddNewVariableMenuItem(restartMenu, service, variable =>
        {
            RememberServiceUse(service);
            return RestartServiceQuietlyAsync(service.Config.Id, variable);
        });
        parent.DropDownItems.Add(restartMenu);
    }

    private void AddRunStepMenu(Forms.ToolStripMenuItem parent, ServiceItemViewModel service)
    {
        var runStepMenu = new Forms.ToolStripMenuItem(LocalizationService.Current.T("RunStep"))
        {
            Enabled = service.RuntimeState.State is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed
        };

        var startupNumber = 1;
        foreach (var step in service.Config.ScriptSteps
                     .Where(s => !string.IsNullOrWhiteSpace(s.Content))
                     .OrderBy(s => s.Order))
        {
            var label = step.RunOnStart ? $"{startupNumber++}. {step.Name}" : step.Name;
            if (service.Config.PresetVariables.Count == 0)
            {
            var stepItem = new Forms.ToolStripMenuItem(label);
                stepItem.Click += (_, _) =>
                {
                    RememberServiceUse(service);
                    _processManager?.RunStep(service.Config.Id, step.Id);
                    RebuildTrayMenu();
                };
                runStepMenu.DropDownItems.Add(stepItem);
                continue;
            }

            var stepMenu = new Forms.ToolStripMenuItem(label);
            foreach (var variable in service.Config.PresetVariables)
            {
                var variableItem = new Forms.ToolStripMenuItem(FormatVariableLabel(variable));
                variableItem.Click += (_, _) =>
                {
                    RememberServiceUse(service);
                    _processManager?.RunStep(service.Config.Id, step.Id, variable);
                    RebuildTrayMenu();
                };
                stepMenu.DropDownItems.Add(variableItem);
            }
            runStepMenu.DropDownItems.Add(stepMenu);
        }

        parent.DropDownItems.Add(runStepMenu);
    }

    private void AddRunStepMenuV2(Forms.ToolStripMenuItem parent, ServiceItemViewModel service)
    {
        var runnableSteps = service.Config.ScriptSteps
            .Where(s => !string.IsNullOrWhiteSpace(s.Content))
            .OrderBy(s => s.Order)
            .ToList();

        var runStepMenu = new Forms.ToolStripMenuItem(LocalizationService.Current.T("RunStep"))
        {
            Enabled = runnableSteps.Count > 0
        };

        AddRunStepGroup(runStepMenu, service, runnableSteps.Where(step => step.RunOnStart), LocalizationService.Current.T("StartupSteps"));
        AddRunStepGroup(runStepMenu, service, runnableSteps.Where(step => !step.RunOnStart), LocalizationService.Current.T("ManualSteps"));

        parent.DropDownItems.Add(runStepMenu);
    }

    private void AddRunStepGroup(
        Forms.ToolStripMenuItem parent,
        ServiceItemViewModel service,
        IEnumerable<ScriptStep> sourceSteps,
        string header)
    {
        var steps = sourceSteps.ToList();
        if (steps.Count == 0)
            return;

        if (parent.DropDownItems.Count > 0)
            parent.DropDownItems.Add(new Forms.ToolStripSeparator());

        parent.DropDownItems.Add(new Forms.ToolStripMenuItem(header) { Enabled = false });
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var label = step.RunOnStart ? $"{i + 1}. {step.Name}" : step.Name;
            parent.DropDownItems.Add(CreateRunStepMenuItem(service, step, label));
        }
    }

    private Forms.ToolStripMenuItem CreateRunStepMenuItem(ServiceItemViewModel service, ScriptStep step, string label)
    {
        var stepState = GetStepState(service.RuntimeState, step.Id);
        var state = stepState?.State ?? StepRunState.NotRun;
        var isRunning = state == StepRunState.Running;
        var variables = GetSortedVariablesForStep(service, step);
        if (!step.UseVariable || (variables.Count == 0 && step.RunOnStart))
        {
            var stepItem = new Forms.ToolStripMenuItem(label)
            {
                Enabled = !isRunning,
                Image = GetStepStatusDot(state),
                ToolTipText = FormatStepStateText(state)
            };
            stepItem.Click += (_, _) =>
            {
                RememberServiceUse(service);
                _processManager?.RunStep(service.Config.Id, step.Id);
                RebuildTrayMenu();
            };
            return stepItem;
        }

        var stepMenu = new Forms.ToolStripMenuItem(label)
        {
            Enabled = !isRunning,
            Image = GetStepStatusDot(state),
            ToolTipText = FormatStepStateText(state)
        };
        foreach (var variable in variables)
        {
            var variableItem = new Forms.ToolStripMenuItem(FormatVariableLabel(variable));
            variableItem.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await RememberVariableForStepAsync(service.Config, step, variable, addIfMissing: false);
                _processManager?.RunStep(service.Config.Id, step.Id, variable);
                RebuildTrayMenu();
            };
            stepMenu.DropDownItems.Add(variableItem);
        }
        AddNewStepVariableMenuItem(stepMenu, service, step, variable =>
        {
            RememberServiceUse(service);
            _processManager?.RunStep(service.Config.Id, step.Id, variable);
            return Task.CompletedTask;
        });
        return stepMenu;
    }

    private string GetTrayStatusText()
    {
        if (_mainViewModel == null)
            return "ServicePilot: 0 running";

        var total = _mainViewModel.Services.Count;
        var active = _mainViewModel.Services
            .Where(s => s.RuntimeState.State is ProcessState.Running or ProcessState.Starting ||
                        s.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running))
            .ToList();
        var failed = _mainViewModel.Services.Count(s => s.RuntimeState.State is ProcessState.Error or ProcessState.StartFailed);

        if (active.Count == 0)
            return LocalizationService.Current.F("TrayStatusEmpty", total, failed);

        var details = active.Select(s =>
        {
            var variable = s.RuntimeState.ActiveVariable;
            var suffix = string.IsNullOrWhiteSpace(variable) ? "" : $" [{variable}]";
            return $"{s.Name}{suffix}";
        });

        return LocalizationService.Current.F("TrayStatusActive", GetActiveProcessCount(), total, failed, string.Join(", ", details));
    }

    private void UpdateTrayIconBadge()
    {
        if (_trayIcon == null || _mainViewModel == null)
            return;

        var activeCount = GetActiveProcessCount();
        try
        {
            _trayIcon.Icon = CreateTrayIconWithBadge(activeCount);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (NullReferenceException)
        {
        }
    }

    private static string FormatStatusText(ProcessState state) => state switch
    {
        ProcessState.Stopped => LocalizationService.Current.T("StateStopped"),
        ProcessState.Starting => LocalizationService.Current.T("StateStarting"),
        ProcessState.Running => LocalizationService.Current.T("StateRunning"),
        ProcessState.Stopping => LocalizationService.Current.T("StateStopping"),
        ProcessState.Error => LocalizationService.Current.T("StateError"),
        ProcessState.StartFailed => LocalizationService.Current.T("StateStartFailed"),
        ProcessState.Completed => LocalizationService.Current.T("StateCompleted"),
        _ => LocalizationService.Current.T("StateUnknown")
    };

    private int GetActiveProcessCount()
    {
        if (_mainViewModel == null)
            return 0;

        return _mainViewModel.Services.Sum(service =>
        {
            var runningSteps = service.RuntimeState.StepStates.Values.Count(step => step.State == StepRunState.Running);
            return runningSteps > 0
                ? runningSteps
                : service.RuntimeState.State is ProcessState.Running or ProcessState.Starting ? 1 : 0;
        });
    }

    private Drawing.Icon CreateTrayIconWithBadge(int activeCount)
    {
        _trayRuntimeIcon?.Dispose();

        using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Drawing.Color.Transparent);

        var background = activeCount > 0
            ? Drawing.Color.FromArgb(255, 24, 155, 96)
            : Drawing.Color.FromArgb(255, 84, 88, 102);
        using var fill = new Drawing.SolidBrush(background);
        using var outline = new Drawing.Pen(Drawing.Color.FromArgb(245, 255, 255, 255), 2);
        graphics.FillEllipse(fill, 1, 1, 30, 30);
        graphics.DrawEllipse(outline, 1, 1, 30, 30);

        var text = activeCount > 99 ? "99+" : activeCount.ToString();
        var fontSize = text.Length switch
        {
            1 => 21f,
            2 => 17f,
            _ => 11.5f
        };

        using var font = new Drawing.Font("Segoe UI", fontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Point);
        using var textBrush = new Drawing.SolidBrush(Drawing.Color.White);
        using var stringFormat = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center
        };
        graphics.DrawString(text, font, textBrush, new Drawing.RectangleF(1, 0, 30, 32), stringFormat);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Drawing.Icon.FromHandle(handle);
            _trayRuntimeIcon = (Drawing.Icon)icon.Clone();
            return _trayRuntimeIcon;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Drawing.Image CreateStatusDotImage(Drawing.Color color)
    {
        var bitmap = new Drawing.Bitmap(16, 16, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);
        using var fill = new Drawing.SolidBrush(color);
        graphics.FillEllipse(fill, 4, 4, 8, 8);
        return bitmap;
    }

    private static Drawing.Image? GetServiceStatusDot(ServiceItemViewModel service)
    {
        if (service.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running))
            return RunningStatusDot;

        return service.RuntimeState.State switch
        {
            ProcessState.Running or ProcessState.Starting => RunningStatusDot,
            ProcessState.Stopping => WarningStatusDot,
            ProcessState.Error or ProcessState.StartFailed => FailedStatusDot,
            _ => null
        };
    }

    private static Drawing.Image? GetStepStatusDot(StepRunState state) => state switch
    {
        StepRunState.Running => RunningStatusDot,
        StepRunState.Failed => FailedStatusDot,
        StepRunState.Cancelled => WarningStatusDot,
        _ => null
    };

    private static string ShortTrayText(string text) =>
        text.Length <= 63 ? text : text[..60] + "...";

    private static string FormatVariableLabel(string variable) =>
        string.IsNullOrWhiteSpace(variable) ? LocalizationService.Current.T("EmptyVariable") : variable;

    private IReadOnlyList<string> GetSortedPresetVariables(ServiceItemViewModel service) =>
        _variableUsageStore.Sort(service.Config.Id, service.Config.PresetVariables);

    private IReadOnlyList<string> GetSortedVariablesForStep(ServiceItemViewModel service, ScriptStep step) =>
        step.RunOnStart
            ? _variableUsageStore.Sort(service.Config.Id, service.Config.PresetVariables)
            : _variableUsageStore.Sort(step.Id, step.StepVariables);

    private void AddNewVariableMenuItem(
        Forms.ToolStripMenuItem parent,
        ServiceItemViewModel service,
        Func<string, Task> runAsync)
    {
        parent.DropDownItems.Add(new Forms.ToolStripSeparator());

        var add = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Add"));
        add.Click += async (_, _) =>
        {
            var variable = await PromptForPresetVariableAsync(service);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
            RebuildTrayMenu();
        };
        parent.DropDownItems.Add(add);
    }

    private async Task<string?> PromptForPresetVariableAsync(ServiceItemViewModel service)
    {
        var defaultValue = _variableUsageStore.First(service.Config.Id, service.Config.PresetVariables);
        var dialog = new PresetVariableInputDialog(defaultValue)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await RememberPresetVariableAsync(service.Config, variable, addIfMissing: true);
        return variable;
    }

    private async Task RememberPresetVariableAsync(ServiceConfig service, string? variable, bool addIfMissing)
    {
        if (string.IsNullOrWhiteSpace(variable))
            return;

        var normalized = variable.Trim();
        if (addIfMissing && !service.PresetVariables.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            service.PresetVariables.Add(normalized);
            if (_mainViewModel != null)
                await _mainViewModel.SaveConfigAsync();
            else
                await _configService.SaveAsync(_appConfig);
        }

        _variableUsageStore.Remember(service.Id, normalized);
    }

    private void AddNewStepVariableMenuItem(
        Forms.ToolStripMenuItem parent,
        ServiceItemViewModel service,
        ScriptStep step,
        Func<string, Task> runAsync)
    {
        parent.DropDownItems.Add(new Forms.ToolStripSeparator());

        var add = new Forms.ToolStripMenuItem(LocalizationService.Current.T("Add"));
        add.Click += async (_, _) =>
        {
            var variable = await PromptForStepVariableAsync(service, step);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
            RebuildTrayMenu();
        };
        parent.DropDownItems.Add(add);
    }

    private async Task<string?> PromptForStepVariableAsync(ServiceItemViewModel service, ScriptStep step)
    {
        if (step.RunOnStart)
            return await PromptForPresetVariableAsync(service);

        var defaultValue = _variableUsageStore.First(step.Id, step.StepVariables);
        var dialog = new PresetVariableInputDialog(defaultValue)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await RememberVariableForStepAsync(service.Config, step, variable, addIfMissing: true);
        return variable;
    }

    private async Task RememberVariableForStepAsync(ServiceConfig service, ScriptStep step, string? variable, bool addIfMissing)
    {
        if (step.RunOnStart)
        {
            await RememberPresetVariableAsync(service, variable, addIfMissing);
            return;
        }

        if (string.IsNullOrWhiteSpace(variable))
            return;

        var normalized = variable.Trim();
        if (addIfMissing && !step.StepVariables.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            step.StepVariables.Add(normalized);
            if (_mainViewModel != null)
                await _mainViewModel.SaveConfigAsync();
            else
                await _configService.SaveAsync(_appConfig);
        }

        _variableUsageStore.Remember(step.Id, normalized);
    }

    private static StepRuntimeState? GetStepState(ServiceRuntimeState service, Guid stepId) =>
        service.StepStates.TryGetValue(stepId, out var state) ? state : null;

    private static string FormatStepStateText(StepRunState state) => state switch
    {
        StepRunState.NotRun => LocalizationService.Current.T("StepNotRun"),
        StepRunState.Running => LocalizationService.Current.T("StepRunning"),
        StepRunState.Succeeded => LocalizationService.Current.T("StepSucceeded"),
        StepRunState.Failed => LocalizationService.Current.T("StepFailed"),
        StepRunState.Skipped => LocalizationService.Current.T("StepSkipped"),
        StepRunState.Cancelled => LocalizationService.Current.T("StepCancelled"),
        _ => LocalizationService.Current.T("StateUnknown")
    };

    private async Task StopServiceQuietlyAsync(Guid serviceId)
    {
        if (_processManager == null)
            return;

        try
        {
            await _processManager.StopServiceAsync(serviceId);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 6)
        {
            // Invalid handles can appear while Windows is already tearing down a process tree.
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            RebuildTrayMenu();
        }
    }

    private async Task RestartServiceQuietlyAsync(Guid serviceId, string? variable = null)
    {
        if (_processManager == null)
            return;

        try
        {
            await _processManager.RestartServiceAsync(serviceId, new ServiceStartOptions { Variable = variable });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 6)
        {
            _processManager.StartService(serviceId, new ServiceStartOptions { Variable = variable });
        }
        catch (InvalidOperationException)
        {
            _processManager.StartService(serviceId, new ServiceStartOptions { Variable = variable });
        }
        finally
        {
            RebuildTrayMenu();
        }
    }

    private async Task StopAllQuietlyAsync()
    {
        if (_processManager == null)
            return;

        try
        {
            await _processManager.StopAllAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 6)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            RebuildTrayMenu();
        }
    }

    private void OnAddServiceRequested()
    {
        var dialog = new ServiceConfigDialog(_appConfig.ServiceTemplates);
        if (dialog.ShowDialog() == true && dialog.Result != null && _mainViewModel != null)
        {
            if (_mainViewModel.Services.Any(s => string.Equals(s.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(LocalizationService.Current.F("ServiceNameExists", dialog.Result.Name), "ServicePilot",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newVm = _mainViewModel.AddService(dialog.Result);
            newVm.LogRequested += OnViewLogRequested;
            RememberServiceUse(newVm);
            _ = _mainViewModel.SaveConfigAsync();
            RebuildTrayMenu();
        }
    }

    private async Task OnEditServiceRequested(ServiceItemViewModel vm, Window? owner = null)
    {
        if (_mainViewModel == null)
            return;

        RememberServiceUse(vm);
        var dialog = new ServiceConfigDialog(vm.Config, _appConfig.ServiceTemplates, SaveServiceDraftAsTemplateAsync);
        if (owner != null)
            dialog.Owner = owner;
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        if (_mainViewModel.Services.Any(s => s.Config.Id != vm.Config.Id &&
                                             string.Equals(s.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("ServiceNameExists", dialog.Result.Name), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _mainViewModel.UpdateServiceAsync(dialog.Result);
        RebuildTrayMenu();
    }

    private async Task OnDeleteServiceRequested(ServiceItemViewModel vm)
    {
        if (_mainViewModel == null)
            return;

        RememberServiceUse(vm);
        var confirm = MessageBox.Show(LocalizationService.Current.F("ConfirmDeleteService", vm.Name), "ServicePilot",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await _mainViewModel.RemoveServiceAsync(vm.Config.Id);
    }

    private async Task OnSaveServiceAsTemplateRequested(ServiceItemViewModel vm)
    {
        RememberServiceUse(vm);
        var template = ServiceManagerWindow.ShowSaveTemplateDialog(vm.Config);
        if (template == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("TemplateNameExists", template.Name), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _appConfig.ServiceTemplates.Add(template);
        await _configService.SaveAsync(_appConfig);
        RebuildTrayMenu();
    }

    private async Task SaveServiceDraftAsTemplateAsync(ServiceConfig draft, Window? owner)
    {
        var template = ServiceManagerWindow.ShowSaveTemplateDialog(draft, owner);
        if (template == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("TemplateNameExists", template.Name), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _appConfig.ServiceTemplates.Add(template);
        await _configService.SaveAsync(_appConfig);
        RebuildTrayMenu();
    }

    private void OnManageServicesRequested()
    {
        if (_mainViewModel == null || _processManager == null)
            return;

        var window = new ServiceManagerWindow(
            _mainViewModel,
            _configService,
            _appConfig,
            _processManager,
            OnViewLogRequested,
            RebuildTrayMenu,
            _variableUsageStore);
        window.Show();
    }

    private void OnManageTemplatesRequested()
    {
        var window = new TemplateManagerWindow(_appConfig, _configService, RebuildTrayMenu);
        window.Show();
    }

    private void OnServiceAdded(ServiceItemViewModel vm)
    {
        vm.LogRequested += OnViewLogRequested;
        RebuildTrayMenu();
    }

    private void OnServiceRemoved(Guid serviceId)
    {
        if (_logWindows.TryGetValue(serviceId, out var logWindow))
            logWindow.Close();

        _logBuffers.Remove(serviceId);
        RebuildTrayMenu();
    }

    private void OnViewLogRequested(ServiceItemViewModel vm)
    {
        if (_processManager == null)
            return;

        RememberServiceUse(vm);
        RebuildTrayMenu();

        if (_logWindows.TryGetValue(vm.Config.Id, out var existing) && existing.IsLoaded)
        {
            existing.Activate();
            return;
        }

        var logWindow = new LogWindow(
            vm,
            _processManager,
            _variableUsageStore,
            RememberPresetVariableAsync,
            RememberVariableForStepAsync,
            OnEditServiceRequested);

        void OnOutput(Guid id, LogEntry entry)
        {
            if (id == vm.Config.Id)
                logWindow.AddLog(entry);
        }

        _processManager.ServiceOutput += OnOutput;
        logWindow.Closed += (_, _) =>
        {
            _processManager.ServiceOutput -= OnOutput;
            _logWindows.Remove(vm.Config.Id);
        };

        _logWindows[vm.Config.Id] = logWindow;
        logWindow.Show();
        logWindow.LoadLogs(GetBufferedLogs(vm.Config.Id));
    }

    private void OnServiceOutput(Guid serviceId, LogEntry entry)
    {
        if (!_logBuffers.TryGetValue(serviceId, out var buffer))
        {
            buffer = new List<LogEntry>();
            _logBuffers[serviceId] = buffer;
        }

        buffer.Add(entry);
        if (buffer.Count > MaxBufferedLogEntries)
            buffer.RemoveAt(0);

        if (ShouldShowErrorNotification(entry))
            Dispatcher.BeginInvoke(() => ShowErrorNotification(serviceId, entry));
    }

    private IReadOnlyList<LogEntry> GetBufferedLogs(Guid serviceId)
    {
        return _logBuffers.TryGetValue(serviceId, out var buffer)
            ? buffer.ToList()
            : Array.Empty<LogEntry>();
    }

    private static bool ShouldShowErrorNotification(LogEntry entry)
    {
        if (entry.Level != LogLevel.Error)
            return false;

        if (string.Equals(entry.Source, "system", StringComparison.OrdinalIgnoreCase))
            return true;

        var message = entry.Message;
        return message.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("退出码", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("exit code", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowErrorNotification(Guid serviceId, LogEntry entry)
    {
        if (_isExiting || _trayIcon == null)
            return;

        var now = DateTime.Now;
        if (_lastErrorNotificationAt.TryGetValue(serviceId, out var last) &&
            now - last < TimeSpan.FromSeconds(5))
            return;

        _lastErrorNotificationAt[serviceId] = now;

        var serviceName = _mainViewModel?.Services
            .FirstOrDefault(service => service.Config.Id == serviceId)
            ?.Name ?? serviceId.ToString();
        var title = ShortBalloonText(LocalizationService.Current.F("ServiceErrorTitle", serviceName), 63);
        var message = CompactBalloonMessage(entry.Message);
        if (!string.IsNullOrWhiteSpace(entry.StepName))
            message = $"{entry.StepName}: {message}";

        try
        {
            _trayIcon.ShowBalloonTip(6000, title, ShortBalloonText(message, 220), Forms.ToolTipIcon.Error);
        }
        catch
        {
            // Notifications are best-effort and must never break service control.
        }
    }

    private static string CompactBalloonMessage(string message)
    {
        var cleaned = AnsiEscapeRegex.Replace(message, string.Empty).Replace("\r", "\n");
        var lines = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToList();

        return lines.Count == 0 ? LocalizationService.Current.T("RunFailedOpenLogs") : string.Join(Environment.NewLine, lines);
    }

    private static string ShortBalloonText(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";

    private async Task<CommandResponse> HandlePipeCommandAsync(string[] args)
    {
        if (_commandProcessor == null)
            return CommandResponse.Error("命令处理器尚未初始化。");

        var operation = Dispatcher.InvokeAsync(() => _commandProcessor.ExecuteAsync(args));
        return await await operation.Task;
    }

    private async Task RequestExitFromCommandAsync()
    {
        await Task.Delay(200);
        var operation = Dispatcher.InvokeAsync(ExitAsync);
        await await operation.Task;
    }

    private async Task ExitAsync()
    {
        if (_isExiting)
            return;

        _isExiting = true;

        if (_processManager != null)
        {
            await StopAllQuietlyAsync();
            _processManager.Dispose();
        }

        _commandPipeServer?.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayRuntimeIcon?.Dispose();

        Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (!_isExiting && _processManager != null)
        {
            await StopAllQuietlyAsync();
            _processManager.Dispose();
        }

        _commandPipeServer?.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayRuntimeIcon?.Dispose();

        if (_ownsMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
