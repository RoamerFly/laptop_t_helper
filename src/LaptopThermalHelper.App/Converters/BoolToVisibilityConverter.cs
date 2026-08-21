using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LaptopThermalHelper.App.Converters;

/// <summary>
/// Converts a boolean value to Visibility. True → Visible, False → Collapsed.
/// Pass "Inverse" as the converter parameter to invert the logic.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        bool invert = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        return (flag ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
