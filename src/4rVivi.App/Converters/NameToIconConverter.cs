using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using FourRVivi.App.Services;

namespace FourRVivi.App.Converters;

/// <summary>Resolves an item NAME (as typed/picked in a dropdown) to its sprite via IconImageService.</summary>
public sealed class NameToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        if (string.IsNullOrWhiteSpace(name)) return null;
        // strip leading "(no ...)" placeholders
        if (name.StartsWith("(no ", StringComparison.OrdinalIgnoreCase)) return null;
        try { return IconImageService.Instance?.GetItemByName(name.Trim()); } catch { return null; }
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
