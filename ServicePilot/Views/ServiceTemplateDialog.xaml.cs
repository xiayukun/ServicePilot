using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class ServiceTemplateDialog : Window
{
    private readonly ObservableCollection<ScriptStep> _steps = new();
    private readonly ServiceTemplate? _editingTemplate;
    private readonly ServiceConfig? _sourceService;
    private ScriptStep? _selectedStep;
    private ScriptStep? _variablesStep;
    private string _templateVariablesText = string.Empty;
    private bool _loadingStep;

    public ServiceTemplateDialog(ServiceConfig? sourceService = null, ServiceTemplate? editingTemplate = null)
    {
        _sourceService = sourceService;
        _editingTemplate = editingTemplate;
        InitializeComponent();
        ApplyLocalization();
        StepsList.ItemsSource = _steps;
        ScriptTypeCombo.SelectedIndex = 0;

        if (editingTemplate != null)
        {
            Title = LocalizationService.Current.T("EditTemplateTitle");
            NameBox.Text = editingTemplate.Name;
            DescriptionBox.Text = editingTemplate.Description;
            _templateVariablesText = string.Join(Environment.NewLine, editingTemplate.PresetVariables);
            VariablesBox.Text = _templateVariablesText;
            foreach (var step in editingTemplate.ScriptSteps.OrderBy(s => s.Order))
                _steps.Add(CloneStepPreserveId(step));
        }
        else if (sourceService != null)
        {
            Title = LocalizationService.Current.T("SaveTemplateTitle");
            NameBox.Text = sourceService.Name;
            _templateVariablesText = string.Join(Environment.NewLine, sourceService.PresetVariables);
            VariablesBox.Text = _templateVariablesText;
            foreach (var step in sourceService.ScriptSteps.OrderBy(s => s.Order))
                _steps.Add(ScriptDefinitionService.CloneStep(step));
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
        ScriptStepsLabel.Text = LocalizationService.Current.T("ScriptSteps");
        DeleteStepButton.Content = LocalizationService.Current.T("DeleteShort");
        VariablesTitleText.Text = LocalizationService.Current.T("PresetVariables");
        VariablesHelpText.Text = LocalizationService.Current.T("StartupVariablesHelp");
        StepNameLabel.Text = LocalizationService.Current.T("StepName");
        ScriptTypeLabel.Text = LocalizationService.Current.T("ScriptType");
        UseVariableCheck.Content = LocalizationService.Current.T("UseVariable");
        RunOnStartCheck.Content = LocalizationService.Current.T("RunOnStart");
        OpenLogOnRunCheck.Content = LocalizationService.Current.T("OpenLogOnRun");
        ScriptContentLabel.Text = LocalizationService.Current.T("ScriptContent");
        CancelButton.Content = LocalizationService.Current.T("Cancel");
        SaveButton.Content = LocalizationService.Current.T("Save");
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        var step = new ScriptStep { Name = LocalizationService.Current.F("DefaultStepName", _steps.Count + 1), Order = _steps.Count };
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
        _steps.RemoveAt(idx);
        UpdateOrders();
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
        ScriptTypeCombo.SelectedIndex = (int)_selectedStep.ScriptType;
        UseVariableCheck.IsChecked = _selectedStep.UseVariable;
        RunOnStartCheck.IsChecked = _selectedStep.RunOnStart;
        OpenLogOnRunCheck.IsChecked = _selectedStep.OpenLogOnRun;
        ScriptEditor.Text = _selectedStep.Content;
        _loadingStep = false;
        ShowVariablesForCurrentStep();
    }

    private void SaveCurrentStep()
    {
        if (_selectedStep == null) return;
        _selectedStep.Name = string.IsNullOrWhiteSpace(StepNameBox.Text)
            ? LocalizationService.Current.T("UnnamedStep")
            : StepNameBox.Text.Trim();
        _selectedStep.ScriptType = ScriptTypeCombo.SelectedIndex >= 0 ? (ScriptType)ScriptTypeCombo.SelectedIndex : ScriptType.Batch;
        _selectedStep.UseVariable = UseVariableCheck.IsChecked ?? true;
        _selectedStep.RunOnStart = RunOnStartCheck.IsChecked ?? true;
        _selectedStep.OpenLogOnRun = OpenLogOnRunCheck.IsChecked ?? false;
        _selectedStep.Content = ScriptEditor.Text ?? string.Empty;
        RefreshStepDisplayLabels();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        UpdateOrders();
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(LocalizationService.Current.T("EnterTemplateName"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_steps.Count == 0)
        {
            MessageBox.Show(LocalizationService.Current.T("AddOneStepPrompt"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_steps.Any(s => string.IsNullOrWhiteSpace(s.Content)))
        {
            MessageBox.Show(LocalizationService.Current.T("StepContentRequired"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var now = DateTime.Now;
        Result = new ServiceTemplate
        {
            Id = _editingTemplate?.Id ?? Guid.NewGuid(),
            Name = NameBox.Text.Trim(),
            Description = DescriptionBox.Text.Trim(),
            CreatedAt = _editingTemplate?.CreatedAt ?? now,
            UpdatedAt = now,
            PresetVariables = ParseVariables(_templateVariablesText),
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

    private void UpdateOrders()
    {
        for (var i = 0; i < _steps.Count; i++)
            _steps[i].Order = i;
        RefreshStepDisplayLabels();
    }

    private void RunOnStartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingStep || _selectedStep == null)
            return;

        SaveVariablesBox();
        _selectedStep.RunOnStart = RunOnStartCheck.IsChecked ?? true;
        RefreshStepDisplayLabels();
        ShowVariablesForCurrentStep();
    }

    private void SaveVariablesBox()
    {
        if (_variablesStep == null)
        {
            _templateVariablesText = VariablesBox.Text ?? string.Empty;
            return;
        }

        _variablesStep.StepVariables = ParseVariables(VariablesBox.Text ?? string.Empty);
    }

    private void ShowVariablesForCurrentStep()
    {
        if (_selectedStep != null && !_selectedStep.RunOnStart)
        {
            _variablesStep = _selectedStep;
            VariablesTitleText.Text = LocalizationService.Current.T("ManualStepVariables");
            VariablesHelpText.Text = LocalizationService.Current.T("ManualStepVariablesHelp");
            VariablesBox.ToolTip = LocalizationService.Current.T("ManualStepVariablesTooltip");
            VariablesBox.Text = string.Join(Environment.NewLine, _selectedStep.StepVariables);
            return;
        }

        _variablesStep = null;
        VariablesTitleText.Text = LocalizationService.Current.T("PresetVariables");
        VariablesHelpText.Text = LocalizationService.Current.T("StartupVariablesHelp");
        VariablesBox.ToolTip = LocalizationService.Current.T("StartupVariablesTooltip");
        VariablesBox.Text = _templateVariablesText;
    }

    private void RefreshStepDisplayLabels()
    {
        var startupNumber = 1;
        foreach (var step in _steps.OrderBy(s => s.Order))
        {
            step.DisplayLabel = step.RunOnStart
                ? $"{startupNumber++}. {step.Name}"
                : step.Name;
        }

        StepsList.Items.Refresh();
    }

    private static List<string> ParseVariables(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ScriptStep CloneStepPreserveId(ScriptStep source)
    {
        var clone = ScriptDefinitionService.CloneStep(source);
        clone.Id = source.Id;
        return clone;
    }
}
