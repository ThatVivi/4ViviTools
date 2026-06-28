using System.Runtime.InteropServices;

namespace FourRVivi.Core.Input;

/// <summary>Left-clicks the game at a CLIENT coordinate via a selectable standard backend (see
/// <see cref="InputMethod"/>): SendInput, legacy mouse_event, or PostMessage. SendInput/mouse_event move
/// the real cursor (window must be focused); PostMessage posts a window message (works unfocused but many
/// DirectInput clients ignore it). All are normal Windows APIs.</summary>
public sealed class MouseSender
{
    public InputMethod Method { get; set; } = InputMethod.SendInput;

    private static readonly System.Random _rng = new();
    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint INPUT_MOUSE = 0;

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inp, int cb);
    [DllImport("user32.dll")] private static extern IntPtr SetForegroundWindow(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public MOUSEINPUT mi; }

    public (int w, int h) ClientSize(IntPtr hwnd)
        => GetClientRect(hwnd, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : (0, 0);

    /// <summary>Left-click at a client coordinate using the selected method.</summary>
    public void Click(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return;
        if (Method == InputMethod.PostMessage)
        {
            IntPtr l = (IntPtr)((y << 16) | (x & 0xFFFF));
            PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, l);
            Thread.Sleep(40 + _rng.Next(0, 30));
            PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, l);
            return;
        }
        // SendInput / mouse_event both drive the real cursor -> move there first.
        var p = new POINT { X = x, Y = y };
        if (!ClientToScreen(hwnd, ref p)) return;
        SetForegroundWindow(hwnd);
        Thread.Sleep(20);
        HumanMoveTo(p.X, p.Y);
        Thread.Sleep(15 + _rng.Next(0, 20));
        if (Method == InputMethod.MouseKeyEvent)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            Thread.Sleep(45 + _rng.Next(0, 40));
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        }
        else // SendInput
        {
            var down = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
            var up   = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
            SendInput(1, down, Marshal.SizeOf<INPUT>());
            Thread.Sleep(45 + _rng.Next(0, 40));
            SendInput(1, up, Marshal.SizeOf<INPUT>());
        }
    }

    /// <summary>Kept for the bot's HardwareClick toggle — identical to Click for SendInput/mouse_event modes.</summary>
    public void HardwareClick(IntPtr hwnd, int clientX, int clientY)
    {
        var prev = Method;
        if (Method == InputMethod.PostMessage) Method = InputMethod.SendInput;   // hardware click can't be PostMessage
        try { Click(hwnd, clientX, clientY); } finally { Method = prev; }
    }

    private static void HumanMoveTo(int tx, int ty)
    {
        GetCursorPos(out var cur);
        double sx = cur.X, sy = cur.Y;
        int steps = 16 + _rng.Next(0, 12);
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            double e = t * t * (3 - 2 * t);
            int jx = (i < steps) ? _rng.Next(-2, 3) : 0;
            int jy = (i < steps) ? _rng.Next(-2, 3) : 0;
            SetCursorPos((int)System.Math.Round(sx + (tx - sx) * e) + jx, (int)System.Math.Round(sy + (ty - sy) * e) + jy);
            Thread.Sleep(3 + _rng.Next(0, 5));
        }
        SetCursorPos(tx, ty);
    }
}
