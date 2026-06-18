using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels;

namespace ServicePilot.Views;

public partial class LogWindow : Window
{
    private const int MaxLogEntries = 5000;
    private const string ServiceLogsKey = "__service__";
    private static readonly Regex WebpackProgressRegex = new(
        @"^(?<prefix>.*?\s*)?\[webpack\.Progress\]\s+(?<percent>\d{1,3})%\s+(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ServiceItemViewModel _service;
    private readonly ProcessManager _processManager;
    private readonly PresetVariableUsageStore _variableUsageStore;
    private readonly Func<ServiceConfig, ScriptStep, string?, bool, Task> _rememberVariableForStepAsync;
    private readonly Func<ServiceItemViewModel, Window?, Task> _editServiceAsync;
    private readonly DispatcherTimer _scrollTimer;
    private readonly DispatcherTimer _renderTimer;
    private readonly ObservableCollection<LogTabState> _logTabs = new();
    private readonly Dictionary<string, LogTabState> _logTabsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingLogLock = new();
    private readonly List<LogEntry> _pendingCrossThreadLogs = new();
    private bool _pendingCrossThreadDispatch;
    private bool _activeTabDirty;
    private bool _searchStatusDirty;
    private bool _pendingAutoScroll;
    private bool _logUiReady;
    private int _lastSearchOffset = -1;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public LogWindow(
        ServiceItemViewModel service,
        ProcessManager processManager,
        PresetVariableUsageStore variableUsageStore,
        Func<ServiceConfig, ScriptStep, string?, bool, Task> rememberVariableForStepAsync,
        Func<ServiceItemViewModel, Window?, Task> editServiceAsync)
    {
        _service = service;
        _processManager = processManager;
        _variableUsageStore = variableUsageStore;
        _rememberVariableForStepAsync = rememberVariableForStepAsync;
        _editServiceAsync = editServiceAsync;

        InitializeComponent();
        DataContext = service;
        LogEditor.Document ??= new TextDocument();
        LogTabs.ItemsSource = _logTabs;
        LogEditor.TextArea.TextView.LineTransformers.Add(new LogLineColorizer(LogEntries));
        ApplyLocalization();
        UpdateTitle();
        UpdateActionButtons();
        _logUiReady = true;

        _scrollTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _scrollTimer.Tick += (_, _) =>
        {
            _scrollTimer.Stop();
            if (AutoScrollCheck.IsChecked == true && LogEntries.Count > 0)
                LogEditor.ScrollToLine(LogEntries.Count);
        };

        _renderTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _renderTimer.Tick += (_, _) => FlushLogRender();

        _processManager.ServiceStateChanged += OnServiceStateChanged;
        _processManager.StepStateChanged += OnStepStateChanged;
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _scrollTimer.Stop();
            _renderTimer.Stop();
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
        RunActionButton.Content = LocalizationService.Current.T("RunAction");
        StopButton.Content = LocalizationService.Current.T("Stop");
        SearchBox.ToolTip = LocalizationService.Current.T("SearchLogs");
        FindPreviousButton.Content = LocalizationService.Current.T("FindPrevious");
        FindNextButton.Content = LocalizationService.Current.T("FindNext");
        AutoScrollCheck.Content = LocalizationService.Current.T("AutoScroll");
        EditButton.Content = LocalizationService.Current.T("Edit");
        ClearButton.Content = LocalizationService.Current.T("Clear");
        CopySelectedMenuItem.Header = LocalizationService.Current.T("CopySelected");
        CopyAllMenuItem.Header = LocalizationService.Current.T("CopyAll");
        RefreshServiceTabHeader();
    }

    public void LoadLogs(IEnumerable<LogEntry> entries)
    {
        var snapshot = entries.TakeLast(MaxLogEntries).ToList();
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded)
                return;

            LogEntries.Clear();
            _logTabs.Clear();
            _logTabsByKey.Clear();
            foreach (var entry in snapshot)
                AddEntryToTab(entry);

            LogTabs.SelectedItem = _logTabs.LastOrDefault();
            RebuildActiveLogText();
            UpdateSearchStatus();

            if (LogEntries.Count > 0)
                ScheduleAutoScroll();
        }, DispatcherPriority.Background);
    }

    public void AddLog(LogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            var shouldSchedule = false;
            lock (_pendingLogLock)
            {
                _pendingCrossThreadLogs.Add(entry);
                if (!_pendingCrossThreadDispatch)
                {
                    _pendingCrossThreadDispatch = true;
                    shouldSchedule = true;
                }
            }

            if (shouldSchedule)
                Dispatcher.BeginInvoke(DrainPendingLogs, DispatcherPriority.Background);
            return;
        }

        AddLogOnUi(entry);
    }

    private void DrainPendingLogs()
    {
        List<LogEntry> batch;
        lock (_pendingLogLock)
        {
            batch = _pendingCrossThreadLogs.ToList();
            _pendingCrossThreadLogs.Clear();
            _pendingCrossThreadDispatch = false;
        }

        foreach (var entry in batch)
            AddLogOnUi(entry);

        lock (_pendingLogLock)
        {
            if (_pendingCrossThreadLogs.Count > 0 && !_pendingCrossThreadDispatch)
            {
                _pendingCrossThreadDispatch = true;
                Dispatcher.BeginInvoke(DrainPendingLogs, DispatcherPriority.Background);
            }
        }
    }

    private void AddLogOnUi(LogEntry entry)
    {
        var targetTab = AddEntryToTab(entry);
        var selectedTab = LogTabs.SelectedItem as LogTabState;
        if (selectedTab == null)
        {
            LogTabs.SelectedItem = targetTab;
            MarkActiveTabDirty(autoScroll: true);
        }
        else if (ReferenceEquals(selectedTab, targetTab))
        {
            MarkActiveTabDirty(autoScroll: true);
        }
    }

    private void RefreshServiceTabHeader()
    {
        if (_logTabsByKey.TryGetValue(ServiceLogsKey, out var service))
            service.Header = LocalizationService.Current.T("ServiceLogs");
        LogTabs.Items.Refresh();
    }

    private LogTabState AddEntryToTab(LogEntry entry)
    {
        var targetTab = string.IsNullOrWhiteSpace(entry.StepName)
            ? EnsureLogTab(ServiceLogsKey, LocalizationService.Current.T("ServiceLogs"))
            : EnsureLogTab(StepLogKey(entry.StepName), entry.StepName.Trim());
        AddEntryToTabEntries(targetTab, entry);
        return targetTab;
    }

    private LogTabState EnsureLogTab(string key, string header)
    {
        if (_logTabsByKey.TryGetValue(key, out var existing))
            return existing;

        var tab = new LogTabState(key, header);
        _logTabsByKey[key] = tab;
        _logTabs.Add(tab);
        return tab;
    }

    private static void AddEntryToTabEntries(LogTabState tab, LogEntry entry)
    {
        if (TryCreateProgressEntry(entry, out var progressEntry, out var progressKey))
        {
            if (tab.Entries.Count > 0 &&
                TryGetProgressKey(tab.Entries[^1], out var lastProgressKey) &&
                string.Equals(lastProgressKey, progressKey, StringComparison.Ordinal))
            {
                tab.Entries[^1] = progressEntry;
                return;
            }

            entry = progressEntry;
        }

        if (tab.Entries.Count >= MaxLogEntries)
            tab.Entries.RemoveAt(0);
        tab.Entries.Add(entry);
    }

    private void MarkActiveTabDirty(bool autoScroll)
    {
        _activeTabDirty = true;
        _pendingAutoScroll |= autoScroll;
        _searchStatusDirty |= HasSearchQuery();
        if (!_renderTimer.IsEnabled)
            _renderTimer.Start();
    }

    private void FlushLogRender()
    {
        _renderTimer.Stop();
        if (_activeTabDirty)
            RebuildActiveLogText();

        if (_searchStatusDirty)
            UpdateSearchStatus();

        if (_pendingAutoScroll)
            ScheduleAutoScroll();

        _activeTabDirty = false;
        _searchStatusDirty = false;
        _pendingAutoScroll = false;
    }

    private static string StepLogKey(string stepName) => "step:" + stepName.Trim();

    private void LogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != LogTabs)
            return;
        if (!_logUiReady || LogEditor?.Document == null)
            return;

        RebuildActiveLogText();
        UpdateSearchStatus();
        ScheduleAutoScroll();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        UpdateActionButtons();
        await _processManager.StopServiceAsync(_service.Config.Id);
        UpdateActionButtons();
    }

    private void RunAction_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddActionItems(menu);
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = LocalizationService.Current.T("NoActions"), IsEnabled = false });
        OpenMenu(sender, menu);
    }

    private void AddActionItems(ItemsControl menu)
    {
        foreach (var step in _service.Config.ScriptSteps.OrderBy(step => step.Order))
        {
            if (step.Kind == StepKind.Composite)
            {
                menu.Items.Add(CreateCompositeMenuItem(step));
            }
            else if (!string.IsNullOrWhiteSpace(step.Content))
            {
                menu.Items.Add(CreateActionMenuItem(step));
            }
        }
    }

    private MenuItem CreateCompositeMenuItem(ScriptStep composite)
    {
        var running = _service.RuntimeState.State is ProcessState.Running or ProcessState.Starting;
        var variableMember = ScriptDefinitionService.FindVariableMember(_service.Config, composite);
        if (variableMember == null)
        {
            var item = new MenuItem
            {
                Header = CreatePlainHeader(composite.Name),
                IsEnabled = !running
            };
            item.Click += (_, _) =>
            {
                _processManager.RunComposite(_service.Config.Id, composite.Id);
                UpdateActionButtons();
            };
            return item;
        }

        var menu = new MenuItem
        {
            Header = CreatePlainHeader(composite.Name),
            IsEnabled = !running
        };
        AddVariableChoices(menu, variableMember, variable =>
        {
            _processManager.RunComposite(_service.Config.Id, composite.Id, variable);
            UpdateActionButtons();
            return Task.CompletedTask;
        });
        return menu;
    }

    private MenuItem CreateActionMenuItem(ScriptStep action)
    {
        var state = GetStepState(_service.RuntimeState, action.Id);
        var stepState = state?.State ?? StepRunState.NotRun;
        var isRunning = stepState == StepRunState.Running;
        if (!action.UseVariable)
        {
            var item = new MenuItem
            {
                Header = CreateStatusHeader(action.Name, GetStepStatusBrush(stepState)),
                ToolTip = FormatStepStateText(stepState),
                IsEnabled = !isRunning
            };
            item.Click += (_, _) =>
            {
                _processManager.RunStep(_service.Config.Id, action.Id);
                UpdateActionButtons();
            };
            return item;
        }

        var menu = new MenuItem
        {
            Header = CreateStatusHeader(action.Name, GetStepStatusBrush(stepState)),
            ToolTip = FormatStepStateText(stepState),
            IsEnabled = !isRunning
        };
        AddVariableChoices(menu, action, variable =>
        {
            _processManager.RunStep(_service.Config.Id, action.Id, variable);
            UpdateActionButtons();
            return Task.CompletedTask;
        });
        return menu;
    }

    private void AddVariableChoices(ItemsControl parent, ScriptStep step, Func<string?, Task> runAsync)
    {
        foreach (var variable in GetSortedVariablesForStep(step))
        {
            var variableItem = new MenuItem { Header = CreatePlainHeader(variable) };
            variableItem.Click += async (_, _) =>
            {
                await _rememberVariableForStepAsync(_service.Config, step, variable, false);
                await runAsync(variable);
            };
            parent.Items.Add(variableItem);
        }

        parent.Items.Add(new Separator());
        var add = new MenuItem { Header = LocalizationService.Current.T("Add") };
        add.Click += async (_, _) =>
        {
            var variable = await PromptForStepVariableAsync(step);
            if (!string.IsNullOrWhiteSpace(variable))
                await runAsync(variable);
        };
        parent.Items.Add(add);
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

    private void OnServiceStateChanged(Guid serviceId, ProcessState state)
    {
        if (serviceId == _service.Config.Id)
            Dispatcher.Invoke(UpdateActionButtons);
    }

    private void OnStepStateChanged(Guid serviceId, StepRuntimeState state)
    {
        if (serviceId != _service.Config.Id)
            return;

        Dispatcher.Invoke(() =>
        {
            UpdateActionButtons();
            if (state.State == StepRunState.Running && !string.IsNullOrWhiteSpace(state.StepName))
                ActivateStepTab(state.StepName);
        });
    }

    private void ActivateStepTab(string stepName)
    {
        var tab = EnsureLogTab(StepLogKey(stepName), stepName.Trim());
        if (!ReferenceEquals(LogTabs.SelectedItem, tab))
            LogTabs.SelectedItem = tab;
    }

    private void UpdateActionButtons()
    {
        var state = _service.RuntimeState.State;
        var hasRunningStep = _service.RuntimeState.StepStates.Values.Any(step => step.State == StepRunState.Running);
        StopButton.IsEnabled = state is ProcessState.Running or ProcessState.Starting or ProcessState.Stopping || hasRunningStep;
        RunActionButton.IsEnabled = _service.Config.ScriptSteps.Any(step =>
            step.Kind == StepKind.Composite || !string.IsNullOrWhiteSpace(step.Content));
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
        foreach (var tab in _logTabs)
            tab.Entries.Clear();
        _logTabs.Clear();
        _logTabsByKey.Clear();
        LogEntries.Clear();
        LogEditor.Clear();
        LogTabs.SelectedItem = null;
        SearchStatusText.Text = string.Empty;
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        await _editServiceAsync(_service, this);
        UpdateTitle();
        RebuildActiveLogText();
    }

    private void UpdateTitle()
    {
        TitleText.Text = LocalizationService.Current.F("LogTitle", _service.Name);
        Title = LocalizationService.Current.F("LogTitle", _service.Name);
    }

    private void ScheduleAutoScroll()
    {
        if (AutoScrollCheck.IsChecked == true && !_scrollTimer.IsEnabled)
            _scrollTimer.Start();
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e) => CopySelectedLogs();

    private void CopyAll_Click(object sender, RoutedEventArgs e) => CopyAllLogs();

    private void LogEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopySelectedLogs();
            e.Handled = true;
        }
    }

    private void CopySelectedLogs()
    {
        var text = LogEditor.SelectedText;
        if (string.IsNullOrEmpty(text))
            text = LogEditor.Text;

        CopyToClipboard(text);
    }

    private void CopyAllLogs()
    {
        if (LogEditor.Text.Length > 0)
            CopyToClipboard(LogEditor.Text);
    }

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, true);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(80);
            }
        }
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
        _lastSearchOffset = -1;
        UpdateSearchStatus();
    }

    private bool HasSearchQuery() => !string.IsNullOrWhiteSpace(SearchBox.Text);

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindLogMatch(forward: true);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindLogMatch(forward: false);

    private void FindLogMatch(bool forward)
    {
        var query = SearchBox.Text?.Trim();
        var text = LogEditor.Text;
        if (string.IsNullOrWhiteSpace(query) || text.Length == 0)
        {
            UpdateSearchStatus();
            return;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        var start = _lastSearchOffset >= 0 ? _lastSearchOffset : LogEditor.CaretOffset;
        int index;
        if (forward)
        {
            index = text.IndexOf(query, Math.Min(start + 1, text.Length), comparison);
            if (index < 0)
                index = text.IndexOf(query, 0, comparison);
        }
        else
        {
            index = text.LastIndexOf(query, Math.Max(0, start - 1), comparison);
            if (index < 0)
                index = text.LastIndexOf(query, text.Length - 1, comparison);
        }

        if (index < 0)
        {
            SearchStatusText.Text = "0/0";
            return;
        }

        _lastSearchOffset = index;
        LogEditor.Select(index, query.Length);
        var line = LogEditor.Document.GetLineByOffset(index).LineNumber;
        LogEditor.ScrollToLine(line);
        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        var query = SearchBox.Text?.Trim();
        var text = LogEditor.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchStatusText.Text = string.Empty;
            return;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += query.Length;
        }

        if (count == 0)
        {
            SearchStatusText.Text = "0/0";
            return;
        }

        var current = _lastSearchOffset >= 0
            ? CountMatchesBefore(text, query, _lastSearchOffset) + 1
            : 0;
        SearchStatusText.Text = current > 0 ? $"{current}/{count}" : $"0/{count}";
    }

    private static int CountMatchesBefore(string text, string query, int offset)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0 && index < offset)
        {
            count++;
            index += query.Length;
        }
        return count;
    }

    private void RebuildActiveLogText()
    {
        if (!_logUiReady || LogEditor == null)
            return;

        EnsureLogDocument();
        LogEntries.Clear();
        if (LogTabs.SelectedItem is LogTabState tab)
        {
            foreach (var entry in tab.Entries)
                LogEntries.Add(entry);
        }

        var builder = new StringBuilder();
        foreach (var entry in LogEntries)
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(FormatLogLine(entry));
        }

        LogEditor.Text = builder.ToString();
        LogEditor.TextArea.TextView.Redraw();
    }

    private void EnsureLogDocument()
    {
        LogEditor.Document ??= new TextDocument();
    }

    private static string FormatLogLine(LogEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}";

    private static bool TryCreateProgressEntry(LogEntry entry, out LogEntry progressEntry, out string progressKey)
    {
        progressEntry = entry;
        progressKey = string.Empty;

        if (entry.Level == LogLevel.Error)
            return false;

        var match = WebpackProgressRegex.Match(entry.Message);
        if (!match.Success)
            return false;

        progressKey = "webpack-progress";
        var percent = Math.Clamp(int.Parse(match.Groups["percent"].Value), 0, 100);
        var prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value.TrimEnd() : string.Empty;
        var rest = match.Groups["rest"].Value.Trim();
        var bar = CreateProgressBar(percent);
        var messagePrefix = string.IsNullOrWhiteSpace(prefix)
            ? "[webpack.Progress]"
            : $"{prefix} [webpack.Progress]";

        progressEntry = new LogEntry(entry.Level, $"{messagePrefix} {percent}% {bar} {rest}", entry.Source, entry.StepName)
        {
            Timestamp = entry.Timestamp
        };
        return true;
    }

    private static bool TryGetProgressKey(LogEntry entry, out string progressKey)
    {
        progressKey = string.Empty;
        if (entry.Level == LogLevel.Error)
            return false;

        var match = WebpackProgressRegex.Match(entry.Message);
        if (!match.Success)
            return false;

        progressKey = "webpack-progress";
        return true;
    }

    private static string CreateProgressBar(int percent)
    {
        const int width = 24;
        var filled = Math.Clamp((int)Math.Round(percent / 100d * width), 0, width);
        return "[" + new string('#', filled) + new string('-', width - filled) + "]";
    }

    private sealed class LogLineColorizer : DocumentColorizingTransformer
    {
        private readonly IReadOnlyList<LogEntry> _entries;

        public LogLineColorizer(IReadOnlyList<LogEntry> entries)
        {
            _entries = entries;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var index = line.LineNumber - 1;
            if (index < 0 || index >= _entries.Count)
                return;

            var brush = _entries[index].Level switch
            {
                LogLevel.Error => System.Windows.Media.Brushes.OrangeRed,
                LogLevel.Warning => System.Windows.Media.Brushes.Gold,
                LogLevel.System => System.Windows.Media.Brushes.Gray,
                _ => System.Windows.Media.Brushes.White
            };

            ChangeLinePart(line.Offset, line.EndOffset, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
            });
        }
    }

    private sealed class LogTabState
    {
        public LogTabState(string key, string header)
        {
            Key = key;
            Header = header;
        }

        public string Key { get; }
        public string Header { get; set; }
        public List<LogEntry> Entries { get; } = new();
    }
}
