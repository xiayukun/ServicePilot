using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class ServiceTemplateDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly ObservableCollection<ScriptStep> _steps = new();
    private readonly ServiceTemplate? _editingTemplate;
    private readonly ServiceConfig? _sourceService;
    private ScriptStep? _selectedStep;
    private ScriptStep? _variablesStep;
    private bool _loadingStep;

    public ServiceTemplateDialog(ServiceConfig? sourceService = null, ServiceTemplate? editingTemplate = null)
    {
        _sourceService = sourceService;
        _editingTemplate = editingTemplate;
        InitializeComponent();
        ApplyLocalization();
        StepsList.ItemsSource = _steps;
        ScriptTypeCombo.SelectedIndex = 0;
        StepKindCombo.SelectedIndex = 0;

        if (editingTemplate != null)
        {
            Title = LocalizationService.Current.T("EditTemplateTitle");
            NameBox.Text = editingTemplate.Name;
            DescriptionBox.Text = editingTemplate.Description;
            foreach (var step in editingTemplate.ScriptSteps.OrderBy(s => s.Order))
                _steps.Add(CloneStepPreserveId(step));
        }
        else if (sourceService != null)
        {
            Title = LocalizationService.Current.T("SaveTemplateTitle");
            NameBox.Text = sourceService.Name;
            foreach (var step in ScriptDefinitionService.CloneStepsWithNewIds(sourceService.ScriptSteps))
                _steps.Add(step);
        }
        else
        {
            Title = LocalizationService.Current.T("AddTemplateTitle");
        }

        RefreshStepDisplayLabels();
        ShowVariablesForCurrentStep();
    }

    public ServiceTemplate? Result { get; private set; }

    private void ApplyLocalization()
    {
        Title = _editingTemplate != null
            ? LocalizationService.Current.T("EditTemplateTitle")
            : _sourceService != null
                ? LocalizationService.Current.T("SaveTemplateTitle")
                : LocalizationService.Current.T("AddTemplateTitle");
        TemplateNameLabel.Text = LocalizationService.Current.T("TemplateName");
        DescriptionLabel.Text = LocalizationService.Current.T("Description");
        ScriptStepsLabel.Text = LocalizationService.Current.T("Actions");
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
        if (_selectedStep == null) return;
        _selectedStep.Name = string.IsNullOrWhiteSpace(StepNameBox.Text)
            ? LocalizationService.Current.T("UnnamedStep")
            : StepNameBox.Text.Trim();
        _selectedStep.Kind = StepKindCombo.SelectedIndex == 1 ? StepKind.Composite : StepKind.Action;
        _selectedStep.ScriptType = ScriptTypeCombo.SelectedIndex >= 0 ? (ScriptType)ScriptTypeCombo.SelectedIndex : ScriptType.Batch;
        _selectedStep.UseVariable = _selectedStep.Kind == StepKind.Action && (UseVariableCheck.IsChecked ?? true);
        _selectedStep.OpenLogOnRun = _selectedStep.Kind == StepKind.Action && (OpenLogOnRunCheck.IsChecked ?? false);
        _selectedStep.Content = _selectedStep.Kind == StepKind.Action ? ScriptEditor.Text ?? string.Empty : string.Empty;
        if (_selectedStep.Kind == StepKind.Action)
            _selectedStep.MemberStepIds.Clear();
        RefreshStepDisplayLabels();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        UpdateOrders();
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("EnterTemplateName"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_steps.Count == 0)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("AddOneStepPrompt"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!ValidateSteps())
            return;

        var now = DateTime.Now;
        Result = new ServiceTemplate
        {
            Id = _editingTemplate?.Id ?? Guid.NewGuid(),
            Name = NameBox.Text.Trim(),
            Description = DescriptionBox.Text.Trim(),
            CreatedAt = _editingTemplate?.CreatedAt ?? now,
            UpdatedAt = now,
            PresetVariables = [],
            ScriptSteps = _steps.Select(ScriptDefinitionService.CloneStep).Select((s, i) =>
            {
                s.Order = i;
                return s;
            }).ToList()
        };
        DialogResult = true;
        Close();
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
                    WpfMessageBoxHelper.Show(LocalizationService.Current.T("ActionContentRequired"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                WpfMessageBoxHelper.Show(LocalizationService.Current.T("CompositeMembersRequired"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (members.Any(m => m.Kind != StepKind.Action))
            {
                WpfMessageBoxHelper.Show(LocalizationService.Current.T("CompositeCannotNest"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (members.Count(m => m.UseVariable) > 1)
            {
                WpfMessageBoxHelper.Show(LocalizationService.Current.T("CompositeOneVariableMember"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private static List<string> ParseVariables(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
            MemberStepIds = source.MemberStepIds.ToList(),
            Order = source.Order,
            RunOnStart = source.RunOnStart
        };
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
