using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.App.Converters;

public sealed class ThermalLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string resourceKey = value is ThermalLevel level ? level switch
        {
            ThermalLevel.Normal => "SuccessBrush",
            ThermalLevel.Elevated => "WarningBrush",
            ThermalLevel.High => "HighBrush",
            ThermalLevel.Critical => "CriticalBrush",
            _ => "TextMutedBrush",
        } : "TextMutedBrush";

        return System.Windows.Application.Current.TryFindResource(resourceKey) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
