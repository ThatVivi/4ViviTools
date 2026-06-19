using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FourRVivi.App.Services;

namespace FourRVivi.App.Converters;

public sealed class RoleToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => RolePalette.Brush(value as string ?? "");
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
