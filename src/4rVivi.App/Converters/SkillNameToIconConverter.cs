using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FourRVivi.App.Services;

namespace FourRVivi.App.Converters;

/// <summary>Resolves a skill DISPLAY name to its GRF skill sprite (by aegis), for skill pickers.</summary>
public sealed class SkillNameToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => IconImageService.Instance?.GetSkillByName(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
