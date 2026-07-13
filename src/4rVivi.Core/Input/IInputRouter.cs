namespace FourRVivi.Core.Input;

public interface IInputRouter
{
    InputMethod Method { get; set; }
    bool FallbackToNormalInput { get; set; }

    InputRouteResult Tap(string key, int holdMs = 0, string source = "");
    InputRouteResult Tap(IntPtr hwnd, int virtualKey, int holdMs = 0, string source = "");
    InputRouteResult ClickAt(int clientX, int clientY, string source = "");
    InputRouteResult ClickAt(IntPtr hwnd, int clientX, int clientY, string source = "");
}
