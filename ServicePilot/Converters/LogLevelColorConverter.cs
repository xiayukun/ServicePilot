using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ServicePilot.Models;

namespace ServicePilot.Converters;

public class LogLevelColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            var color = level switch
            {
                LogLevel.Info => Color.FromRgb(0xDD, 0xDD, 0xDD),
                LogLevel.Warning => Color.FromRgb(0xFF, 0xD5, 0x4F),
                LogLevel.Error => Color.FromRgb(0xF4, 0x43, 0x36),
                LogLevel.System => Color.FromRgb(0x88, 0x88, 0x88),
                _ => Color.FromRgb(0xDD, 0xDD, 0xDD)
            };
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
