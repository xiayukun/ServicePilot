using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class TemplateManagerWindow : Wpf.Ui.Controls.FluentWindow
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
        ExportButton.Content = LocalizationService.Current.T("Export");
        ImportButton.Content = LocalizationService.Current.T("Import");
        NameColumn.Header = LocalizationService.Current.T("Name");
        DescriptionColumn.Header = LocalizationService.Current.T("Description");
        StepsColumn.Header = LocalizationService.Current.T("Actions");
        UpdatedAtColumn.Header = LocalizationService.Current.T("UpdatedAt");
    }

    private void Refresh()
    {
        TemplatesGrid.ItemsSource = null;
        TemplatesGrid.ItemsSource = _appConfig.ServiceTemplates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        PreviewBox.Text = string.Empty;
    }

    public void RefreshAfterConfigChanged()
    {
        var selectedId = SelectedTemplate?.Id;
        TemplatesGrid.ItemsSource = null;
        var templates = _appConfig.ServiceTemplates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        TemplatesGrid.ItemsSource = templates;
        if (selectedId.HasValue)
            TemplatesGrid.SelectedItem = templates.FirstOrDefault(t => t.Id == selectedId.Value);
        RefreshPreview();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ServiceTemplateDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        if (_appConfig.ServiceTemplates.Any(t => string.Equals(t.Name, dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateNameExists", dialog.Result.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateNameExists", dialog.Result.Name), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var confirm = WpfMessageBoxHelper.Show(LocalizationService.Current.F("ConfirmDeleteTemplate", template.Name), "ServicePilot",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        _appConfig.ServiceTemplates.RemoveAll(t => t.Id == template.Id);
        await _configService.SaveAsync(_appConfig);
        Refresh();
        _changed();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var template = SelectedTemplate;
        if (template == null)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.T("SelectTemplatePrompt"), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationService.Current.T("ExportTemplate"),
            Filter = LocalizationService.Current.T("TemplateFileFilter"),
            FileName = SafeFileName(template.Name) + TemplateExchangeService.DefaultExtension,
            AddExtension = true,
            DefaultExt = TemplateExchangeService.DefaultExtension
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await TemplateExchangeService.ExportAsync(template, dialog.FileName);
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateExported", dialog.FileName), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateExportFailed", ex.Message), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Current.T("ImportTemplate"),
            Filter = LocalizationService.Current.T("TemplateFileFilter"),
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var (imported, skipped) = await TemplateExchangeService.ImportAsync(dialog.FileName, _appConfig.ServiceTemplates);
            _appConfig.ServiceTemplates.AddRange(imported.Select(i => i.Template));
            await _configService.SaveAsync(_appConfig);
            Refresh();
            _changed();

            var names = string.Join(", ", imported.Select(i => i.Template.Name));
            var skipMsg = skipped.Count > 0 ? $"\n{LocalizationService.Current.F("TemplateImportSkipped", skipped.Count)}" : "";
            WpfMessageBoxHelper.Show($"{LocalizationService.Current.F("TemplateImported", imported.Count)}: {names}{skipMsg}", "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBoxHelper.Show(LocalizationService.Current.F("TemplateImportFailed", ex.Message), "ServicePilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        foreach (var step in template.ScriptSteps.OrderBy(s => s.Order))
        {
            builder.AppendLine();
            if (step.Kind == StepKind.Composite)
            {
                builder.AppendLine($"{step.Name} ({LocalizationService.Current.T("Composite")})");
                builder.AppendLine(LocalizationService.Current.T("Members") + ": " + ResolveMemberNames(template, step));
                continue;
            }

            builder.AppendLine($"{step.Name} ({LocalizationService.Current.T("Action")} / {step.ScriptType})");
            if (step.StepVariables.Count > 0)
                builder.AppendLine(LocalizationService.Current.T("StepVariables") + ": " + string.Join(", ", step.StepVariables));
            builder.AppendLine(step.Content);
        }

        return builder.ToString().Trim();
    }

    private static string ResolveMemberNames(ServiceTemplate template, ScriptStep composite)
    {
        var byId = template.ScriptSteps.ToDictionary(s => s.Id);
        var names = composite.MemberStepIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id].Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        return names.Count == 0 ? LocalizationService.Current.T("NoActions") : string.Join(" -> ", names);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var name = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? "template" : name;
    }
}
