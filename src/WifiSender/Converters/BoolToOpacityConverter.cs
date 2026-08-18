using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WifiSender.Converters;

public static class BoolToOpacity
{
    public static readonly IValueConverter Instance = new BoolToOpacityConverter();
}

public class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 1.0 : 0.35;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
