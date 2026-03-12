using System.Globalization;
using System.Windows.Data;

namespace w_finder.Views;

/// <summary>
/// Returns true if the bound integer value is greater than 0.
/// Used to show/hide type count and disclosure chevron for family summary rows.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intVal) return intVal > 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
