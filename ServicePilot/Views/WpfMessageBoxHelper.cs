using System.Windows;

namespace ServicePilot.Views;

public static class WpfMessageBoxHelper
{
    public static MessageBoxResult Show(string text, string title,
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        return System.Windows.MessageBox.Show(text, title, button, icon);
    }
}