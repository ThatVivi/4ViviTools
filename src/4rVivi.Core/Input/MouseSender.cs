using System.Runtime.InteropServices;

namespace FourRVivi.Core.Input;

/// <summary>Sends mouse clicks to a window via PostMessage (works unfocused). Used to walk/seek.</summary>
public sealed class MouseSender
{
    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public (int w, int h) ClientSize(IntPtr hwnd)
        => GetClientRect(hwnd, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : (0, 0);

    public void Click(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr l = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, l);
        PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, l);
    }
}
