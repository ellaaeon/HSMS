using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HSMS.Desktop.Mvvm;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool bb && bb;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Visibility v) return Binding.DoNothing;
        var b = v == Visibility.Visible;
        return Invert ? !b : b;
    }
}

