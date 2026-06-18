using System.Windows;
using System.Windows.Input;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class TemplateSelectDialog : Window
{
    public TemplateSelectDialog(IReadOnlyList<ServiceTemplate> templates)
    {
        InitializeComponent();
        Title = LocalizationService.Current.T("ApplyTemplate");
        CancelButton.Content = LocalizationService.Current.T("Cancel");
        ApplyButton.Content = LocalizationService.Current.T("Apply");
        TemplatesList.ItemsSource = templates
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TemplateChoice(t))
            .ToList();
        TemplatesList.SelectedIndex = 0;
    }

    public ServiceTemplate? SelectedTemplate { get; private set; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplate = (TemplatesList.SelectedItem as TemplateChoice)?.Template;
        if (SelectedTemplate == null)
            return;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TemplatesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Apply_Click(sender, e);
    }

    private sealed class TemplateChoice(ServiceTemplate template)
    {
        public ServiceTemplate Template { get; } = template;

        public string Summary => LocalizationService.Current.F(
            "TemplateSummary",
            Template.ScriptSteps.Count(s => s.Kind == StepKind.Action),
            Template.ScriptSteps.Count(s => s.Kind == StepKind.Composite));
    }
}
