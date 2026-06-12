using System.Runtime.InteropServices;

namespace FourRVivi.Core.Input;

/// <summary>Sends key presses to a window via PostMessage (works on an unfocused window).</summary>
public sealed class KeySender
{
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Tap(IntPtr hWnd, int virtualKey, int holdMs = 0)
    {
        if (hWnd == IntPtr.Zero) return;
        PostMessage(hWnd, WM_KEYDOWN, virtualKey, 0);
        if (holdMs > 0) Thread.Sleep(holdMs);
        PostMessage(hWnd, WM_KEYUP, virtualKey, 0);
    }

    public void Down(IntPtr hWnd, int vk) { if (hWnd != IntPtr.Zero) PostMessage(hWnd, WM_KEYDOWN, vk, 0); }
    public void Up(IntPtr hWnd, int vk) { if (hWnd != IntPtr.Zero) PostMessage(hWnd, WM_KEYUP, vk, 0); }
}
