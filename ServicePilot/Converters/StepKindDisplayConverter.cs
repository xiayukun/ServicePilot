using System.Globalization;
using System.Windows.Data;
using ServicePilot.Models;
using ServicePilot.Services;

namespace ServicePilot.Converters;

public sealed class StepKindDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is StepKind kind
            ? LocalizationService.Current.T(kind == StepKind.Composite ? "Composite" : "Action")
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
