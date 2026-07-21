using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class ServiceConfigDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly ObservableCollection<ScriptStep> _steps = new();
    private readonly IReadOnlyList<ServiceTemplate> _templates;
    private readonly Func<ServiceConfig, Window?, Task>? _saveTemplateAsync;
    private readonly ServiceConfig? _editingConfig;
    private ScriptStep? _selectedStep;
    private ScriptStep? _variablesStep;
    private bool _loadingStep;

    public ServiceConfig? Result { get; private set; }

    public ServiceConfigDialog(
        IReadOnlyList<ServiceTemplate>? templates = null,
        Func<ServiceConfig, Window?, Task>? saveTemplateAsync = null)
        : this(null, templates, saveTemplateAsync)
    {
    }

    public ServiceConfigDialog(
        ServiceConfig? config,
        IReadOnlyList<ServiceTemplate>? templates = null,
        Func<ServiceConfig, Window?, Task>? saveTemplateAsync = null)
    {
        _templates = templates ?? [];
        _saveTemplateAsync = saveTemplateAsync;
        _editingConfig = config;
        InitializeComponent();
        ApplyLocalization();
        StepsList.ItemsSource = _steps;
        ScriptTypeCombo.SelectedIndex = 0;
        StepKindCombo.SelectedIndex = 0;
        SaveTemplateButton.Visibility = config == null || saveTemplateAsync == null
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (config != null)
        {
            Title = LocalizationService.Current.T("EditServiceTitle");
            NameBox.Text = config.Name;
            DirBox.Text = config.WorkingDirectory;
            foreach (var step in config.ScriptSteps.OrderBy(s => s.Order))
                _steps.Add(CloneStepPreserveId(step));
        }
        else
        {
            Title = LocalizationService.Current.T("AddServiceTitle");
        }

        RefreshStepDisplayLabels();
        ShowVariablesForCurrentStep();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = LocalizationService.Current.T("SelectWorkingDirectory") };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DirBox.Text = dialog.SelectedPath;
    }

    private void ApplyLocalization()
    {
        Title = _editingConfig == null ? LocalizationService.Current.T("AddServiceTitle") : LocalizationService.Current.T("EditServiceTitle");
        ServiceNameLabel.Text = LocalizationService.Current.T("ServiceName");
        WorkingDirectoryLabel.Text = LocalizationService.Current.T("WorkingDirectory");
        BrowseButton.Content = LocalizationService.Current.T("Browse");
        ScriptStepsLabel.Text = LocalizationService.Current.T("Actions");
        ApplyTemplateButton.Content = LocalizationService.Current.T("ApplyTemplate");
        DeleteStepButton.Content = LocalizationService.Current.T("DeleteShort");
        VariablesTitleText.Text = LocalizationService.Current.T("StepVariables");
        VariablesHelpText.Text = LocalizationService.Current.T("StepVariablesHelp");
        StepNameLabel.Text = LocalizationService.Current.T("StepName");
        StepKindLabel.Text = LocalizationService.Current.T("StepKind");
        SetComboItemContent(StepKindCombo, 0, LocalizationService.Current.T("Action"));
        SetComboItemContent(StepKindCombo, 1, LocalizationService.Current.T("Composite"));
        ScriptTypeLabel.Text = LocalizationService.Current.T("ScriptType");
        MembersLabel.Text = LocalizationService.Current.T("Members");
        AddMemberButton.Content = LocalizationService.Current.T("Add");
        RemoveMemberButton.Content = LocalizationService.Current.T("Delete");
        UseVariableCheck.Content = LocalizationService.Current.T("UseVariable");
        OpenLogOnRunCheck.Content = LocalizationService.Current.T("OpenLogOnRun");
        ScriptContentLabel.Text = LocalizationService.Current.T("ScriptContent");
        MergeFunctionLabel.Text = LocalizationService.Current.T("MergeFunction");
        SaveTemplateButton.Content = LocalizationService.Current.T("SaveAsTemplate");
        CancelButton.Content = LocalizationService.Current.T("Cancel");
        SaveButton.Content = LocalizationService.Current.T("Save");
    }

    private static void SetComboItemContent(System.Windows.Controls.ComboBox comboBox, int index, string content)
    {
        if (comboBox.Items.Count > index && comboBox.Items[index] is ComboBoxItem item)
            item.Content = content;
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        var step = new ScriptStep
        {
            Name = LocalizationService.Current.F("DefaultStepName", _steps.Count + 1),
            Kind = StepKind.Action,
            Order = _steps.Count
        };
        _steps.Add(step);
        StepsList.SelectedIndex = _steps.Count - 1;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        var idx = StepsList.SelectedIndex;
        if (idx <= 0) return;
        _steps.Move(idx, idx - 1);
        UpdateOrders();
        StepsList.SelectedIndex = idx - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        var idx = StepsList.SelectedIndex;
        if (idx < 0 || idx >= _steps.Count - 1) return;
        _steps.Move(idx, idx + 1);
        UpdateOrders();
        StepsList.SelectedIndex = idx + 1;
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        SaveVariablesBox();
        var idx = StepsList.SelectedIndex;
        if (idx < 0 || idx >= _steps.Count) return;
        var removedId = _steps[idx].Id;
        _steps.RemoveAt(idx);
        foreach (var composite in _steps.Where(s => s.Kind == StepKind.Composite))
            composite.MemberStepIds.RemoveAll(id => id == removedId);
        UpdateOrders();
        StepEditor.Visibility = _steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        if (_templates.Count == 0)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("NoTemplatesAvailable"), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new TemplateSelectDialog(_templates) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedTemplate == null)
            return;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = dialog.SelectedTemplate.Name;

        _variablesStep = null;
        _steps.Clear();
        foreach (var step in ScriptDefinitionService.CloneStepsWithNewIds(dialog.SelectedTemplate.ScriptSteps))
            _steps.Add(step);

        RefreshStepDisplayLabels();
        StepsList.SelectedIndex = _steps.Count > 0 ? 0 : -1;
        ShowVariablesForCurrentStep();
    }

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        _selectedStep = StepsList.SelectedItem as ScriptStep;
        StepEditor.Visibility = _selectedStep != null ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedStep == null)
        {
            ShowVariablesForCurrentStep();
            return;
        }

        _loadingStep = true;
        StepNameBox.Text = _selectedStep.Name;
        StepKindCombo.SelectedIndex = _selectedStep.Kind == StepKind.Composite ? 1 : 0;
        ScriptTypeCombo.SelectedIndex = (int)_selectedStep.ScriptType;
        UseVariableCheck.IsChecked = _selectedStep.UseVariable;
        OpenLogOnRunCheck.IsChecked = _selectedStep.OpenLogOnRun;
        ScriptEditor.Text = _selectedStep.Content;
        SetScriptHighlighting(ScriptEditor, _selectedStep.ScriptType);
        LoadMergeScript(_selectedStep);
        _loadingStep = false;
        UpdateStepEditorMode();
        ShowVariablesForCurrentStep();
    }

    private void StepKindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStep || _selectedStep == null)
            return;

        SaveVariablesBox();
        _selectedStep.Kind = StepKindCombo.SelectedIndex == 1 ? StepKind.Composite : StepKind.Action;
        if (_selectedStep.Kind == StepKind.Action)
            _selectedStep.MemberStepIds.Clear();
        else
            _selectedStep.Content = string.Empty;
        UpdateStepEditorMode();
        RefreshStepDisplayLabels();
        ShowVariablesForCurrentStep();
    }

    private void SaveCurrentStep()
    {
        if (_selectedStep == null)
            return;

        _selectedStep.Name = string.IsNullOrWhiteSpace(StepNameBox.Text)
            ? LocalizationService.Current.T("UnnamedStep")
            : StepNameBox.Text.Trim();
        _selectedStep.Kind = StepKindCombo.SelectedIndex == 1 ? StepKind.Composite : StepKind.Action;
        _selectedStep.ScriptType = ScriptTypeCombo.SelectedIndex >= 0 ? (ScriptType)ScriptTypeCombo.SelectedIndex : ScriptType.Batch;
        _selectedStep.UseVariable = _selectedStep.Kind == StepKind.Action && (UseVariableCheck.IsChecked ?? true);
        _selectedStep.OpenLogOnRun = _selectedStep.Kind == StepKind.Action && (OpenLogOnRunCheck.IsChecked ?? false);
        _selectedStep.Content = _selectedStep.Kind == StepKind.Action ? ScriptEditor.Text ?? string.Empty : string.Empty;
        _selectedStep.LogMergeScript = _selectedStep.Kind == StepKind.Action ? MergeScriptEditor.Text ?? string.Empty : null;
        if (_selectedStep.Kind == StepKind.Action)
            _selectedStep.MemberStepIds.Clear();
        RefreshStepDisplayLabels();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildResult(out var result))
            return;

        Result = result!;
        DialogResult = true;
        Close();
    }

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_saveTemplateAsync == null)
            return;

        if (!TryBuildResult(out var result))
            return;

        await _saveTemplateAsync(result!, this);
    }

    private bool TryBuildResult(out ServiceConfig? result)
    {
        result = null;
        SaveCurrentStep();
        SaveVariablesBox();
        UpdateOrders();

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("EnterServiceName"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(DirBox.Text))
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("SelectDirectoryPrompt"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_steps.Count == 0)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("AddOneStepPrompt"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!ValidateSteps())
            return false;

        result = new ServiceConfig
        {
            Id = _editingConfig?.Id ?? Guid.NewGuid(),
            Name = NameBox.Text.Trim(),
            WorkingDirectory = DirBox.Text.Trim(),
            AutoStart = _editingConfig?.AutoStart ?? false,
            SortOrder = _editingConfig?.SortOrder ?? 0,
            CreatedAt = _editingConfig?.CreatedAt ?? DateTime.Now,
            PresetVariables = [],
            ScriptSteps = _steps.Select(CloneStepPreserveId).ToList()
        };

        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddMember_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        if (_selectedStep?.Kind != StepKind.Composite || MemberCandidateCombo.SelectedItem is not ScriptStep action)
            return;

        if (!_selectedStep.MemberStepIds.Contains(action.Id))
            _selectedStep.MemberStepIds.Add(action.Id);
        RefreshMembers();
    }

    private void RemoveMember_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStep?.Kind != StepKind.Composite || MembersList.SelectedItem is not ScriptStep action)
            return;

        _selectedStep.MemberStepIds.Remove(action.Id);
        RefreshMembers();
    }

    private void MoveMemberUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedMember(-1);
    }

    private void MoveMemberDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedMember(1);
    }

    private void MoveSelectedMember(int direction)
    {
        if (_selectedStep?.Kind != StepKind.Composite || MembersList.SelectedItem is not ScriptStep action)
            return;

        var index = _selectedStep.MemberStepIds.FindIndex(id => id == action.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _selectedStep.MemberStepIds.Count)
            return;

        (_selectedStep.MemberStepIds[index], _selectedStep.MemberStepIds[target]) =
            (_selectedStep.MemberStepIds[target], _selectedStep.MemberStepIds[index]);
        RefreshMembers(action.Id);
    }

    private void UpdateOrders()
    {
        for (var i = 0; i < _steps.Count; i++)
            _steps[i].Order = i;
        RefreshStepDisplayLabels();
    }

    private void SaveVariablesBox()
    {
        if (_variablesStep == null)
            return;

        _variablesStep.StepVariables = ParseVariables(VariablesBox.Text ?? string.Empty);
    }

    private void ShowVariablesForCurrentStep()
    {
        if (_selectedStep is { Kind: StepKind.Action, UseVariable: true })
        {
            _variablesStep = _selectedStep;
            VariablesTitleText.Text = LocalizationService.Current.T("StepVariables");
            VariablesHelpText.Text = LocalizationService.Current.T("StepVariablesHelp");
            VariablesBox.ToolTip = LocalizationService.Current.T("StepVariablesTooltip");
            VariablesBox.IsEnabled = true;
            VariablesBox.Text = string.Join(Environment.NewLine, _selectedStep.StepVariables);
            return;
        }

        _variablesStep = null;
        VariablesTitleText.Text = LocalizationService.Current.T("StepVariables");
        VariablesHelpText.Text = LocalizationService.Current.T("NoStepVariablesHelp");
        VariablesBox.ToolTip = null;
        VariablesBox.Text = string.Empty;
        VariablesBox.IsEnabled = false;
    }

    private void UpdateStepEditorMode()
    {
        if (_selectedStep == null)
            return;

        var isComposite = StepKindCombo.SelectedIndex == 1;
        ScriptTypePanel.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
        UseVariableCheck.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
        OpenLogOnRunCheck.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
        ScriptContentLabel.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
        ScriptEditor.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
        MergeScriptPanel.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
        CompositeMembersPanel.Visibility = isComposite ? Visibility.Visible : Visibility.Collapsed;
        RefreshMembers();
    }

    private void RefreshMembers(Guid? selectedMemberId = null)
    {
        if (_selectedStep?.Kind != StepKind.Composite)
        {
            MembersList.ItemsSource = null;
            MemberCandidateCombo.ItemsSource = null;
            return;
        }

        var actions = _steps
            .Where(s => s.Kind == StepKind.Action && s.Id != _selectedStep.Id)
            .OrderBy(s => s.Order)
            .ToList();
        var byId = actions.ToDictionary(s => s.Id);
        var members = _selectedStep.MemberStepIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
        _selectedStep.MemberStepIds = members.Select(s => s.Id).ToList();

        MembersList.ItemsSource = members;
        if (selectedMemberId.HasValue)
            MembersList.SelectedItem = members.FirstOrDefault(s => s.Id == selectedMemberId.Value);

        MemberCandidateCombo.ItemsSource = actions.Where(s => !_selectedStep.MemberStepIds.Contains(s.Id)).ToList();
        MemberCandidateCombo.SelectedIndex = MemberCandidateCombo.Items.Count > 0 ? 0 : -1;
    }

    private bool ValidateSteps()
    {
        var byId = _steps.ToDictionary(s => s.Id);
        foreach (var step in _steps)
        {
            if (step.Kind == StepKind.Action)
            {
                if (string.IsNullOrWhiteSpace(step.Content))
                {
                    WpfMessageBoxHelper.Show(LocalizationService.Current.T("ActionContentRequired"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                continue;
            }

            var members = step.MemberStepIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
            if (members.Count == 0)
            {
                WpfMessageBoxHelper.Show(LocalizationService.Current.T("CompositeMembersRequired"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (members.Any(m => m.Kind != StepKind.Action))
            {
                WpfMessageBoxHelper.Show(LocalizationService.Current.T("CompositeCannotNest"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (members.Count(m => m.UseVariable) > 1)
            {
                WpfMessageBoxHelper.Show(LocalizationService.Current.T("CompositeOneVariableMember"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        return true;
    }

    private void RefreshStepDisplayLabels()
    {
        foreach (var step in _steps.OrderBy(s => s.Order))
            step.DisplayLabel = step.Name;

        StepsList.Items.Refresh();
    }

    private static List<string> ParseVariables(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ScriptStep CloneStepPreserveId(ScriptStep source)
    {
        return new ScriptStep
        {
            Id = source.Id,
            Name = source.Name,
            Kind = source.Kind,
            ScriptType = source.ScriptType,
            UseVariable = source.UseVariable,
            OpenLogOnRun = source.OpenLogOnRun,
            StepVariables = source.StepVariables.ToList(),
            Content = source.Content,
            LogMergeScript = source.LogMergeScript,
            MemberStepIds = source.MemberStepIds.ToList(),
            Order = source.Order,
            RunOnStart = source.RunOnStart
        };
    }

    private void LoadMergeScript(ScriptStep step)
    {
        if (step.Kind != StepKind.Action)
        {
            MergeScriptEditor.Text = string.Empty;
            return;
        }

        var script = step.LogMergeScript;
        if (string.IsNullOrWhiteSpace(script))
            script = GetDefaultMergeFunctionTemplate();

        MergeScriptEditor.Text = script;
        MergeScriptEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance
            .GetDefinitionByExtension(".cs");
    }

    private static string GetDefaultMergeFunctionTemplate()
    {
        return LocalizationService.Current.IsEnglish
            ? """
              // Inputs (available as locals):
              //   CurrentLine  (string?)  the current log line: "HH:mm:ss [Level] message"
              //   PreviousLine (string?)  the previous log line (null on the first line)
              //   PreviousResult        (MergeResult?) the result returned for the previous line
              //   PreviousWasCollapsed  (bool) was the previous line folded?
              //   InCollapseGroup       (bool) is a fold group currently open?
              // Return a MergeResult, or null to keep the line unchanged.
              // Multi-line logic: use a variable then return it.
              //   MergeResult? result; if (...) result = ...; return result;
              //
              // Fold: the FIRST line of a group returns Collapse = false (it becomes the
              // header/summary); following lines return Collapse = true to fold under it.
              // MergedMessage on the header is the text shown on the collapsed one line.
              //
              // Carry state to the next line via State (runtime only, simple values only):
              //   var count = (PreviousResult?.State? ["count"] as int?) ?? 0;
              //   return new MergeResult { State = new() { ["count"] = count + 1 } };
              new MergeResult
              {
                  MergedMessage = CurrentLine,
                  // Color = "Gray",     // any WPF color name or #RRGGBB
                  // Collapse = false,   // true = fold this line into the current group
                  // State = null        // Dictionary<string, object?> passed to the next line
              }
              """
            : """
              // 输入（可直接作为局部变量使用）：
              //   CurrentLine  (string?)  当前日志行："HH:mm:ss [级别] 消息"
              //   PreviousLine (string?)  上一行日志（首行为 null）
              //   PreviousResult        (MergeResult?) 上一行返回的结果
              //   PreviousWasCollapsed  (bool) 上一行是否被折叠
              //   InCollapseGroup       (bool) 当前是否已有打开的折叠组
              // 返回 MergeResult，或返回 null 保持原行不变。
              // 多行逻辑：用变量再返回。
              //   MergeResult? result; if (...) result = ...; return result;
              //
              // 折叠：一组的“第一行”返回 Collapse = false（作为组头/摘要行）；
              // 后续行返回 Collapse = true 折叠进该组。组头的 MergedMessage 就是折叠成一行时显示的文字。
              //
              // 通过 State 把状态传给下一行（仅运行期、只存简单类型）：
              //   var count = (PreviousResult?.State? ["count"] as int?) ?? 0;
              //   return new MergeResult { State = new() { ["count"] = count + 1 } };
              new MergeResult
              {
                  MergedMessage = CurrentLine,
                  // Color = "Gray",     // 任意 WPF 颜色名或 #RRGGBB
                  // Collapse = false,   // true = 把本行折叠进当前组
                  // State = null        // Dictionary<string, object?>，传给下一行
              }
              """;
    }

    private static void SetScriptHighlighting(ICSharpCode.AvalonEdit.TextEditor editor, ScriptType scriptType)
    {
        var ext = scriptType switch
        {
            ScriptType.Batch => ".bat",
            ScriptType.PowerShell => ".ps1",
            ScriptType.Python => ".py",
            ScriptType.Node => ".js",
            _ => ".bat"
        };
        editor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance
            .GetDefinitionByExtension(ext);
    }
}
