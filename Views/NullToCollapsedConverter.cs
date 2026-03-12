using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace w_finder.Views;

/// <summary>
/// Returns Visible when the bound value is non-null and non-empty, Collapsed otherwise.
/// Used to hide UI elements when their bound string property is null.
/// </summary>
public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
