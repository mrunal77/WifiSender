using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WifiSender.Converters;

public static class StringConverters
{
    public static readonly IValueConverter IsNotNullOrEmpty = new IsNotNullOrEmptyConverter();
}

public class IsNotNullOrEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
