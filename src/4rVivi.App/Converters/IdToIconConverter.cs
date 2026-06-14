using System.Globalization;
using Avalonia.Data.Converters;
using FourRVivi.App.Services;

namespace FourRVivi.App.Converters;

/// <summary>Binds an item id (int or string) to its icon bitmap via IconImageService.</summary>
public sealed class IdToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int id = value switch { int i => i, string s when int.TryParse(s, out var v) => v, _ => 0 };
        return id > 0 ? IconImageService.Instance?.Get(id) : null;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
