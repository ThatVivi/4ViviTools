using Avalonia.Media;

namespace FourRVivi.App.Services;

/// <summary>A distinct colour per OCR role, used for the calibration boxes and the marks list.</summary>
public static class RolePalette
{
    private static readonly Dictionary<string, Color> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HP"] = Color.Parse("#FF5C5C"),      ["MaxHP"] = Color.Parse("#FF9A3C"),
        ["SP"] = Color.Parse("#5C8CFF"),      ["MaxSP"] = Color.Parse("#3CC8FF"),
        ["BaseLevel"] = Color.Parse("#7CFF6C"), ["JobLevel"] = Color.Parse("#B06CFF"),
        ["Weight"] = Color.Parse("#FFE05C"),  ["MaxWeight"] = Color.Parse("#D4A03C"),
        ["Zeny"] = Color.Parse("#FFD700"),    ["BaseEXP"] = Color.Parse("#5CFFC8"),
        ["JobEXP"] = Color.Parse("#FF6CD4"),  ["CharName"] = Color.Parse("#FFFFFF"),
    };

    public static Color ColorFor(string role) => Map.TryGetValue(role ?? "", out var c) ? c : Colors.Aqua;
    public static IBrush Brush(string role) => new SolidColorBrush(ColorFor(role));
}
