using System.Globalization;
using System.Windows.Data;

namespace FileExplorer.Converters;

public sealed class DateTimeFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
            return Services.SettingsManager.Instance.FormatDateTime(dateTime);

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
