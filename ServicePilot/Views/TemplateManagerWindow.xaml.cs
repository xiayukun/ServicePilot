using System.Text;
using System.Windows;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class TemplateManagerWindow : Window
{
    private readonly AppConfig _appConfig;
    private readonly ConfigService _configService;
    private readonly Action _changed;

    public TemplateManagerWindow(AppConfig appConfig, ConfigService configService, Action changed)
    {
        _appConfig = appConfig;
        _configService = configService;
        _changed = changed;
        InitializeComponent();
        ApplyLocalization();
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
        Refresh();
    }

    private ServiceTemplate? SelectedTemplate => TemplatesGrid.SelectedItem as ServiceTemplate;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            TemplatesGrid.Items.Refresh();
            RefreshPreview();
        });
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.Current.T("ManageTemplates");
        AddButton.Content = LocalizationService.Current.T("Add");
        EditButton.Content = LocalizationService.Current.T("Edit");
        DeleteButton.Content = LocalizationService.Current.T("Delete");
        NameColumn.Header = LocalizationService.Current.T("Name");
        DescriptionColumn.Header = LocalizationService.Current.T("Description");
        StepsColumn.Header = LocalizationService.Current.T("Steps");
        VariablesColumn.Header = LocalizationService.Current.T("Variables");
        UpdatedAtColumn.Header = LocalizationService.Current.T("UpdatedAt");
    }

    private void Refresh()
    {
        TemplatesGrid.ItemsSource = null;
        TemplatesGrid.ItemsSource = _appConfig.ServiceTemplates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        PreviewBox.Text = string.Empty;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ServiceTemplateDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => string.Equals(t.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("TemplateNameExists", dialog.Result.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _appConfig.ServiceTemplates.Add(dialog.Result);
        await _configService.SaveAsync(_appConfig);
        Refresh();
        _changed();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null) return;

        var dialog = new ServiceTemplateDialog(editingTemplate: template) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => t.Id != template.Id &&
                                                  string.Equals(t.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(LocalizationService.Current.F("TemplateNameExists", dialog.Result.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var index = _appConfig.ServiceTemplates.FindIndex(t => t.Id == template.Id);
        if (index >= 0)
            _appConfig.ServiceTemplates[index] = dialog.Result;

        await _configService.SaveAsync(_appConfig);
        Refresh();
        _changed();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null) return;

        var confirm = MessageBox.Show(LocalizationService.Current.F("ConfirmDeleteTemplate", template.Name), "ServicePilot",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        _appConfig.ServiceTemplates.RemoveAll(t => t.Id == template.Id);
        await _configService.SaveAsync(_appConfig);
        Refresh();
        _changed();
    }

    private void TemplatesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var template = SelectedTemplate;
        PreviewBox.Text = template == null ? string.Empty : FormatPreview(template);
    }

    private static string FormatPreview(ServiceTemplate template)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(template.Description))
            builder.AppendLine(template.Description);

        if (template.PresetVariables.Count > 0)
            builder.AppendLine(LocalizationService.Current.T("Variables") + ": " + string.Join(", ", template.PresetVariables));

        var startupNumber = 1;
        foreach (var step in template.ScriptSteps.OrderBy(s => s.Order))
        {
            var label = step.RunOnStart ? $"{startupNumber++}. {step.Name}" : step.Name;
            builder.AppendLine();
            builder.AppendLine($"{label} ({step.ScriptType})");
            if (!step.RunOnStart && step.StepVariables.Count > 0)
                builder.AppendLine(LocalizationService.Current.T("StepVariables") + ": " + string.Join(", ", step.StepVariables));
            builder.AppendLine(step.Content);
        }

        return builder.ToString().Trim();
    }
}
