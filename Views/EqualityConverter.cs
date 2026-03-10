using System.Globalization;
using System.Windows.Data;

namespace w_finder.Views;

/// <summary>
/// Returns true when two bound values are equal.
/// Used to highlight the focused pill in the quick action bar.
/// </summary>
public class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        return Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
