using System.Windows;

namespace ServicePilot.Views;

public partial class WpfConfirmDialog : Wpf.Ui.Controls.FluentWindow
{
    public bool Confirmed { get; private set; }

    public WpfConfirmDialog(string message, string title = "ServicePilot")
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
