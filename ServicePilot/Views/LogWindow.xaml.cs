using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ServicePilot.Models;
using ServicePilot.Services;
using ServicePilot.ViewModels;

namespace ServicePilot.Views;

public partial class LogWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int MaxLogEntries = 5000;
    private const string ServiceLogsKey = "__service__";

    private readonly ServiceItemViewModel _service;
    private readonly ProcessManager _processManager;
    private readonly PresetVariableUsageStore _variableUsageStore;
    private readonly Func<ServiceConfig, ScriptStep, string?, bool, Task> _rememberVariableForStepAsync;
    private readonly Func<ServiceItemViewModel, Window?, Task> _editServiceAsync;
    private readonly LogMergeService _logMergeService;
    private readonly DispatcherTimer _scrollTimer;
    private readonly DispatcherTimer _renderTimer;
    private readonly ObservableCollection<LogTabState> _logTabs = new();
    private readonly Dictionary<string, LogTabState> _logTabsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingLogLock = new();
    private readonly List<LogEntry> _pendingCrossThreadLogs = new();
    private readonly List<PendingRenderOp> _pendingInserts = new();
    private readonly HashSet<Guid> _reportedMergeErrors = new();
    // (merge colors live on each LogEntry.MergeColor; no separate cache needed)
    private FoldingManager? _foldingManager;
    private FoldColorMarkerRenderer? _foldColorMarker;
    private string? _foldTitlePrefix;
    private OverviewMargin? _overviewMargin;
    private bool _pendingCrossThreadDispatch;
    private bool _activeTabDirty;
    private bool _searchStatusDirty;
    private bool _pendingAutoScroll;
    private bool _logUiReady;
    private int _lastSearchOffset = -1;
    private bool _summaryViewActive;
    // Header entries whose folding has already been given its default (folded) state once.
    private readonly HashSet<LogEntry> _foldingInitialized = new();

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
        _logMergeService = new LogMergeService();

        InitializeComponent();
        DataContext = service;
        LogEditor.Document ??= new TextDocument();
        LogTabs.ItemsSource = _logTabs;

        // Folding: use FoldingManager.Install so the manager is actually wired into the TextView's line
        // generation (that is what hides folded lines). It also installs the left-side fold margin with
        // the +/- expander toggles. Creating a FoldingManager directly only shows the markers but never
        // hides folded content.
        _foldingManager = FoldingManager.Install(LogEditor.TextArea);
        // Per-fold color chip: AvalonEdit's fold box is a single global color, so this overlay draws a
        // small content-colored square at each collapsed fold's header line, letting multiple folds show
        // different colors (blue fold above, red fold below) at the same time.
        _foldColorMarker = new FoldColorMarkerRenderer(_foldingManager);
        LogEditor.TextArea.TextView.BackgroundRenderers.Add(_foldColorMarker);
        // Collapsed fold placeholder text is fixed white (per-fold content color is shown by the color
        // block drawn to its left). TextBrush is a global static, so setting it once here is enough.
        FoldingElementGenerator.TextBrush = System.Windows.Media.Brushes.White;
        // Right-side color-coded overview map (Error/Warning/System/merge colors), click to jump. It is
        // folding-aware: folded-away lines don't consume rows, so errors below big folds stay visible.
        _overviewMargin = new OverviewMargin(LogEntries, LogEditor, _foldingManager);
        OverviewHost.Child = _overviewMargin;
        // VisualLinesChanged fires when foldings expand/collapse (or content changes), so the overview
        // rebuilds to reflect which lines are currently visible. This is throttled by AvalonEdit itself.
        LogEditor.TextArea.TextView.VisualLinesChanged += (_, _) => _overviewMargin?.InvalidateVisualCache();
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
            _logMergeService.Dispose();
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
        UpdateSummaryButton();
        CopySelectedMenuItem.Header = LocalizationService.Current.T("CopySelected");
        CopyAllMenuItem.Header = LocalizationService.Current.T("CopyAll");
        RefreshServiceTabHeader();
    }

    public void RefreshAfterConfigChanged()
    {
        UpdateTitle();
        UpdateActionButtons();
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
            _pendingInserts.Clear();
            foreach (var entry in snapshot)
                CommitEntryToTab(entry);

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

    /// <summary>
    /// Resolves the target tab, evaluates the merge script, and commits the entry into the tab's
    /// entry list (append or in-place collapse). Returns the tab and the merge outcome.
    /// Used both by live logging and by initial snapshot rebuild so history folds identically.
    /// </summary>
    private LogTabState CommitEntryToTab(LogEntry entry)
    {
        var targetTab = ResolveTab(entry);
        ApplyMerge(targetTab, entry);

        // Always append: raw lines are preserved so a collapse group can be expanded later. Folding
        // (the left-side > toggle) hides the collapsed children by default; nothing is discarded. The
        // per-entry MergeColor is carried on the entry itself, so it survives document rebuilds.
        AddEntryToTabEntries(targetTab, entry);
        return targetTab;
    }

    private void AddLogOnUi(LogEntry entry)
    {
        // Evaluate the merge script for every tab (not only the visible one) so group state and in-memory
        // history stay correct regardless of which tab the user is looking at.
        var targetTab = CommitEntryToTab(entry);

        var selectedTab = LogTabs.SelectedItem as LogTabState;
        if (selectedTab == null)
        {
            LogTabs.SelectedItem = targetTab;
            selectedTab = targetTab;
        }

        if (!ReferenceEquals(selectedTab, targetTab))
        {
            // Entry went to a non-visible tab — history is already committed; no document work needed.
            return;
        }

        _pendingInserts.Add(new PendingRenderOp(entry));
        MarkActiveTabDirty(autoScroll: true);
    }

    /// <summary>A single queued document mutation: append the entry as a new line.</summary>
    private readonly record struct PendingRenderOp(LogEntry Entry);

    /// <summary>
    /// Evaluates the step's LogMergeScript against the entry and updates the tab's collapse-group state.
    /// Never mutates <paramref name="entry"/>.Message: raw text is kept so groups can be expanded. When
    /// the script returns Collapse=true the entry becomes a folded child and the group header's
    /// <see cref="LogEntry.GroupSummary"/> is refreshed with the latest MergedMessage.
    /// Contract: CurrentLine / PreviousLine are the FULL formatted lines ("HH:mm:ss [Level] message").
    /// Collapse=true folds the current line into the group started by the previous non-collapsed line;
    /// the first line of a group must return Collapse=false.
    /// </summary>
    private void ApplyMerge(LogTabState tab, LogEntry entry)
    {
        var currentLine = FormatLogLine(entry);

        var step = FindStepForTab(tab);
        if (step == null || string.IsNullOrWhiteSpace(step.LogMergeScript))
        {
            tab.GroupHeader = null;
            tab.LastLine = currentLine;
            tab.LastResult = null;
            return;
        }

        var globals = new MergeScriptGlobals
        {
            PreviousLine = tab.LastLine,
            CurrentLine = currentLine,
            PreviousResult = tab.LastResult,
            PreviousWasCollapsed = tab.LastResult?.Collapse == true && tab.GroupHeader != null,
            InCollapseGroup = tab.GroupHeader != null
        };

        MergeResult? result = null;
        try
        {
            // Synchronous evaluation on the UI hot path; must not block on an async method that captures
            // the UI SynchronizationContext (that deadlocks when a burst of lines arrives).
            result = _logMergeService.Evaluate(step.LogMergeScript, globals);
        }
        catch
        {
            // Script evaluation must never crash the UI.
        }

        if (result == null)
        {
            ReportMergeCompileErrorOnce(step);
            tab.GroupHeader = null;
            tab.LastLine = currentLine;
            tab.LastResult = null;
            return;
        }

        entry.MergeColor = string.IsNullOrWhiteSpace(result.Color) ? null : result.Color;

        // Collapse only when the script asks for it AND there is a group header to fold into.
        var collapse = result.Collapse && tab.GroupHeader != null;
        if (collapse)
        {
            entry.IsCollapsedChild = true;
            // Refresh the header's summary with the latest merged message so the folded (one-line) view
            // shows live progress (e.g. "编译中 67%").
            if (!string.IsNullOrWhiteSpace(result.MergedMessage))
                tab.GroupHeader!.GroupSummary = result.MergedMessage;
        }
        else
        {
            // This line starts a new (potential) group and becomes its header.
            entry.IsCollapsedChild = false;
            entry.GroupSummary = string.IsNullOrWhiteSpace(result.MergedMessage) ? null : result.MergedMessage;
            tab.GroupHeader = entry;
        }

        tab.LastLine = currentLine;
        tab.LastResult = result;
    }

    private void ReportMergeCompileErrorOnce(ScriptStep step)
    {
        if (string.IsNullOrWhiteSpace(step.LogMergeScript))
            return;

        var error = _logMergeService.GetCompileError(step.LogMergeScript);
        if (string.IsNullOrWhiteSpace(error))
            return;

        if (!_reportedMergeErrors.Add(step.Id))
            return;

        var entry = new LogEntry(
            LogLevel.Error,
            LocalizationService.Current.F("MergeScriptCompileError", step.Name, error),
            "system",
            step.Name);
        Dispatcher.BeginInvoke(() => AddLog(entry), DispatcherPriority.Background);
    }

    private ScriptStep? FindStepForTab(LogTabState tab)
    {
        if (tab.Key == ServiceLogsKey)
            return null;

        if (!tab.Key.StartsWith("step:"))
            return null;

        var stepName = tab.Key["step:".Length..];
        return _service.Config.ScriptSteps
            .FirstOrDefault(s => s.Name.Trim() == stepName);
    }

    private void RefreshServiceTabHeader()
    {
        if (_logTabsByKey.TryGetValue(ServiceLogsKey, out var service))
            service.Header = LocalizationService.Current.T("ServiceLogs");
        LogTabs.Items.Refresh();
    }

    private LogTabState ResolveTab(LogEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.StepName)
            ? EnsureLogTab(ServiceLogsKey, LocalizationService.Current.T("ServiceLogs"))
            : EnsureLogTab(StepLogKey(entry.StepName), entry.StepName.Trim());
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

    /// <summary>
    /// Incremental flush: inserts only newly added entries into the document.
    /// Does NOT rebuild the entire document (except on tab switch).
    /// </summary>
    private void FlushLogRender()
    {
        _renderTimer.Stop();

        if (_activeTabDirty && LogTabs.SelectedItem is LogTabState tab)
        {
            EnsureLogDocument();
            var doc = LogEditor.Document;

            if (_pendingInserts.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var op in _pendingInserts)
                {
                    LogEntries.Add(op.Entry);
                    if (doc.TextLength > 0 || sb.Length > 0)
                        sb.Append(Environment.NewLine);
                    sb.Append(FormatLogLine(op.Entry));
                }
                doc.Insert(doc.TextLength, sb.ToString());
                _pendingInserts.Clear();

                // Group headers keep their raw first-line text (stable offsets); the live summary is
                // shown as the fold's collapsed placeholder title, refreshed inside RebuildFoldings.
                RebuildFoldings();
                LogEditor.TextArea.TextView.Redraw();
                _overviewMargin?.InvalidateVisualCache();
            }
        }

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

        _pendingInserts.Clear();
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
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 2;
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
        _pendingInserts.Clear();
        _foldingInitialized.Clear();
        _foldingManager?.Clear();
        LogEditor.Clear();
        LogTabs.SelectedItem = null;
        SearchStatusText.Text = string.Empty;
        _overviewMargin?.InvalidateVisualCache();
    }

    /// <summary>
    /// Summary view toggle: folds all merge groups to a single summary line (summary view), or expands
    /// them all to show every raw line. Toggles between the two on each click.
    /// </summary>
    private void Summary_Click(object sender, RoutedEventArgs e)
    {
        if (_foldingManager == null)
            return;

        var foldings = _foldingManager.AllFoldings.ToList();
        if (foldings.Count == 0)
            return;

        // If any group is currently expanded, collapse everything (enter summary view); otherwise expand.
        var shouldFold = foldings.Any(f => !f.IsFolded);
        foreach (var folding in foldings)
            folding.IsFolded = shouldFold;

        _summaryViewActive = shouldFold;
        UpdateSummaryButton();
        LogEditor.TextArea.TextView.Redraw();
    }

    private void UpdateSummaryButton()
    {
        SummaryButton.Content = _summaryViewActive
            ? LocalizationService.Current.T("LogExpandAll")
            : LocalizationService.Current.T("LogFoldAll");
        SummaryButton.ToolTip = _summaryViewActive
            ? LocalizationService.Current.T("LogExpandAllTip")
            : LocalizationService.Current.T("LogFoldAllTip");
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        await _editServiceAsync(_service, this);
        UpdateTitle();
        RebuildActiveLogText();
    }

    private void UpdateTitle()
    {
        // Title flows to the header TitleBar via binding; there is no separate toolbar title anymore.
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
        // If the match lands inside a folded (collapsed) group, expand that group so the user can see it.
        ExpandFoldingAt(index);
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

    /// <summary>Expands (unfolds) any collapse group whose folded region contains the given offset.</summary>
    private void ExpandFoldingAt(int offset)
    {
        if (_foldingManager == null)
            return;
        foreach (var folding in _foldingManager.GetFoldingsContaining(offset))
            folding.IsFolded = false;
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

    /// <summary>
    /// Full document rebuild — called only on tab switch or explicit reload.
    /// </summary>
    private void RebuildActiveLogText()
    {
        if (!_logUiReady || LogEditor == null)
            return;

        EnsureLogDocument();
        LogEntries.Clear();
        // Full rebuild recreates the document and all folding sections from scratch, so reset the
        // "already folded once" tracking to re-apply each group's default folded state.
        _foldingInitialized.Clear();
        // Colors and group metadata live on each LogEntry, so a rebuild (tab switch) just re-renders the
        // stored entries and re-derives foldings — no re-running of the merge script is needed.
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
        RebuildFoldings();
        LogEditor.TextArea.TextView.Redraw();
        _overviewMargin?.InvalidateVisualCache();
    }

    private void EnsureLogDocument()
    {
        LogEditor.Document ??= new TextDocument();
    }

    private static string FormatLogLine(LogEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}";

    /// <summary>
    /// Returns a run of spaces wide enough to clear the reserved color-block gap
    /// (<see cref="FoldColorMarkerRenderer.BlockWidth"/>), so collapsed-fold summary text starts to the
    /// right of the block instead of under it. Measured once against the editor's (monospace) font.
    /// </summary>
    private string GetFoldTitlePrefix()
    {
        if (_foldTitlePrefix != null)
            return _foldTitlePrefix;

        var typeface = new Typeface(LogEditor.FontFamily, LogEditor.FontStyle, LogEditor.FontWeight, LogEditor.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var space = new FormattedText(" ", System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight, typeface, LogEditor.FontSize, System.Windows.Media.Brushes.White, dpi);
        var spaceWidth = space.WidthIncludingTrailingWhitespace;
        if (spaceWidth <= 0)
            spaceWidth = LogEditor.FontSize * 0.55; // Fallback estimate for monospace fonts.

        // Add a small extra margin (BlockLeft ~4px + one space of breathing room) so text never touches
        // the block edge.
        var count = (int)Math.Ceiling((FoldColorMarkerRenderer.BlockWidth + 8) / spaceWidth) + 1;
        _foldTitlePrefix = new string(' ', Math.Max(1, count));
        return _foldTitlePrefix;
    }

    /// <summary>
    /// Builds AvalonEdit foldings from the collapse groups in <see cref="LogEntries"/>. A group is a
    /// header line (IsCollapsedChild=false with GroupSummary set) followed by one or more collapsed
    /// child lines. Each group folds from the end of the header line through the last child; folded by
    /// default so a busy group shows a single summary line with a left-side > toggle to expand it.
    /// </summary>
    private void RebuildFoldings()
    {
        if (_foldingManager == null)
            return;

        var doc = LogEditor.Document;
        var foldings = new List<NewFolding>();
        // Maps a folding's start offset to (header entry, collapsed title, group color). Header line text
        // is stable (we never rewrite it), so start offsets are stable and UpdateFoldings matches cleanly.
        var startOffsetToGroup = new Dictionary<int, (LogEntry Header, string Title, string? Color)>();

        var i = 0;
        while (i < LogEntries.Count)
        {
            // A header owns the following run of collapsed children.
            if (i + 1 < LogEntries.Count && LogEntries[i + 1].IsCollapsedChild)
            {
                var headerIndex = i;
                var lastChild = i + 1;
                while (lastChild + 1 < LogEntries.Count && LogEntries[lastChild + 1].IsCollapsedChild)
                    lastChild++;

                if (headerIndex + 1 <= doc.LineCount && lastChild + 1 <= doc.LineCount)
                {
                    var headerLine = doc.GetLineByNumber(headerIndex + 1);
                    var lastLine = doc.GetLineByNumber(lastChild + 1);
                    var header = LogEntries[headerIndex];
                    // Fold from the START of the header line so the whole raw first line is hidden too;
                    // when collapsed, ONLY the summary title is shown (no leftover raw first log line).
                    var summary = string.IsNullOrWhiteSpace(header.GroupSummary) ? null : header.GroupSummary;
                    // Pad the title with leading spaces so the summary text starts AFTER the reserved
                    // color-block gap (FoldColorMarkerRenderer.BlockWidth) and never overlaps the block.
                    var title = GetFoldTitlePrefix() + (summary ?? FormatLogLine(header));
                    // The color block reflects the FOLDED CONTENT color. Take the FIRST folded child's
                    // color (headerIndex + 1, the first line hidden inside the fold); fall back to the
                    // header color. The placeholder TEXT itself is fixed white.
                    var firstChild = LogEntries[headerIndex + 1];
                    var groupColor = firstChild.MergeColor ?? header.MergeColor;
                    foldings.Add(new NewFolding(headerLine.Offset, lastLine.EndOffset)
                    {
                        Name = title,
                        DefaultClosed = true
                    });
                    startOffsetToGroup[headerLine.Offset] = (header, title, groupColor);
                }

                i = lastChild + 1;
            }
            else
            {
                i++;
            }
        }

        // UpdateFoldings keeps IsFolded for existing sections (preserving the user's manual toggle) and
        // creates the rest. We then (a) refresh each collapsed title with the live summary, and (b) fold
        // each group exactly once — the first time its header appears — since DefaultClosed alone is not
        // reliably applied by UpdateFoldings.
        _foldingManager.UpdateFoldings(foldings, -1);
        _foldColorMarker?.Colors.Clear();
        foreach (var folding in _foldingManager.AllFoldings)
        {
            if (!startOffsetToGroup.TryGetValue(folding.StartOffset, out var group))
                continue;
            folding.Title = group.Title;
            if (_foldingInitialized.Add(group.Header))
                folding.IsFolded = true;
            // Record this fold's first-line content color so the overlay renderer can paint a full-width
            // underline strip below its summary line. This is what lets multiple folds show different
            // colors at the same time (AvalonEdit's own fold placeholder text is one global color).
            if (_foldColorMarker != null)
            {
                var stripBrush = LogLineColorizer.TryParseBrush(group.Color);
                if (stripBrush != null)
                    _foldColorMarker.Colors[folding.StartOffset] = stripBrush;
            }
        }
        LogEditor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// AvalonEdit LogLineColorizer: supports per-entry MergeResult custom colors, falling back to
    /// level-based colors.
    /// </summary>
    private sealed class LogLineColorizer : DocumentColorizingTransformer
    {
        private readonly IReadOnlyList<LogEntry> _entries;

        public LogLineColorizer(IReadOnlyList<LogEntry> entries)
        {
            _entries = entries;
        }

        private static Color? ParseMergeColor(string? colorText)
            => (TryParseBrush(colorText) as SolidColorBrush)?.Color;

        /// <summary>
        /// Parses a WPF color name / hex string into a frozen <see cref="SolidColorBrush"/>, or null when
        /// the text is empty/invalid. Shared by line coloring and the fold-box color.
        /// </summary>
        internal static System.Windows.Media.Brush? TryParseBrush(string? colorText)
        {
            if (string.IsNullOrWhiteSpace(colorText))
                return null;
            try
            {
                if (new System.Windows.Media.BrushConverter().ConvertFromString(colorText) is SolidColorBrush scb)
                {
                    scb.Freeze();
                    return scb;
                }
            }
            catch
            {
                // Ignore invalid color strings.
            }
            return null;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var index = line.LineNumber - 1;
            if (index < 0 || index >= _entries.Count)
                return;

            var entry = _entries[index];

            // Merge color takes priority over level-based color
            var mergeColor = ParseMergeColor(entry.MergeColor);
            if (mergeColor.HasValue)
            {
                var mergeBrush = new SolidColorBrush(mergeColor.Value);
                ChangeLinePart(line.Offset, line.EndOffset, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(mergeBrush);
                });
                return;
            }

            var brush = entry.Level switch
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

        /// <summary>
        /// The current collapse-group header entry (the most recent non-collapsed line). Subsequent
        /// entries whose merge script returns Collapse=true are folded under it. Null when no group is open.
        /// </summary>
        public LogEntry? GroupHeader { get; set; }

        /// <summary>The previous entry's full formatted line, fed to the merge script as PreviousLine.</summary>
        public string? LastLine { get; set; }

        /// <summary>
        /// The merge script's result for the previous line, fed to the next line as PreviousResult so a
        /// script can carry state forward via MergeResult.State. Runtime only; reset when the merge script
        /// is absent. Not restored on rebuild.
        /// </summary>
        public MergeResult? LastResult { get; set; }
    }


}
