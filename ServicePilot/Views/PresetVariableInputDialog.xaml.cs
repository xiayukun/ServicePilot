using System.Windows;

namespace ServicePilot.Views;

public partial class PresetVariableInputDialog : Wpf.Ui.Controls.FluentWindow
{
    public PresetVariableInputDialog(string? defaultValue)
    {
        InitializeComponent();
        ApplyLocalization();
        VariableBox.Text = defaultValue ?? string.Empty;

        Loaded += (_, _) =>
        {
            VariableBox.Focus();
            VariableBox.SelectAll();
        };
    }

    public string Variable => VariableBox.Text.Trim();

    private void ApplyLocalization()
    {
        Title = ServicePilot.Services.LocalizationService.Current.T("AddVariableTitle");
        PromptText.Text = ServicePilot.Services.LocalizationService.Current.T("EnterVariable");
        OkButton.Content = ServicePilot.Services.LocalizationService.Current.T("Ok");
        CancelButton.Content = ServicePilot.Services.LocalizationService.Current.T("Cancel");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Variable))
        {
            WpfMessageBoxHelper.Show(ServicePilot.Services.LocalizationService.Current.T("VariableRequired"), "ServicePilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
