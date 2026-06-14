using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WifiSender.Converters;

public static class ObjectConverters
{
    public static readonly IValueConverter IsNotNull = new IsNotNullConverter();
}

public class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
