namespace FourRVivi.Core.Input;

/// <summary>Small shared status surface for overlays and debug UI. Automation updates it when input paths change or fire.</summary>
public static class InputRuntimeStatus
{
    private static readonly object Lock = new();
    private static string _mouseConfigured = "Mouse: not configured";
    private static string _keyboardConfigured = "Keyboard: not configured";
    private static string _lastMouse = "";
    private static string _lastKeyboard = "";

    public static void SetConfigured(string mouse, string keyboard)
    {
        lock (Lock)
        {
            _mouseConfigured = string.IsNullOrWhiteSpace(mouse) ? "Mouse: not configured" : mouse;
            _keyboardConfigured = string.IsNullOrWhiteSpace(keyboard) ? "Keyboard: not configured" : keyboard;
        }
    }

    public static void SetLastMouse(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Lock) _lastMouse = path;
    }

    public static void SetLastKeyboard(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Lock) _lastKeyboard = path;
    }

    public static (string Mouse, string Keyboard) Snapshot()
    {
        lock (Lock)
        {
            var mouse = string.IsNullOrWhiteSpace(_lastMouse)
                ? _mouseConfigured
                : $"{_mouseConfigured} | last: {_lastMouse}";
            var keyboard = string.IsNullOrWhiteSpace(_lastKeyboard)
                ? _keyboardConfigured
                : $"{_keyboardConfigured} | last: {_lastKeyboard}";
            return (mouse, keyboard);
        }
    }
}
