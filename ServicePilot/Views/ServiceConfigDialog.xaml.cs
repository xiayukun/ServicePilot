using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class ServiceConfigDialog : Window
{
    private readonly ObservableCollection<ScriptStep> _steps = new();
    private readonly IReadOnlyList<ServiceTemplate> _templates;
    private readonly Func<ServiceConfig, Window?, Task>? _saveTemplateAsync;
    private readonly ServiceConfig? _editingConfig;
    private ScriptStep? _selectedStep;
    private ScriptStep? _variablesStep;
    private string _serviceVariablesText = string.Empty;
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
        SaveTemplateButton.Visibility = config == null || saveTemplateAsync == null
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (config != null)
        {
            Title = LocalizationService.Current.T("EditServiceTitle");
            NameBox.Text = config.Name;
            DirBox.Text = config.WorkingDirectory;
            AutoStartCheck.IsChecked = config.AutoStart;
            _serviceVariablesText = string.Join(Environment.NewLine, config.PresetVariables);
            VariablesBox.Text = _serviceVariablesText;
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
        ScriptStepsLabel.Text = LocalizationService.Current.T("ScriptSteps");
        ApplyTemplateButton.Content = LocalizationService.Current.T("ApplyTemplate");
        DeleteStepButton.Content = LocalizationService.Current.T("DeleteShort");
        VariablesTitleText.Text = LocalizationService.Current.T("PresetVariables");
        VariablesHelpText.Text = LocalizationService.Current.T("StartupVariablesHelp");
        StepNameLabel.Text = LocalizationService.Current.T("StepName");
        ScriptTypeLabel.Text = LocalizationService.Current.T("ScriptType");
        UseVariableCheck.Content = LocalizationService.Current.T("UseVariable");
        RunOnStartCheck.Content = LocalizationService.Current.T("RunOnStart");
        ScriptContentLabel.Text = LocalizationService.Current.T("ScriptContent");
        AutoStartCheck.Content = LocalizationService.Current.T("AutoStartService");
        SaveTemplateButton.Content = LocalizationService.Current.T("SaveAsTemplate");
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
        StepEditor.Visibility = _steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentStep();
        SaveVariablesBox();
        if (_templates.Count == 0)
        {
            MessageBox.Show(LocalizationService.Current.T("NoTemplatesAvailable"), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new TemplateSelectDialog(_templates) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedTemplate == null)
            return;

        NameBox.Text = dialog.SelectedTemplate.Name;
        _serviceVariablesText = string.Join(Environment.NewLine, dialog.SelectedTemplate.PresetVariables);
        _variablesStep = null;
        VariablesBox.Text = _serviceVariablesText;
        _steps.Clear();
        foreach (var step in dialog.SelectedTemplate.ScriptSteps.OrderBy(s => s.Order).Select(ScriptDefinitionService.CloneStep))
        {
            step.Order = _steps.Count;
            _steps.Add(step);
        }

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
        ScriptTypeCombo.SelectedIndex = (int)_selectedStep.ScriptType;
        UseVariableCheck.IsChecked = _selectedStep.UseVariable;
        RunOnStartCheck.IsChecked = _selectedStep.RunOnStart;
        ScriptEditor.Text = _selectedStep.Content;
        SetScriptHighlighting(ScriptEditor, _selectedStep.ScriptType);
        _loadingStep = false;
        ShowVariablesForCurrentStep();
    }

    private void SaveCurrentStep()
    {
        if (_selectedStep == null)
            return;

        _selectedStep.Name = string.IsNullOrWhiteSpace(StepNameBox.Text)
            ? LocalizationService.Current.T("UnnamedStep")
            : StepNameBox.Text.Trim();
        _selectedStep.ScriptType = ScriptTypeCombo.SelectedIndex >= 0 ? (ScriptType)ScriptTypeCombo.SelectedIndex : ScriptType.Batch;
        _selectedStep.UseVariable = UseVariableCheck.IsChecked ?? true;
        _selectedStep.RunOnStart = RunOnStartCheck.IsChecked ?? true;
        _selectedStep.Content = ScriptEditor.Text ?? string.Empty;
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
            MessageBox.Show(LocalizationService.Current.T("EnterServiceName"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(DirBox.Text))
        {
            MessageBox.Show(LocalizationService.Current.T("SelectDirectoryPrompt"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_steps.Count == 0)
        {
            MessageBox.Show(LocalizationService.Current.T("AddOneStepPrompt"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_steps.Any(s => string.IsNullOrWhiteSpace(s.Content)))
        {
            MessageBox.Show(LocalizationService.Current.T("StepContentRequired"), LocalizationService.Current.T("Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        result = new ServiceConfig
        {
            Id = _editingConfig?.Id ?? Guid.NewGuid(),
            Name = NameBox.Text.Trim(),
            WorkingDirectory = DirBox.Text.Trim(),
            AutoStart = AutoStartCheck.IsChecked ?? false,
            SortOrder = _editingConfig?.SortOrder ?? 0,
            CreatedAt = _editingConfig?.CreatedAt ?? DateTime.Now,
            PresetVariables = ParseVariables(_serviceVariablesText),
            ScriptSteps = _steps.Select(CloneStepPreserveId).ToList()
        };

        return true;
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
            _serviceVariablesText = VariablesBox.Text ?? string.Empty;
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
        VariablesBox.Text = _serviceVariablesText;
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
            ScriptType = source.ScriptType,
            UseVariable = source.UseVariable,
            RunOnStart = source.RunOnStart,
            StepVariables = source.StepVariables.ToList(),
            Content = source.Content,
            Order = source.Order
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
