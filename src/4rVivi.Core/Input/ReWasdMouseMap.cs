using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace FourRVivi.Core.Input;

public static class ReWasdMouseMap
{
    private static readonly Dictionary<string, Xbox360Button> Buttons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = Xbox360Button.A,
        ["B"] = Xbox360Button.B,
        ["X"] = Xbox360Button.X,
        ["Y"] = Xbox360Button.Y,
        ["LeftShoulder"] = Xbox360Button.LeftShoulder,
        ["RightShoulder"] = Xbox360Button.RightShoulder,
        ["Back"] = Xbox360Button.Back,
        ["Start"] = Xbox360Button.Start,
        ["LeftThumb"] = Xbox360Button.LeftThumb,
        ["RightThumb"] = Xbox360Button.RightThumb,
        ["DpadUp"] = Xbox360Button.Up,
        ["DpadDown"] = Xbox360Button.Down,
        ["DpadLeft"] = Xbox360Button.Left,
        ["DpadRight"] = Xbox360Button.Right,
    };

    public static IReadOnlyList<string> ButtonNames { get; } = Buttons.Keys.ToArray();

    public static Xbox360Button DefaultLeftClickButton => Xbox360Button.A;

    public static Xbox360Button DefaultRightClickButton => Xbox360Button.B;

    public static Xbox360Button FromName(string? name)
        => !string.IsNullOrWhiteSpace(name) && Buttons.TryGetValue(name.Trim(), out var button)
            ? button
            : DefaultLeftClickButton;

    public static bool IsButtonName(string? name)
        => !string.IsNullOrWhiteSpace(name) && Buttons.ContainsKey(name.Trim());

    public static bool IsButtonChord(string? name)
        => NormalizeChord(name).Length > 0;

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "A";
        var trimmed = name.Trim();
        return Buttons.Keys.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)) ?? "A";
    }

    public static string NormalizeChord(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var parts = name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Buttons.Keys.FirstOrDefault(k => string.Equals(k, p, StringComparison.OrdinalIgnoreCase)))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? "" : string.Join("+", parts);
    }

    public static IReadOnlyList<Xbox360Button> FromChord(string? name)
    {
        var chord = NormalizeChord(name);
        if (chord.Length == 0) return Array.Empty<Xbox360Button>();
        return chord.Split('+')
            .Where(Buttons.ContainsKey)
            .Select(p => Buttons[p])
            .Distinct()
            .ToArray();
    }

    public static IReadOnlyList<string> ButtonChordNames(bool includeTwoButtonCombos)
    {
        var singles = ButtonNames.ToList();
        if (!includeTwoButtonCombos) return singles;
        var buttons = ButtonNames.ToArray();
        for (int i = 0; i < buttons.Length; i++)
            for (int j = i + 1; j < buttons.Length; j++)
                singles.Add(buttons[i] + "+" + buttons[j]);
        return singles;
    }

    public static IEnumerable<Xbox360Button> AllButtons() => Buttons.Values;
}
