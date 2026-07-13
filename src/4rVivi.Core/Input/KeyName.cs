namespace FourRVivi.Core.Input;

/// <summary>Maps friendly key names (F1, A, 1, ...) to Win32 virtual-key codes.</summary>
public static class KeyName
{
    public static int ToVk(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        name = name.Trim().ToUpperInvariant();
        if (name.Length == 1)
        {
            char c = name[0];
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }
        if (name.StartsWith('F') && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24)
            return 0x70 + (f - 1); // VK_F1 = 0x70
        return name switch
        {
            "SPACE" => 0x20, "ENTER" => 0x0D, "TAB" => 0x09, "ESC" or "ESCAPE" => 0x1B,
            "INSERT" or "INS" => 0x2D, "DELETE" or "DEL" => 0x2E, "HOME" => 0x24, "END" => 0x23,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
            _ => 0
        };
    }

    public static string FromVk(int vk)
    {
        if (vk is >= 'A' and <= 'Z') return ((char)vk).ToString();
        if (vk is >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x7B) return $"F{vk - 0x70 + 1}";
        return vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Escape",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => ""
        };
    }

    public static IReadOnlyList<string> Common { get; } = new[]
    {
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "1","2","3","4","5","6","7","8","9","0","Insert","Delete","Home","End","PageUp","PageDown"
    };
}
