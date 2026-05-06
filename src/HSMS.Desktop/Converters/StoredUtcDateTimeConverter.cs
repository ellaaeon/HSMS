using System.Globalization;
using System.Windows.Data;
using HSMS.Shared.Time;

namespace HSMS.Desktop.Converters;

/// <summary>
/// DB stores UTC instants (often <see cref="DateTimeKind.Unspecified"/> from SQL). Formats in the HSMS deployment zone (Abu Dhabi, UTC+04).
/// </summary>
public sealed class StoredUtcDateTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "g";
        if (value is null)
        {
            return "";
        }

        if (value is DateTime dt)
        {
            return HsmsDeploymentTimeZone.FormatInDeploymentZone(dt, format, culture);
        }

        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
