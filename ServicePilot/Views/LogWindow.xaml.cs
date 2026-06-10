using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels;

namespace ServicePilot.Views;

public partial class LogWindow : Window
{
    private const int MaxLogEntries = 20000;

    private readonly ServiceItemViewModel _service;
    private readonly ProcessManager _processManager;
    private readonly PresetVariableUsageStore _variableUsageStore;
    private readonly Func<ServiceConfig, string?, bool, Task> _rememberPresetVariableAsync;
    private readonly Func<ServiceConfig, ScriptStep, string?, bool, Task> _rememberVariableForStepAsync;
    private readonly Func<ServiceItemViewModel, Window?, Task> _editServiceAsync;
    private readonly DispatcherTimer _scrollTimer;
    private LogEntry? _pendingScrollEntry;
    private int _lastSearchIndex = -1;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public LogWindow(
        ServiceItemViewModel service,
        ProcessManager processManager,
        PresetVariableUsageStore variableUsageStore,
        Func<ServiceConfig, string?, bool, Task> rememberPresetVariableAsync,
        Func<ServiceConfig, ScriptStep, string?, bool, Task> rememberVariableForStepAsync,
        Func<ServiceItemViewModel, Window?, Task> editServiceAsync)
    {
        _service = service;
        _processManager = processManager;
        _variableUsageStore = variableUsageStore;
        _rememberPresetVariableAsync = rememberPresetVariableAsync;
        _rememberVariableForStepAsync = rememberVariableForStepAsync;
        _editServiceAsync = editServiceAsync;

        InitializeComponent();
        DataContext = service;
        ApplyLocalization();
        UpdateTitle();
        LogList.ItemsSource = LogEntries;
        UpdateActionButtons();

        _scrollTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _scrollTimer.Tick += (_, _) =>
        {
            _scrollTimer.Stop();
            if (_pendingScrollEntry == null || AutoScrollCheck.IsChecked != true)
                return;

            var entry = _pendingScrollEntry;
            _pendingScrollEntry = null;
            LogList.ScrollIntoView(entry);
        };

        _processManager.ServiceStateChanged += OnServiceStateChanged;
        _processManager.StepStateChanged += OnStepStateChanged;
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _scrollTimer.Stop();
            _processManager.ServiceStateChanged -= OnServiceStateChanged;
            _processManager.StepStateChanged -= OnStepStateChanged;
            LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            UpdateTitle();
            UpdateActionButtons();
        });
    }

    private void ApplyLocalization()
    {
        StartButton.Content = LocalizationService.Current.T("Start");
        RunStepButton.Content = LocalizationService.Current.T("RunStep");
        StopButton.Content = LocalizationService.Current.T("Stop");
        RestartButton.Content = LocalizationService.Current.T("Restart");
        SearchBox.ToolTip = LocalizationService.Current.T("SearchLogs");
        FindPreviousButton.Content = LocalizationService.Current.T("FindPrevious");
        FindNextButton.Content = LocalizationService.Current.T("FindNext");
        AutoScrollCheck.Content = LocalizationService.Current.T("AutoScroll");
        EditButton.Content = LocalizationService.Current.T("Edit");
        ClearButton.Content = LocalizationService.Current.T("Clear");
        CopySelectedMenuItem.Header = LocalizationService.Current.T("CopySelected");
        CopyAllMenuItem.Header = LocalizationService.Current.T("CopyAll");
    }

    public void LoadLogs(IEnumerable<LogEntry> entries)
    {
        var snapshot = entries.TakeLast(MaxLogEntries).ToList();
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded)
                return;

            LogList.ItemsSource = null;
            LogEntries.Clear();
            foreach (var entry in snapshot)
                LogEntries.Add(entry);
            LogList.ItemsSource = LogEntries;
            UpdateSearchStatus();

            if (LogEntries.Count > 0)
                ScheduleAutoScroll(LogEntries[LogEntries.Count - 1]);
        }, DispatcherPriority.Background);
    }

    public void AddLog(LogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddLog(entry), DispatcherPriority.Background);
            return;
        }

        if (LogEntries.Count >= MaxLogEntries)
            LogEntries.RemoveAt(0);

        LogEntries.Add(entry);
        UpdateSearchStatus();
        ScheduleAutoScroll(entry);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (ShowVariableMenu(sender, variable =>
            {
                _processManager.StartService(_service.Config.Id, new ServiceStartOptions { Variable = variable });
                UpdateActionButtons();
                return Task.CompletedTask;
            }))
        {
            return;
        }

        _processManager.StartService(_service.Config.Id);
        UpdateActionButtons();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        UpdateActionButtons();
        await _processManager.StopServiceAsync(_service.Config.Id);
        UpdateActionButtons();
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (ShowVariableMenu(sender, variable =>
            RestartWithVariableAsync(variable)))
        {
            return;
        }

        await _processManager.RestartServiceAsync(_service.Config.Id);
        UpdateActionButtons();
    }

    private async Task RestartWithVariableAsync(string? variable)
    {
        await _processManager.RestartServiceAsync(_service.Config.Id, new ServiceStartOptions { Variable = variable });
        UpdateActionButtons();
    }

    private void RunStep_Click(object sender, RoutedEventArgs e)
    {
        var steps = _service.Config.ScriptSteps
            .Where(step => !string.IsNullOrWhiteSpace(step.Content))
            .OrderBy(step => step.Order)
            .ToList();
        if (steps.Count == 0)
            return;

        var menu = new ContextMenu();
        AddStepGroup(menu, steps.Where(step => step.RunOnStart), LocalizationService.Current.T("StartupSteps"));
        AddStepGroup(menu, steps.Where(step => !step.RunOnStart), LocalizationService.Current.T("ManualSteps"));
        OpenMenu(sender, menu);
    }

    private void AddStepGroup(ItemsControl menu, IEnumerable<ScriptStep> sourceSteps, string header)
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
            menu.Items.Add(CreateRunStepMenuItem(step, displayHeader));
        }
    }

    private MenuItem CreateRunStepMenuItem(ScriptStep step, string header)
    {
        var state = GetStepState(_service.RuntimeState, step.Id);
        var stepState = state?.State ?? StepRunState.NotRun;
        var isRunning = stepState == StepRunState.Running;
        var variables = GetSortedVariablesForStep(step);
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
                _processManager.RunStep(_service.Config.Id, step.Id);
                UpdateActionButtons();
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
                await _rememberVariableForStepAsync(_service.Config, step, variable, false);
                _processManager.RunStep(_service.Config.Id, step.Id, variable);
                UpdateActionButtons();
            };
            stepMenu.Items.Add(variableItem);
        }

        AddNewStepVariableMenuItem(stepMenu, step, variable =>
        {
            _processManager.RunStep(_service.Config.Id, step.Id, variable);
            UpdateActionButtons();
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

    private bool ShowVariableMenu(object sender, Func<string?, Task> runAsync)
    {
        var variables = GetSortedPresetVariables();
        if (variables.Count == 0)
            return false;

        var menu = new ContextMenu();
        foreach (var variable in variables)
        {
            var item = new MenuItem { Header = variable };
            item.Click += async (_, _) =>
            {
                await _rememberPresetVariableAsync(_service.Config, variable, false);
                await runAsync(variable);
            };
            menu.Items.Add(item);
        }

        AddNewVariableMenuItem(menu, runAsync);
        OpenMenu(sender, menu);
        return true;
    }

    private void OnServiceStateChanged(Guid serviceId, ProcessState state)
    {
        if (serviceId != _service.Config.Id)
            return;

        Dispatcher.Invoke(UpdateActionButtons);
    }

    private void OnStepStateChanged(Guid serviceId, StepRuntimeState state)
    {
        if (serviceId != _service.Config.Id)
            return;

        Dispatcher.Invoke(UpdateActionButtons);
    }

    private void UpdateActionButtons()
    {
        var state = _service.RuntimeState.State;
        var hasRunningStep = _service.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running);
        StartButton.IsEnabled = !hasRunningStep &&
                                state is ProcessState.Stopped or ProcessState.Error or ProcessState.StartFailed or ProcessState.Completed;
        StopButton.IsEnabled = state is ProcessState.Running or ProcessState.Starting or ProcessState.Stopping || hasRunningStep;
        RestartButton.IsEnabled = state is not ProcessState.Starting and not ProcessState.Stopping;
        RunStepButton.IsEnabled = _service.Config.ScriptSteps.Any(step => !string.IsNullOrWhiteSpace(step.Content));
    }

    private void AddNewVariableMenuItem(ItemsControl parent, Func<string?, Task> runAsync)
    {
        parent.Items.Add(new Separator());

        var add = new MenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForPresetVariableAsync();
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
        };
        parent.Items.Add(add);
    }

    private async Task<string?> PromptForPresetVariableAsync()
    {
        var defaultValue = _variableUsageStore.First(_service.Config.Id, _service.Config.PresetVariables);
        var dialog = new PresetVariableInputDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await _rememberPresetVariableAsync(_service.Config, variable, true);
        return variable;
    }

    private IReadOnlyList<string> GetSortedPresetVariables() =>
        _variableUsageStore.Sort(_service.Config.Id, _service.Config.PresetVariables);

    private IReadOnlyList<string> GetSortedVariablesForStep(ScriptStep step) =>
        step.RunOnStart
            ? _variableUsageStore.Sort(_service.Config.Id, _service.Config.PresetVariables)
            : _variableUsageStore.Sort(step.Id, step.StepVariables);

    private void AddNewStepVariableMenuItem(ItemsControl parent, ScriptStep step, Func<string?, Task> runAsync)
    {
        parent.Items.Add(new Separator());

        var add = new MenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForStepVariableAsync(step);
            if (string.IsNullOrWhiteSpace(variable))
                return;

            await runAsync(variable);
        };
        parent.Items.Add(add);
    }

    private async Task<string?> PromptForStepVariableAsync(ScriptStep step)
    {
        if (step.RunOnStart)
            return await PromptForPresetVariableAsync();

        var defaultValue = _variableUsageStore.First(step.Id, step.StepVariables);
        var dialog = new PresetVariableInputDialog(defaultValue) { Owner = this };
        if (dialog.ShowDialog() != true)
            return null;

        var variable = dialog.Variable;
        await _rememberVariableForStepAsync(_service.Config, step, variable, true);
        return variable;
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

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LogEntries.Clear();
        SearchStatusText.Text = string.Empty;
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        await _editServiceAsync(_service, this);
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        TitleText.Text = LocalizationService.Current.F("LogTitle", _service.Name);
        Title = LocalizationService.Current.F("LogTitle", _service.Name);
    }

    private void ScheduleAutoScroll(LogEntry entry)
    {
        if (AutoScrollCheck.IsChecked != true)
            return;

        _pendingScrollEntry = entry;
        if (!_scrollTimer.IsEnabled)
            _scrollTimer.Start();
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e) => CopySelectedLogs();

    private void CopyAll_Click(object sender, RoutedEventArgs e) => CopyAllLogs();

    private void LogList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopySelectedLogs();
            e.Handled = true;
        }
    }

    private void CopySelectedLogs()
    {
        var selected = LogList.SelectedItems.Cast<LogEntry>().ToList();
        if (selected.Count == 0)
            selected = LogEntries.ToList();

        if (selected.Count == 0)
            return;

        System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(FormatLogLine)));
    }

    private void CopyAllLogs()
    {
        if (LogEntries.Count == 0)
            return;

        System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(FormatLogLine)));
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _lastSearchIndex = -1;
        UpdateSearchStatus();
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindLogMatch(forward: true);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindLogMatch(forward: false);

    private void FindLogMatch(bool forward)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query) || LogEntries.Count == 0)
        {
            UpdateSearchStatus();
            return;
        }

        var start = _lastSearchIndex >= 0 ? _lastSearchIndex : LogList.SelectedIndex;
        for (var offset = 1; offset <= LogEntries.Count; offset++)
        {
            var index = forward
                ? (start + offset + LogEntries.Count) % LogEntries.Count
                : (start - offset + LogEntries.Count) % LogEntries.Count;
            if (!Matches(LogEntries[index], query))
                continue;

            _lastSearchIndex = index;
            LogList.SelectedIndex = index;
            LogList.ScrollIntoView(LogEntries[index]);
            UpdateSearchStatus();
            return;
        }

        SearchStatusText.Text = "0/0";
    }

    private void UpdateSearchStatus()
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchStatusText.Text = string.Empty;
            return;
        }

        var matches = LogEntries
            .Select((entry, index) => (entry, index))
            .Where(item => Matches(item.entry, query))
            .Select(item => item.index)
            .ToList();
        if (matches.Count == 0)
        {
            SearchStatusText.Text = "0/0";
            return;
        }

        var current = _lastSearchIndex >= 0 ? matches.IndexOf(_lastSearchIndex) + 1 : 0;
        SearchStatusText.Text = current > 0 ? $"{current}/{matches.Count}" : $"0/{matches.Count}";
    }

    private static bool Matches(LogEntry entry, string query) =>
        entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        entry.Level.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string FormatLogLine(LogEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}";
}
