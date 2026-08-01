using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SystemPerformanceAccelerator.Desktop.Converters;

public sealed class SubtractDoubleConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not double source ||
            double.IsNaN(source) ||
            double.IsInfinity(source) ||
            !double.TryParse(
                parameter?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return DependencyProperty.UnsetValue;
        }

        return Math.Max(0, source - amount);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
