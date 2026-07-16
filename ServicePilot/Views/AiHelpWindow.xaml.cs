using System.Runtime.InteropServices;
using System.Windows;
using ServicePilot.Services;

namespace ServicePilot.Views;

public partial class AiHelpWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly string _exePath;
    private readonly string _commandsText;


    public AiHelpWindow()
    {
        InitializeComponent();

        _exePath = AiHelpContentService.GetCurrentExePath();
        _commandsText = string.Join(Environment.NewLine, AiHelpContentService.BuildCommandList(_exePath));
        ApplyLocalization();
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            PromptBox.Text = AiHelpContentService.BuildPrompt(_exePath);
        });
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.Current.T("CopyServicePilotHelpForAi");
        IntroText.Text = LocalizationService.Current.T("AiHelpIntro");
        CopyAllButton.Content = LocalizationService.Current.T("CopyAll");
        CopyCommandsButton.Content = LocalizationService.Current.T("CopyCommands");
        CloseButton.Content = LocalizationService.Current.T("Close");

        PromptBox.Text = AiHelpContentService.BuildPrompt(_exePath);
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e) => CopyToClipboard(PromptBox.Text);

    private void CopyCommands_Click(object sender, RoutedEventArgs e) => CopyToClipboard(_commandsText);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
}
