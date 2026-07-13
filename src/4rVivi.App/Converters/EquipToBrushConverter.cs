using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FourRVivi.Core.Data;

namespace FourRVivi.App.Converters;

/// <summary>Colour-codes a piece of equipment by its type/slot so every item shows its own colour.</summary>
public sealed class EquipToBrushConverter : IValueConverter
{
    private static readonly IBrush Weapon   = new SolidColorBrush(Color.Parse("#FF7A7A")); // red
    private static readonly IBrush Armor     = new SolidColorBrush(Color.Parse("#7CC4FF")); // blue
    private static readonly IBrush Headgear  = new SolidColorBrush(Color.Parse("#F5C24B")); // gold
    private static readonly IBrush Garment   = new SolidColorBrush(Color.Parse("#A78BFA")); // violet
    private static readonly IBrush Shoes      = new SolidColorBrush(Color.Parse("#5EC26A")); // green
    private static readonly IBrush Accessory = new SolidColorBrush(Color.Parse("#FF9F45")); // orange
    private static readonly IBrush Card       = new SolidColorBrush(Color.Parse("#FF8AD8")); // pink
    private static readonly IBrush Costume    = new SolidColorBrush(Color.Parse("#67E8C3")); // teal
    private static readonly IBrush Default    = new SolidColorBrush(Color.Parse("#ECEEF5"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            EquipInfo e => (e.Type + " " + e.SubType + " " + string.Join(" ", e.Loc)),
            string s => s,
            _ => ""
        };
        key = key.ToLowerInvariant();

        if (key.Contains("costume")) return Costume;
        if (key.Contains("card")) return Card;
        if (key.Contains("weapon") || key.Contains("hand")) return Weapon;
        if (key.Contains("head") || key.Contains("helm")) return Headgear;
        if (key.Contains("garment") || key.Contains("robe")) return Garment;
        if (key.Contains("shoe") || key.Contains("foot")) return Shoes;
        if (key.Contains("accessory") || key.Contains("acc")) return Accessory;
        if (key.Contains("armor") || key.Contains("body")) return Armor;
        return Default;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
