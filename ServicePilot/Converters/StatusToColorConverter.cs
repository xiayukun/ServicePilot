using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ServicePilot.Models;

namespace ServicePilot.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ProcessState state)
        {
            var color = state switch
            {
                ProcessState.Running => Color.FromRgb(0x4C, 0xAF, 0x50),   // 绿色
                ProcessState.Starting => Color.FromRgb(0xFF, 0x98, 0x00),  // 橙色
                ProcessState.Stopping => Color.FromRgb(0xFF, 0x98, 0x00),  // 橙色
                ProcessState.Error => Color.FromRgb(0xF4, 0x43, 0x36),     // 红色
                ProcessState.StartFailed => Color.FromRgb(0xF4, 0x43, 0x36), // 红色
                ProcessState.Completed => Color.FromRgb(0x21, 0x96, 0xF3), // 蓝色
                _ => Color.FromRgb(0x9E, 0x9E, 0x9E)                       // 灰色
            };
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
