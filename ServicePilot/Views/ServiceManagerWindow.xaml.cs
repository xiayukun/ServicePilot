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
        RunActionButton.Content = LocalizationService.Current.T("RunAction");
        StopButton.Content = LocalizationService.Current.T("Stop");
        LogsButton.Content = LocalizationService.Current.T("ViewLogs");
        SaveTemplateButton.Content = LocalizationService.Current.T("SaveAsTemplate");
        StatusColumn.Header = LocalizationService.Current.T("Status");
        NameColumn.Header = LocalizationService.Current.T("Name");
        WorkingDirectoryColumn.Header = LocalizationService.Current.T("WorkingDirectory");
        StepsColumn.Header = LocalizationService.Current.T("Actions");
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

    public void RefreshAfterConfigChanged(Guid? selectedServiceId = null)
    {
        RefreshServicesGrid(selectedServiceId);
        ServicesGrid.Items.Refresh();
        UpdateActionButtons();
    }

    private void RememberServiceUse(ServiceItemViewModel vm)
    {
        _variableUsageStore.RememberService(vm.Config.Id);
        RefreshServicesGrid(vm.Config.Id);
        _changed();
    }

    private void ServicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionButtons();

    private void UpdateActionButtons()
    {
        var vm = SelectedService;
        if (vm == null)
        {
            StopButton.IsEnabled = false;
            RunActionButton.IsEnabled = false;
            return;
        }

        var state = vm.RuntimeState.State;
        var hasRunningStep = vm.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running);
        StopButton.IsEnabled = state is ProcessState.Running or ProcessState.Starting or ProcessState.Stopping || hasRunningStep;
        RunActionButton.IsEnabled = vm.Config.ScriptSteps.Any(step =>
            step.Kind == StepKind.Composite || !string.IsNullOrWhiteSpace(step.Content));
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

    private void RunAction_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        RememberServiceUse(vm);

        var menu = new ContextMenu();
        AddActionItems(menu, vm);
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = LocalizationService.Current.T("NoActions"), IsEnabled = false });
        OpenMenu(sender, menu);
    }

    private void AddActionItems(ItemsControl menu, ServiceItemViewModel vm)
    {
        foreach (var step in vm.Config.ScriptSteps.OrderBy(s => s.Order))
        {
            if (step.Kind == StepKind.Composite)
                menu.Items.Add(CreateCompositeMenuItem(vm, step));
            else if (!string.IsNullOrWhiteSpace(step.Content))
                menu.Items.Add(CreateActionMenuItem(vm, step));
        }
    }

    private MenuItem CreateCompositeMenuItem(ServiceItemViewModel vm, ScriptStep composite)
    {
        var running = vm.RuntimeState.State is ProcessState.Running or ProcessState.Starting;
        var variableMember = ScriptDefinitionService.FindVariableMember(vm.Config, composite);
        if (variableMember == null)
        {
            var item = new MenuItem { Header = CreatePlainHeader(composite.Name), IsEnabled = !running };
            item.Click += (_, _) =>
            {
                RememberServiceUse(vm);
                _processManager.RunComposite(vm.Config.Id, composite.Id);
                UpdateActionButtons();
                _changed();
            };
            return item;
        }

        var menu = new MenuItem { Header = CreatePlainHeader(composite.Name), IsEnabled = !running };
        AddVariableChoices(menu, vm, variableMember, variable =>
        {
            RememberServiceUse(vm);
            _processManager.RunComposite(vm.Config.Id, composite.Id, variable);
            UpdateActionButtons();
            _changed();
            return Task.CompletedTask;
        });
        return menu;
    }

    private MenuItem CreateActionMenuItem(ServiceItemViewModel vm, ScriptStep step)
    {
        var state = GetStepState(vm.RuntimeState, step.Id);
        var stepState = state?.State ?? StepRunState.NotRun;
        var isRunning = stepState == StepRunState.Running;
        if (!step.UseVariable)
        {
            var item = new MenuItem
            {
                Header = CreateStatusHeader(step.Name, GetStepStatusBrush(stepState)),
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

        var menu = new MenuItem
        {
            Header = CreateStatusHeader(step.Name, GetStepStatusBrush(stepState)),
            ToolTip = FormatStepStateText(stepState),
            IsEnabled = !isRunning
        };
        AddVariableChoices(menu, vm, step, variable =>
        {
            RememberServiceUse(vm);
            _processManager.RunStep(vm.Config.Id, step.Id, variable);
            UpdateActionButtons();
            _changed();
            return Task.CompletedTask;
        });
        return menu;
    }

    private void AddVariableChoices(ItemsControl parent, ServiceItemViewModel vm, ScriptStep step, Func<string?, Task> runAsync)
    {
        foreach (var variable in GetSortedVariablesForStep(step))
        {
            var variableItem = new MenuItem { Header = CreatePlainHeader(variable) };
            variableItem.Click += async (_, _) =>
            {
                RememberServiceUse(vm);
                await RememberVariableForStepAsync(vm.Config, step, variable, addIfMissing: false);
                await runAsync(variable);
                ServicesGrid.Items.Refresh();
            };
            parent.Items.Add(variableItem);
        }

        parent.Items.Add(new Separator());
        var add = new MenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForStepVariableAsync(step);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
            ServicesGrid.Items.Refresh();
        };
        parent.Items.Add(add);
    }

    private IReadOnlyList<string> GetSortedVariablesForStep(ScriptStep step) =>
        _variableUsageStore.Sort(step.Id, step.StepVariables);

    private async Task<string?> PromptForStepVariableAsync(ScriptStep step)
    {
        var defaultValue = _variableUsageStore.First(step.Id, step.StepVariables);
        var dialog = new PresetVariableInputDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await RememberVariableForStepAsync(SelectedService?.Config ?? throw new InvalidOperationException(), step, variable, addIfMissing: true);
        return variable;
    }

    private async Task RememberVariableForStepAsync(ServiceConfig config, ScriptStep step, string? variable, bool addIfMissing)
    {
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

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedService;
        if (vm == null) return;
        RememberServiceUse(vm);
        _viewLog(vm);
    }

    private static object CreateStatusHeader(string text, System.Windows.Media.Brush? dotBrush)
    {
        if (dotBrush == null)
            return CreatePlainHeader(text);

        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        panel.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = dotBrush,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(CreatePlainHeader(text));
        return panel;
    }

    private static System.Windows.Media.Brush? GetStepStatusBrush(StepRunState state) => state switch
    {
        StepRunState.Running => System.Windows.Media.Brushes.ForestGreen,
        StepRunState.Failed => System.Windows.Media.Brushes.Firebrick,
        StepRunState.Cancelled => System.Windows.Media.Brushes.DarkOrange,
        _ => null
    };

    private static TextBlock CreatePlainHeader(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center
    };

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
