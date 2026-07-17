using System.Windows;

namespace ServicePilot.Views;

public static class WpfMessageBoxHelper
{
    public static MessageBoxResult Show(string text, string title,
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        if (button == MessageBoxButton.YesNo || button == MessageBoxButton.YesNoCancel)
        {
            var dialog = new WpfConfirmDialog(text, title);
            var result = dialog.ShowDialog();
            if (result == true && dialog.Confirmed)
                return MessageBoxResult.Yes;
            return MessageBoxResult.No;
        }

        return System.Windows.MessageBox.Show(text, title, button, icon);
    }
}
