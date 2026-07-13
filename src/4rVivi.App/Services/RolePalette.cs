using Avalonia.Media;

namespace FourRVivi.App.Services;

/// <summary>A distinct colour per OCR role, used for the calibration boxes and the marks list.</summary>
public static class RolePalette
{
    private static readonly Dictionary<string, Color> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HP"] = Color.Parse("#FF5C5C"),      ["MaxHP"] = Color.Parse("#FF9A3C"),
        ["HP / MaxHP"] = Color.Parse("#FF7070"), ["SP / MaxSP"] = Color.Parse("#6699FF"), ["Weight / MaxWeight"] = Color.Parse("#FFD24C"),
        ["SP"] = Color.Parse("#5C8CFF"),      ["MaxSP"] = Color.Parse("#3CC8FF"),
        ["HP % Text"] = Color.Parse("#FF2E2E"), ["SP % Text"] = Color.Parse("#2E6CFF"),
        ["HP Bar"] = Color.Parse("#FF2E2E"), ["SP Bar"] = Color.Parse("#2E6CFF"),
        ["HpPercent"] = Color.Parse("#FF2E2E"), ["SpPercent"] = Color.Parse("#2E6CFF"),
        ["BaseExpBar"] = Color.Parse("#9CFFB0"), ["JobExpBar"] = Color.Parse("#FFC04C"),
        ["BaseLevel"] = Color.Parse("#7CFF6C"), ["JobLevel"] = Color.Parse("#B06CFF"),
        ["Weight"] = Color.Parse("#FFE05C"),  ["MaxWeight"] = Color.Parse("#D4A03C"),
        ["Zeny"] = Color.Parse("#FFD700"),    ["BaseEXP"] = Color.Parse("#5CFFC8"),
        ["JobEXP"] = Color.Parse("#FF6CD4"),  ["CharName"] = Color.Parse("#FFD23F"),
        ["Loot"] = Color.Parse("#FF8C42"),
        ["PosX"] = Color.Parse("#4CD9C0"),    ["PosY"] = Color.Parse("#9CD94C"),
        ["ClassName"] = Color.Parse("#C0A0FF"), ["BasicInfo"] = Color.Parse("#8899AA"),
    };

    public static Color ColorFor(string role) => Map.TryGetValue(role ?? "", out var c) ? c : Colors.Aqua;
    public static IBrush Brush(string role) => new SolidColorBrush(ColorFor(role));
}
