using System.Windows;
using System.Windows.Controls;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels;

namespace ServicePilot.Views;

public partial class ServiceManagerWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly ConfigService _configService;
    private readonly AppConfig _appConfig;
    private readonly ProcessManager _processManager;
    private readonly Action<ServiceItemViewModel> _viewLog;
    private readonly Action _changed;
    private readonly PresetVariableUsageStore _variableUsageStore;

    public ServiceManagerWindow(
        MainViewModel mainViewModel,
        ConfigService configService,
        AppConfig appConfig,
        ProcessManager processManager,
        Action<ServiceItemViewModel> viewLog,
        Action changed,
        PresetVariableUsageStore variableUsageStore)
    {
        _mainViewModel = mainViewModel;
        _configService = configService;
        _appConfig = appConfig;
        _processManager = processManager;
        _viewLog = viewLog;
        _changed = changed;
        _variableUsageStore = variableUsageStore;

        InitializeComponent();
        ApplyLocalization();
        RefreshServicesGrid();
        UpdateActionButtons();
        _processManager.ServiceStateChanged += OnServiceStateChanged;
        _processManager.StepStateChanged += OnStepStateChanged;
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _processManager.ServiceStateChanged -= OnServiceStateChanged;
            _processManager.StepStateChanged -= OnStepStateChanged;
            LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
        };
    }

    private ServiceItemViewModel? SelectedService => ServicesGrid.SelectedItem as ServiceItemViewModel;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            foreach (var vm in _mainViewModel.Services)
                vm.RefreshLanguage();
            RefreshServicesGrid();
        });
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.Current.T("ManageServices");
        AddButton.Content = LocalizationService.Current.T("Add");
        EditButton.Content = LocalizationService.Current.T("Edit");
        DeleteButton.Content = LocalizationService.Current.T("Delete");
        StartButton.Content = LocalizationService.Current.T("Start");
        RunStepButton.Content = LocalizationService.Current.T("RunStep");
        StopButton.Content = LocalizationService.Current.T("Stop");
        RestartButton.Content = LocalizationService.Current.T("Restart");
        LogsButton.Content = LocalizationService.Current.T("ViewLogs");
        SaveTemplateButton.Content = LocalizationService.Current.T("SaveAsTemplate");
        StatusColumn.Header = LocalizationService.Current.T("Status");
        NameColumn.Header = LocalizationService.Current.T("Name");
        WorkingDirectoryColumn.Header = LocalizationService.Current.T("WorkingDirectory");
        StepsColumn.Header = LocalizationService.Current.T("Steps");
        VariablesColumn.Header = LocalizationService.Current.T("Variables");
        AutoStartColumn.Header = LocalizationService.Current.T("AutoStart");
    }

    private void OnServiceStateChanged(Guid serviceId, ProcessState state)
    {
        Dispatcher.Invoke(() =>
        {
            ServicesGrid.Items.Refresh();
            UpdateActionButtons();
        });
    }

    private void OnStepStateChanged(Guid serviceId, StepRuntimeState state)
    {
        Dispatcher.Invoke(() =>
        {
            ServicesGrid.Items.Refresh();
            UpdateActionButtons();
        });
    }

    private void RefreshServicesGrid(Guid? selectedServiceId = null)
    {
        var selectedId = selectedServiceId ?? SelectedService?.Config.Id;
        var services = _variableUsageStore.SortServices(
            _mainViewModel.Services,
            service => service.Config.Id,
            service => service.Config.SortOrder,
            service => service.Name);

        ServicesGrid.ItemsSource = services;
        if (selectedId.HasValue)
            ServicesGrid.SelectedItem = services.FirstOrDefault(service => service.Config.Id == selectedId.Value);
    }

    private void RememberServiceUse(ServiceItemViewModel vm)
    {
        _variableUsageStore.RememberService(vm.Config.Id);
        RefreshServicesGrid(vm.Config.Id);
        _changed();
    }

    private void ServicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        var vm = SelectedService;
        if (vm == null)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            RestartButton.IsEnabled = false;
            RunStepButton.IsEnabled = false;
            return;
        }

        var state = vm.RuntimeState.State;
        var hasRunningStep = vm.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running);
        StartButton.IsEnabled = state is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed;
        StopButton.IsEnabled = state is ProcessState.Running or ProcessState.Starting or ProcessState.Stopping || hasRunningStep;
        RestartButton.IsEnabled = state is not ProcessState.Starting and not ProcessState.Stopping;
        RunStepButton.IsEnabled = vm.Config.ScriptSteps.Any(step => !string.IsNullOrWhiteSpace(step.Content));
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ServiceConfigDialog(_appConfig.ServiceTemplates) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        if (_mainViewModel.Services.Any(s => string.Equals(s.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("ServiceNameExists", dialog.Result.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newVm = _mainViewModel.AddService(dialog.Result);
        RememberServiceUse(newVm);
        await _mainViewModel.SaveConfigAsync();
        RefreshServicesGrid(newVm.Config.Id);
        _changed();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;

        RememberServiceUse(vm);
        var dialog = new ServiceConfigDialog(vm.Config, _appConfig.ServiceTemplates, SaveServiceDraftAsTemplateAsync) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        if (_mainViewModel.Services.Any(s => s.Config.Id != vm.Config.Id &&
                                             string.Equals(s.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("ServiceNameExists", dialog.Result.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _mainViewModel.UpdateServiceAsync(dialog.Result);
        RefreshServicesGrid(dialog.Result.Id);
        _changed();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;

        RememberServiceUse(vm);
        var confirm = MessageBox.Show(LocalizationService.Current.F("ConfirmDeleteService", vm.Name), "ServicePilot",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await _mainViewModel.RemoveServiceAsync(vm.Config.Id);
        RefreshServicesGrid();
        _changed();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        if (ShowVariableMenu(sender, vm, variable => _processManager.StartService(vm.Config.Id, new ServiceStartOptions { Variable = variable })))
            return;

        RememberServiceUse(vm);
        _processManager.StartService(vm.Config.Id);
        UpdateActionButtons();
        _changed();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        RememberServiceUse(vm);
        UpdateActionButtons();
        await _processManager.StopServiceAsync(vm.Config.Id);
        UpdateActionButtons();
        _changed();
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        if (ShowVariableMenu(sender, vm, variable => _ = RestartWithVariableAsync(vm, variable)))
            return;

        RememberServiceUse(vm);
        await _processManager.RestartServiceAsync(vm.Config.Id);
        UpdateActionButtons();
        _changed();
    }

    private void RunStep_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        RememberServiceUse(vm);

        var steps = vm.Config.ScriptSteps
            .Where(s => !string.IsNullOrWhiteSpace(s.Content))
            .OrderBy(s => s.Order)
            .ToList();
        if (steps.Count == 0)
            return;

        var menu = new ContextMenu();
        AddStepGroup(menu, vm, steps.Where(step => step.RunOnStart), LocalizationService.Current.T("StartupSteps"));
        AddStepGroup(menu, vm, steps.Where(step => !step.RunOnStart), LocalizationService.Current.T("ManualSteps"));

        OpenMenu(sender, menu);
    }

    private void AddStepGroup(ItemsControl menu, ServiceItemViewModel vm, IEnumerable<ScriptStep> sourceSteps, string header)
    {
        var steps = sourceSteps.ToList();
        if (steps.Count == 0)
            return;

        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem { Header = header, IsEnabled = false });
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var displayHeader = step.RunOnStart ? $"{i + 1}. {step.Name}" : step.Name;
            menu.Items.Add(CreateRunStepMenuItem(vm, step, displayHeader));
        }
    }

    private MenuItem CreateRunStepMenuItem(ServiceItemViewModel vm, ScriptStep step, string header)
    {
        var state = GetStepState(vm.RuntimeState, step.Id);
        var stepState = state?.State ?? StepRunState.NotRun;
        var isRunning = stepState == StepRunState.Running;
        var variables = GetSortedVariablesForStep(vm, step);
        if (!step.UseVariable || (variables.Count == 0 && step.RunOnStart))
        {
            var item = new MenuItem
            {
                Header = CreateStatusHeader(header, GetStepStatusBrush(stepState)),
                ToolTip = FormatStepStateText(stepState),
                IsEnabled = !isRunning
            };
            item.Click += (_, _) =>
            {
                RememberServiceUse(vm);
                _processManager.RunStep(vm.Config.Id, step.Id);
                UpdateActionButtons();
                _changed();
            };
            return item;
        }

        var stepMenu = new MenuItem
        {
            Header = CreateStatusHeader(header, GetStepStatusBrush(stepState)),
            ToolTip = FormatStepStateText(stepState),
            IsEnabled = !isRunning
        };
        foreach (var variable in variables)
        {
            var variableItem = new MenuItem { Header = variable };
            variableItem.Click += async (_, _) =>
            {
                RememberServiceUse(vm);
                await RememberVariableForStepAsync(vm.Config, step, variable, addIfMissing: false);
                _processManager.RunStep(vm.Config.Id, step.Id, variable);
                UpdateActionButtons();
                _changed();
            };
            stepMenu.Items.Add(variableItem);
        }

        AddNewStepVariableMenuItem(stepMenu, vm, step, variable =>
        {
            RememberServiceUse(vm);
            _processManager.RunStep(vm.Config.Id, step.Id, variable);
            return Task.CompletedTask;
        });
        return stepMenu;
    }

    private static object CreateStatusHeader(string text, System.Windows.Media.Brush? dotBrush)
    {
        if (dotBrush == null)
            return text;

        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        panel.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = dotBrush,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private static System.Windows.Media.Brush? GetStepStatusBrush(StepRunState state) => state switch
    {
        StepRunState.Running => System.Windows.Media.Brushes.ForestGreen,
        StepRunState.Failed => System.Windows.Media.Brushes.Firebrick,
        StepRunState.Cancelled => System.Windows.Media.Brushes.DarkOrange,
        _ => null
    };

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        RememberServiceUse(vm);
        _viewLog(vm);
    }

    private async Task RestartWithVariableAsync(ServiceItemViewModel vm, string? variable)
    {
        await _processManager.RestartServiceAsync(vm.Config.Id, new ServiceStartOptions { Variable = variable });
        _changed();
    }

    private bool ShowVariableMenu(object sender, ServiceItemViewModel vm, Action<string?> run)
    {
        var variables = GetSortedPresetVariables(vm);
        if (variables.Count == 0)
            return false;

        var menu = new ContextMenu();
        foreach (var variable in variables)
        {
            var item = new MenuItem { Header = variable };
            item.Click += async (_, _) =>
            {
                RememberServiceUse(vm);
                await RememberPresetVariableAsync(vm.Config, variable, addIfMissing: false);
                run(variable);
                _changed();
            };
            menu.Items.Add(item);
        }
        AddNewVariableMenuItem(menu, vm, variable =>
        {
            RememberServiceUse(vm);
            run(variable);
            _changed();
            return Task.CompletedTask;
        });

        OpenMenu(sender, menu);
        return true;
    }

    private IReadOnlyList<string> GetSortedPresetVariables(ServiceItemViewModel vm) =>
        _variableUsageStore.Sort(vm.Config.Id, vm.Config.PresetVariables);

    private IReadOnlyList<string> GetSortedVariablesForStep(ServiceItemViewModel vm, ScriptStep step) =>
        step.RunOnStart
            ? _variableUsageStore.Sort(vm.Config.Id, vm.Config.PresetVariables)
            : _variableUsageStore.Sort(step.Id, step.StepVariables);

    private void AddNewVariableMenuItem(ItemsControl parent, ServiceItemViewModel vm, Func<string, Task> runAsync)
    {
        parent.Items.Add(new Separator());

        var add = new MenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForPresetVariableAsync(vm);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
            ServicesGrid.Items.Refresh();
            _changed();
        };
        parent.Items.Add(add);
    }

    private async Task<string?> PromptForPresetVariableAsync(ServiceItemViewModel vm)
    {
        var defaultValue = _variableUsageStore.First(vm.Config.Id, vm.Config.PresetVariables);
        var dialog = new PresetVariableInputDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await RememberPresetVariableAsync(vm.Config, variable, addIfMissing: true);
        return variable;
    }

    private async Task RememberPresetVariableAsync(ServiceConfig config, string? variable, bool addIfMissing)
    {
        if (string.IsNullOrWhiteSpace(variable))
            return;

        var normalized = variable.Trim();
        if (addIfMissing && !config.PresetVariables.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            config.PresetVariables.Add(normalized);
            await _mainViewModel.SaveConfigAsync();
        }

        _variableUsageStore.Remember(config.Id, normalized);
    }

    private void AddNewStepVariableMenuItem(ItemsControl parent, ServiceItemViewModel vm, ScriptStep step, Func<string, Task> runAsync)
    {
        parent.Items.Add(new Separator());

        var add = new MenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForStepVariableAsync(vm, step);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
            ServicesGrid.Items.Refresh();
            _changed();
        };
        parent.Items.Add(add);
    }

    private async Task<string?> PromptForStepVariableAsync(ServiceItemViewModel vm, ScriptStep step)
    {
        if (step.RunOnStart)
            return await PromptForPresetVariableAsync(vm);

        var defaultValue = _variableUsageStore.First(step.Id, step.StepVariables);
        var dialog = new PresetVariableInputDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await RememberVariableForStepAsync(vm.Config, step, variable, addIfMissing: true);
        return variable;
    }

    private async Task RememberVariableForStepAsync(ServiceConfig config, ScriptStep step, string? variable, bool addIfMissing)
    {
        if (step.RunOnStart)
        {
            await RememberPresetVariableAsync(config, variable, addIfMissing);
            return;
        }

        if (string.IsNullOrWhiteSpace(variable))
            return;

        var normalized = variable.Trim();
        if (addIfMissing && !step.StepVariables.Any(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            step.StepVariables.Add(normalized);
            await _mainViewModel.SaveConfigAsync();
        }

        _variableUsageStore.Remember(step.Id, normalized);
    }

    private static void OpenMenu(object sender, ContextMenu menu)
    {
        if (sender is FrameworkElement element)
        {
            menu.PlacementTarget = element;
            element.ContextMenu = menu;
        }

        menu.IsOpen = true;
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

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;

        RememberServiceUse(vm);
        var template = ShowSaveTemplateDialog(vm.Config, this);
        if (template == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("TemplateNameExists", template.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _appConfig.ServiceTemplates.Add(template);
        await _configService.SaveAsync(_appConfig);
        _changed();
    }

    private async Task SaveServiceDraftAsTemplateAsync(ServiceConfig draft, Window? owner)
    {
        var template = ShowSaveTemplateDialog(draft, owner ?? this);
        if (template == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("TemplateNameExists", template.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _appConfig.ServiceTemplates.Add(template);
        await _configService.SaveAsync(_appConfig);
        _changed();
    }

    public static ServiceTemplate? ShowSaveTemplateDialog(ServiceConfig service, Window? owner = null)
    {
        if (service.ScriptSteps.Count == 0)
        {
            MessageBox.Show(LocalizationService.Current.T("NoTemplateSteps"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var templateDialog = new ServiceTemplateDialog(sourceService: service);
        if (owner != null)
            templateDialog.Owner = owner;

        return templateDialog.ShowDialog() == true ? templateDialog.Result : null;
    }
}
