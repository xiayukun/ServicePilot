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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Wpf.Ui.Appearance;
using Wpf.Ui.Extensions;
using WpfMenuItem = Wpf.Ui.Controls.MenuItem;
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
    private readonly List<ServiceManagerWindow> _serviceManagerWindows = new();
    private readonly List<TemplateManagerWindow> _templateManagerWindows = new();

    private TaskbarIcon? _trayIcon;
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

    [Flags]
    private enum RuntimeRefreshScope
    {
        None = 0,
        Services = 1,
        Templates = 2,
        RecentUsage = 4,
        Tray = 8,
        Logs = 16,
        All = Services | Templates | RecentUsage | Tray | Logs
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
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
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("AlreadyRunning"), "ServicePilot",
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
                _variableUsageStore,
                ReloadConfigAsync);
            _commandPipeServer = new CommandPipeServer(HandlePipeCommandAsync);
            _commandPipeServer.Start();

            CreateTrayIcon();

            foreach (var state in _processManager.Services.Where(s => s.Config.AutoStart))
                _processManager.StartService(state.Config.Id);
        }
        catch (Exception ex)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("StartupFailed", ex), LocalizationService.Current.T("StartupErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("UiThreadException", args.Exception), LocalizationService.Current.T("ServicePilotErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WpfMessageBoxHelper.Show(LocalizationService.Current.F("FatalThreadException", ex), LocalizationService.Current.T("ServicePilotErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("AsyncTaskException", args.Exception?.InnerException ?? args.Exception),
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
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "ServicePilot",
                Visibility = Visibility.Visible,
                Icon = CreateTrayIconWithBadge(0),
                MenuActivation = PopupActivationMode.RightClick
            };

            _trayIcon.TrayMouseDoubleClick += (_, _) =>
            {
                RebuildTrayMenu();
            };

            RebuildTrayMenu();
        }
        catch (Exception ex)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TrayCreateFailed", ex.Message),
                LocalizationService.Current.T("ServicePilotErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static ImageSource? ConvertDrawingImageToImageSource(Drawing.Image? source)
    {
        if (source == null)
            return null;

        try
        {
            using var ms = new MemoryStream();
            source.Save(ms, Drawing.Imaging.ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Lazy<ImageSource?> RunningStatusDotWpf = new(() => ConvertDrawingImageToImageSource(RunningStatusDot));
    private static readonly Lazy<ImageSource?> FailedStatusDotWpf = new(() => ConvertDrawingImageToImageSource(FailedStatusDot));
    private static readonly Lazy<ImageSource?> WarningStatusDotWpf = new(() => ConvertDrawingImageToImageSource(WarningStatusDot));

    private static System.Windows.Controls.Primitives.Popup? FindPopupChild(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.Primitives.Popup popup)
                return popup;
            var result = FindPopupChild(child);
            if (result != null)
                return result;
        }
        return null;
    }

    private static void ApplySubmenuOffset(MenuItem item)
    {
        // 不干预子菜单位置，完全由 WPF 默认行为控制
    }

    private static void SetMenuItemIcon(MenuItem item, Drawing.Image? dot)
    {
        var source = dot == RunningStatusDot ? RunningStatusDotWpf.Value :
                     dot == FailedStatusDot ? FailedStatusDotWpf.Value :
                     dot == WarningStatusDot ? WarningStatusDotWpf.Value :
                     ConvertDrawingImageToImageSource(dot);

        if (source != null)
        {
            item.Icon = new System.Windows.Controls.Image
            {
                Source = source,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0)
            };
        }
    }

    private void RebuildTrayMenu()
    {
        if (_isExiting || _trayIcon == null || _mainViewModel == null || _processManager == null)
            return;

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.VerticalOffset = 4;
        menu.HorizontalOffset = -8;

        foreach (var service in GetSortedServicesForDisplay())
        {
            var state = service.RuntimeState.State;
            var serviceMenu = new WpfMenuItem
            {
                Header = service.Name,
                ToolTip = FormatStatusText(state)
            };
            ApplySubmenuOffset(serviceMenu);

            SetMenuItemIcon(serviceMenu, GetServiceStatusDot(service));

            AddStepItems(serviceMenu, service);

            serviceMenu.Items.Add(new System.Windows.Controls.Separator());

            var hasRunning = service.RuntimeState.State is ProcessState.Running or ProcessState.Starting ||
                             service.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running);
            var stop = new WpfMenuItem { Header = LocalizationService.Current.T("Stop"), IsEnabled = hasRunning };
            stop.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await StopServiceQuietlyAsync(service.Config.Id);
            };
            serviceMenu.Items.Add(stop);

            var viewLog = new WpfMenuItem { Header = LocalizationService.Current.T("ViewLogs") };
            viewLog.Click += (_, _) => OnViewLogRequested(service);
            serviceMenu.Items.Add(viewLog);

            serviceMenu.Items.Add(new System.Windows.Controls.Separator());

            var edit = new WpfMenuItem { Header = LocalizationService.Current.T("Edit") };
            edit.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await OnEditServiceRequested(service);
            };
            serviceMenu.Items.Add(edit);

            var delete = new WpfMenuItem { Header = LocalizationService.Current.T("Delete") };
            delete.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await OnDeleteServiceRequested(service);
            };
            serviceMenu.Items.Add(delete);

            var saveAsTemplate = new WpfMenuItem { Header = LocalizationService.Current.T("SaveAsTemplate") };
            saveAsTemplate.Click += async (_, _) =>
            {
                RememberServiceUse(service);
                await OnSaveServiceAsTemplateRequested(service);
            };
            serviceMenu.Items.Add(saveAsTemplate);

            menu.Items.Add(serviceMenu);
        }

        if (_mainViewModel.Services.Count > 0)
            menu.Items.Add(new System.Windows.Controls.Separator());

        var addService = new WpfMenuItem { Header = LocalizationService.Current.T("AddService") };
        addService.Click += (_, _) => OnAddServiceRequested();
        menu.Items.Add(addService);

        var manageServices = new WpfMenuItem { Header = LocalizationService.Current.T("ManageServices") };
        manageServices.Click += (_, _) => OnManageServicesRequested();
        menu.Items.Add(manageServices);

        var manageTemplates = new WpfMenuItem { Header = LocalizationService.Current.T("ManageTemplates") };
        manageTemplates.Click += (_, _) => OnManageTemplatesRequested();
        menu.Items.Add(manageTemplates);

        var copyHelpForAi = new WpfMenuItem { Header = LocalizationService.Current.T("CopyHelpForAi") };
        copyHelpForAi.Click += (_, _) => OnCopyHelpForAiRequested();
        menu.Items.Add(copyHelpForAi);

        var stopAll = new WpfMenuItem
        {
            Header = LocalizationService.Current.T("StopAllServices"),
            IsEnabled = _mainViewModel.Services.Any(s => s.RuntimeState.State is ProcessState.Running or ProcessState.Starting ||
                                                         s.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running))
        };
        stopAll.Click += async (_, _) => await StopAllQuietlyAsync();
        menu.Items.Add(stopAll);

        menu.Items.Add(new System.Windows.Controls.Separator());

        AddLanguageMenu(menu);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var status = new WpfMenuItem { Header = GetTrayStatusText(), IsEnabled = false };
        menu.Items.Add(status);

        var exit = new WpfMenuItem { Header = LocalizationService.Current.T("Exit") };
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(exit);

        _trayIcon.ContextMenu = menu;
        _trayIcon.ToolTipText = ShortTrayText(GetTrayStatusText());
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

    private void AddStepItems(MenuItem parent, ServiceItemViewModel service)
    {
        var steps = service.Config.ScriptSteps.OrderBy(s => s.Order).ToList();
        var serviceBusy = service.RuntimeState.State is ProcessState.Starting or ProcessState.Stopping;

        var any = false;
        foreach (var step in steps)
        {
            if (step.Kind == StepKind.Composite)
            {
                parent.Items.Add(CreateCompositeMenuItem(service, step, serviceBusy));
                any = true;
            }
            else if (!string.IsNullOrWhiteSpace(step.Content))
            {
                parent.Items.Add(CreateActionMenuItem(service, step));
                any = true;
            }
        }

        if (!any)
        {
            parent.Items.Add(new WpfMenuItem { Header = LocalizationService.Current.T("NoActions"), IsEnabled = false });
        }
    }

    private MenuItem CreateCompositeMenuItem(ServiceItemViewModel service, ScriptStep composite, bool serviceBusy)
    {
        var running = service.RuntimeState.State is ProcessState.Running or ProcessState.Starting;
        var variableMember = ScriptDefinitionService.FindVariableMember(service.Config, composite);
        var label = composite.Name;

        if (variableMember == null)
        {
            var item = new WpfMenuItem
            {
                Header = label,
                IsEnabled = !serviceBusy && !running
            };
            if (running)
                SetMenuItemIcon(item, RunningStatusDot);
            item.Click += (_, _) =>
            {
                RememberServiceUse(service);
                _processManager?.RunComposite(service.Config.Id, composite.Id);
                RebuildTrayMenu();
            };
            return item;
        }

        var menu = new WpfMenuItem
        {
            Header = label,
            IsEnabled = !serviceBusy && !running
        };
        if (running)
            SetMenuItemIcon(menu, RunningStatusDot);
        AddVariableChoices(menu, service, variableMember, variable =>
        {
            RememberServiceUse(service);
            _processManager?.RunComposite(service.Config.Id, composite.Id, variable);
            return Task.CompletedTask;
        });
        ApplySubmenuOffset(menu);
        return menu;
    }

    private MenuItem CreateActionMenuItem(ServiceItemViewModel service, ScriptStep action)
    {
        var stepState = GetStepState(service.RuntimeState, action.Id)?.State ?? StepRunState.NotRun;
        var isRunning = stepState == StepRunState.Running;

        if (!action.UseVariable)
        {
            var item = new WpfMenuItem
            {
                Header = action.Name,
                IsEnabled = !isRunning,
                ToolTip = FormatStepStateText(stepState)
            };
            SetMenuItemIcon(item, GetStepStatusDot(stepState));
            item.Click += (_, _) =>
            {
                RememberServiceUse(service);
                _processManager?.RunStep(service.Config.Id, action.Id);
                RebuildTrayMenu();
            };
            return item;
        }

        var menu = new WpfMenuItem
        {
            Header = action.Name,
            IsEnabled = !isRunning,
            ToolTip = FormatStepStateText(stepState)
        };
        SetMenuItemIcon(menu, GetStepStatusDot(stepState));
        AddVariableChoices(menu, service, action, variable =>
        {
            RememberServiceUse(service);
            _processManager?.RunStep(service.Config.Id, action.Id, variable);
            return Task.CompletedTask;
        });
        ApplySubmenuOffset(menu);
        return menu;
    }

    private void AddVariableChoices(
        MenuItem menu,
        ServiceItemViewModel service,
        ScriptStep variableStep,
        Func<string?, Task> runAsync)
    {
        foreach (var variable in GetSortedVariablesForStep(variableStep))
        {
            var variableItem = new WpfMenuItem { Header = FormatVariableLabel(variable) };
            variableItem.Click += async (_, _) =>
            {
                await RememberVariableForStepAsync(service.Config, variableStep, variable, addIfMissing: false);
                await runAsync(variable);
                RebuildTrayMenu();
            };
            menu.Items.Add(variableItem);
        }

        AddNewStepVariableMenuItem(menu, service, variableStep, variable => runAsync(variable));
    }

    private void AddLanguageMenu(System.Windows.Controls.ContextMenu menu)
    {
        var languageMenu = new WpfMenuItem { Header = LocalizationService.Current.T("Language") };
        AddLanguageOption(languageMenu, LocalizationService.Auto);
        AddLanguageOption(languageMenu, LocalizationService.Chinese);
        AddLanguageOption(languageMenu, LocalizationService.English);
        languageMenu.Items.Add(new System.Windows.Controls.Separator());
        languageMenu.Items.Add(new WpfMenuItem
        {
            Header = LocalizationService.Current.F("LanguageCurrent", LocalizationService.Current.DisplayLanguageName(LocalizationService.Current.LanguageSetting)),
            IsEnabled = false
        });
        ApplySubmenuOffset(languageMenu);
        menu.Items.Add(languageMenu);
    }

    private void AddLanguageOption(MenuItem parent, string languageSetting)
    {
        var item = new WpfMenuItem
        {
            Header = LocalizationService.Current.DisplayLanguageName(languageSetting),
            IsChecked = string.Equals(LocalizationService.Current.LanguageSetting, languageSetting, StringComparison.OrdinalIgnoreCase),
            StaysOpenOnClick = true
        };
        item.Click += async (_, _) => await SetLanguageAsync(languageSetting);
        parent.Items.Add(item);
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

    private string GetTrayStatusText()
    {
        if (_mainViewModel == null)
            return "ServicePilot: 0 running";

        var total = _mainViewModel.Services.Count;
        var activeCount = GetActiveProcessCount();
        var failed = _mainViewModel.Services.Count(s => s.RuntimeState.State is ProcessState.Error or ProcessState.StartFailed);

        if (activeCount == 0)
            return LocalizationService.Current.F("TrayStatusEmpty", total, failed);

        return LocalizationService.Current.F("TrayStatusActive", activeCount, total, failed);
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
            1 => 17f,
            2 => 14f,
            _ => 10f
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

    private IReadOnlyList<string> GetSortedVariablesForStep(ScriptStep step) =>
        _variableUsageStore.Sort(step.Id, step.StepVariables);

    private void AddNewStepVariableMenuItem(
        MenuItem parent,
        ServiceItemViewModel service,
        ScriptStep step,
        Func<string, Task> runAsync)
    {
        parent.Items.Add(new System.Windows.Controls.Separator());

        var add = new WpfMenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForStepVariableAsync(service, step);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
            RebuildTrayMenu();
        };
        parent.Items.Add(add);
    }

    private async Task<string?> PromptForStepVariableAsync(ServiceItemViewModel service, ScriptStep step)
    {
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
                WpfMessageBoxHelper.Show(LocalizationService.Current.F("ServiceNameExists", dialog.Result.Name), "ServicePilot",
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
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("ServiceNameExists", dialog.Result.Name), "ServicePilot",
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
        var confirm = WpfMessageBoxHelper.Show(LocalizationService.Current.F("ConfirmDeleteService", vm.Name), "ServicePilot",
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
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateNameExists", template.Name), "ServicePilot",
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
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateNameExists", template.Name), "ServicePilot",
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
        _serviceManagerWindows.Add(window);
        window.Closed += (_, _) => _serviceManagerWindows.Remove(window);
        window.Show();
    }

    private void OnManageTemplatesRequested()
    {
        var window = new TemplateManagerWindow(_appConfig, _configService, RebuildTrayMenu);
        _templateManagerWindows.Add(window);
        window.Closed += (_, _) => _templateManagerWindows.Remove(window);
        window.Show();
    }

    private void OnCopyHelpForAiRequested()
    {
        var window = new AiHelpWindow();
        window.Show();
        window.Activate();
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
            _trayIcon.ShowBalloonTip(title, ShortBalloonText(message, 220), BalloonIcon.Error);
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

        var operation = Dispatcher.InvokeAsync(async () =>
        {
            var response = await _commandProcessor.ExecuteAsync(args);
            if (response.ExitCode == 0)
                RefreshAfterCommand(args);
            return response;
        });
        return await await operation.Task;
    }

    private void RefreshAfterCommand(string[] args)
    {
        var scope = ClassifyCommandRefresh(args);
        if (scope == RuntimeRefreshScope.None || _isExiting)
            return;

        if (scope.HasFlag(RuntimeRefreshScope.Services) || scope.HasFlag(RuntimeRefreshScope.RecentUsage))
        {
            foreach (var window in _serviceManagerWindows.ToList())
                window.RefreshAfterConfigChanged();
        }

        if (scope.HasFlag(RuntimeRefreshScope.Templates))
        {
            foreach (var window in _templateManagerWindows.ToList())
                window.RefreshAfterConfigChanged();
        }

        if (scope.HasFlag(RuntimeRefreshScope.Logs) || scope.HasFlag(RuntimeRefreshScope.Services))
        {
            foreach (var window in _logWindows.Values.ToList())
                window.RefreshAfterConfigChanged();
        }

        if (scope.HasFlag(RuntimeRefreshScope.Tray) ||
            scope.HasFlag(RuntimeRefreshScope.Services) ||
            scope.HasFlag(RuntimeRefreshScope.RecentUsage))
        {
            RebuildTrayMenu();
        }
    }

    private static RuntimeRefreshScope ClassifyCommandRefresh(string[] args)
    {
        if (args.Length == 0)
            return RuntimeRefreshScope.None;

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch
        {
            "add" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            "remove" or "delete" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            "start" or "stop" or "restart" => RuntimeRefreshScope.Services | RuntimeRefreshScope.RecentUsage | RuntimeRefreshScope.Tray,
            "logs" or "log" => RuntimeRefreshScope.Services | RuntimeRefreshScope.RecentUsage | RuntimeRefreshScope.Tray,
            "service" => ClassifyServiceCommandRefresh(rest),
            "step" => ClassifyStepCommandRefresh(rest),
            "template" => ClassifyTemplateCommandRefresh(rest),
            "config" => RuntimeRefreshScope.All,
            _ => RuntimeRefreshScope.None
        };
    }

    private static RuntimeRefreshScope ClassifyServiceCommandRefresh(string[] args)
    {
        if (args.Length == 0)
            return RuntimeRefreshScope.None;

        var subCommand = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return subCommand switch
        {
            "add" or "edit" or "remove" or "delete" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            "start" or "stop" or "restart" or "logs" or "log" => RuntimeRefreshScope.Services | RuntimeRefreshScope.RecentUsage | RuntimeRefreshScope.Tray,
            "step" => ClassifyStepCommandRefresh(rest),
            _ => RuntimeRefreshScope.None
        };
    }

    private static RuntimeRefreshScope ClassifyStepCommandRefresh(string[] args)
    {
        if (args.Length == 0)
            return RuntimeRefreshScope.None;

        var subCommand = args[0].ToLowerInvariant();
        return subCommand switch
        {
            "run" => RuntimeRefreshScope.Services | RuntimeRefreshScope.RecentUsage | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            "add" or "edit" or "remove" or "delete" or "move" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            "variable-add" or "var-add" or "variable-remove" or "variable-delete" or "var-remove" or "var-delete" or "variable-clear" or "var-clear" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            _ => RuntimeRefreshScope.None
        };
    }

    private static RuntimeRefreshScope ClassifyTemplateCommandRefresh(string[] args)
    {
        if (args.Length == 0)
            return RuntimeRefreshScope.None;

        var subCommand = args[0].ToLowerInvariant();
        return subCommand switch
        {
            "add" or "edit" or "remove" or "delete" or "import" or "save-from-service" => RuntimeRefreshScope.Templates | RuntimeRefreshScope.Tray,
            "apply" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Templates | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            "step-variable-add" or "step-var-add" or "step-variable-remove" or "step-variable-delete" or "step-var-remove" or "step-var-delete" or "step-variable-clear" or "step-var-clear" => RuntimeRefreshScope.Templates | RuntimeRefreshScope.Tray,
            "create" => RuntimeRefreshScope.Services | RuntimeRefreshScope.Tray | RuntimeRefreshScope.Logs,
            _ => RuntimeRefreshScope.None
        };
    }

    private async Task<CommandResponse> ReloadConfigAsync()
    {
        if (_processManager == null || _mainViewModel == null)
            return CommandResponse.Error("托盘实例尚未初始化。", 2);

        try
        {
            // P0: Ensure any pending CLI modifications are persisted to disk before reloading.
            await _configService.SaveAsync(_appConfig);

            var reloadedConfig = await _configService.LoadAsync();

            // Update the in-memory AppConfig
            _appConfig.Services.Clear();
            _appConfig.Services.AddRange(reloadedConfig.Services);
            _appConfig.ServiceTemplates.Clear();
            _appConfig.ServiceTemplates.AddRange(reloadedConfig.ServiceTemplates);
            _appConfig.Settings = reloadedConfig.Settings ?? new AppSettings();

            // Refresh ProcessManager: reload service configs
            _processManager.LoadConfigs(_appConfig.Services);

            // Refresh MainViewModel: rebuild the Services collection
            _mainViewModel.Services.Clear();
            foreach (var state in _processManager.Services)
            {
                var vm = new ServiceItemViewModel(state, _processManager);
                vm.LogRequested += OnViewLogRequested;
                _mainViewModel.Services.Add(vm);
            }

            // Refresh UI
            RebuildTrayMenu();
            foreach (var window in _serviceManagerWindows.ToList())
                window.RefreshAfterConfigChanged();
            foreach (var window in _templateManagerWindows.ToList())
                window.RefreshAfterConfigChanged();
            foreach (var window in _logWindows.Values.ToList())
                window.RefreshAfterConfigChanged();

            return CommandResponse.Ok("已重新加载配置");
        }
        catch (Exception ex)
        {
            return CommandResponse.Error($"重新加载配置失败: {ex.Message}", 2);
        }
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
