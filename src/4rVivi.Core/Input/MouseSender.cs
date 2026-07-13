using System.Runtime.InteropServices;
using FourRVivi.Core.Common;
using FourRVivi.Core.Game;

namespace FourRVivi.Core.Input;

/// <summary>Left-clicks the game at a CLIENT coordinate via a selectable standard backend (see
/// <see cref="InputMethod"/>): SendInput, legacy mouse_event, or PostMessage. SendInput/mouse_event move
/// the real cursor (window must be focused); PostMessage posts a window message (works unfocused but many
/// DirectInput clients ignore it). All are normal Windows APIs.</summary>
public sealed class MouseSender
{
    public InputMethod Method { get; set; } = InputMethod.SendInput;
    public VirtualHidInput? VirtualHid { get; set; }
    public ViiperInput? Viiper { get; set; }
    public FocusGate? FocusGate { get; set; }
    public string VirtualLeftClickButton
    {
        get => _reWasd.LeftClickButtonName;
        set => _reWasd.LeftClickButtonName = value;
    }
    public int VirtualClickHoldMs
    {
        get => _reWasd.TapDurationMs;
        set => _reWasd.TapDurationMs = Math.Clamp(value, 30, 500);
    }
    public bool FallbackToNormalClickWhenReWasdRunning { get; set; }
    public bool FallbackToNormalClickWhenVirtualHidFails { get; set; }

    private readonly ReWasdController _reWasd = new();
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

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public MOUSEINPUT mi; }

    public (int w, int h) ClientSize(IntPtr hwnd)
        => GetClientRect(hwnd, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : (0, 0);

    private bool CanAct(string action)
    {
        if (FocusGate == null || FocusGate.CanAct(out _))
            return true;
        DebugTrace.Write("Input", $"Mouse action blocked by FocusGate action='{action}'.");
        InputRuntimeStatus.SetLastMouse("Paused: focus RO client");
        return false;
    }

    public bool IsVirtualDriverReady() => _reWasd.IsVirtualDriverReady();

    public bool IsVirtualHidReady() => VirtualHid?.IsReady == true;
    public bool IsViiperInstalled() => Viiper?.IsInstalled == true;
    public bool IsViiperReady() => Viiper?.IsReady == true;

    public bool IsVirtualDriverInstalled() => _reWasd.IsVirtualDriverInstalled();

    public bool IsVirtualHidInstalled() => VirtualHid?.IsFakerInputInstalled() == true || VirtualHid?.IsVmouseInstalled() == true;

    public bool EnableVirtualHid() => VirtualHid?.EnsureConnected() == true;
    public bool EnableViiper() => Viiper?.EnsureConnected() == true;

    public bool IsReWasdRunning() => _reWasd.IsReWasdRunning();

    public bool EnableVirtualController() => _reWasd.EnsureConnected();

    public void ShutdownVirtualController() => _reWasd.Dispose();

    public bool TapVirtualLeftClick(int holdMs = 0)
    {
        if (!CanAct("ViGEm left click")) return false;
        _reWasd.LeftClick(holdMs > 0 ? holdMs : VirtualClickHoldMs);
        InputRuntimeStatus.SetLastMouse($"ViGEm click {VirtualLeftClickButton}");
        return _reWasd.IsVirtualDriverReady();
    }

    public bool TapVirtualButton(string buttonName, int holdMs = 0)
    {
        if (!CanAct($"ViGEm button {buttonName}")) return false;
        var buttons = ReWasdMouseMap.FromChord(buttonName);
        if (buttons.Count == 0)
        {
            DebugTrace.Write("Input", $"Ignored invalid virtual button/chord '{buttonName}'.");
            return false;
        }

        int duration = Math.Max(30, holdMs > 0 ? holdMs : VirtualClickHoldMs);
        DebugTrace.Write("Input", $"Tap virtual button/chord '{buttonName}' holdMs={duration} configuredHold={VirtualClickHoldMs}.");
        InputRuntimeStatus.SetLastKeyboard($"ViGEm button {ReWasdMouseMap.NormalizeChord(buttonName)}");
        if (buttons.Count == 1)
        {
            _reWasd.Tap(buttons[0], duration);
            return _reWasd.IsVirtualDriverReady();
        }

        try
        {
            foreach (var button in buttons)
                _reWasd.SetButton(button, true);
            Thread.Sleep(duration);
        }
        finally
        {
            foreach (var button in buttons)
                _reWasd.SetButton(button, false);
        }
        return _reWasd.IsVirtualDriverReady();
    }

    /// <summary>Left-click at a client coordinate using the selected method.</summary>
    public void Click(IntPtr hwnd, int x, int y)
    {
        DebugTrace.Write("Input", $"Click requested route={MouseRouteName(Method)} method={Method} hwnd=0x{hwnd.ToInt64():X} client={x},{y} virtualButton={VirtualLeftClickButton} normalFallback={FallbackToNormalClickWhenVirtualHidFails || FallbackToNormalClickWhenReWasdRunning}.");
        if (hwnd == IntPtr.Zero)
        {
            DebugTrace.Write("Input", "Click ignored because hwnd is zero.");
            return;
        }
        if (!CanAct("client click")) return;
        var size = ClientSize(hwnd);
        if (size.w <= 2 || size.h <= 2)
        {
            DebugTrace.Write("Input", $"Click aborted because client rect is invalid size={size.w}x{size.h}.");
            InputRuntimeStatus.SetLastMouse("Click aborted: invalid client");
            return;
        }
        x = Math.Clamp(x, 0, size.w - 1);
        y = Math.Clamp(y, 0, size.h - 1);
        if (Method == InputMethod.PostMessage)
        {
            IntPtr l = (IntPtr)((y << 16) | (x & 0xFFFF));
            var downOk = PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, l);
            Thread.Sleep(40 + _rng.Next(0, 30));
            var upOk = PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, l);
            DebugTrace.Write("Input", $"PostMessage click sent down={downOk} up={upOk} lParam=0x{l.ToInt64():X}.");
            InputRuntimeStatus.SetLastMouse("PostMessage click");
            return;
        }
        // SendInput / mouse_event both drive the real cursor -> move there first.
        var p = new POINT { X = x, Y = y };
        if (!ClientToScreen(hwnd, ref p))
        {
            DebugTrace.Write("Input", "ClientToScreen failed; click aborted.");
            return;
        }
        DebugTrace.Write("Input", $"ClientToScreen -> screen={p.X},{p.Y}.");

        if (Method == InputMethod.Viiper)
        {
            if (TryViiperClick(p.X, p.Y))
                return;
            if (TryVirtualHidClick(p.X, p.Y, "after VIIPER failure"))
                return;
            if (TryViGemClick(p.X, p.Y, moveCursorFirst: true, "after VIIPER/FakerInput failure"))
                return;
            if (!FallbackToNormalClickWhenVirtualHidFails)
            {
                DebugTrace.Write("Input", "VIIPER route stopped before normal click; normal fallback disabled.");
                return;
            }
            SendNormalCursorClick(hwnd, p.X, p.Y);
            return;
        }

        if (Method == InputMethod.VirtualHid)
        {
            if (TryVirtualHidClick(p.X, p.Y, "primary"))
                return;
            if (TryViGemClick(p.X, p.Y, moveCursorFirst: true, "after FakerInput/vmouse failure"))
                return;
            if (!FallbackToNormalClickWhenVirtualHidFails)
            {
                DebugTrace.Write("Input", "Virtual HID route stopped before normal click; normal fallback disabled.");
                return;
            }
            SendNormalCursorClick(hwnd, p.X, p.Y);
            return;
        }

        if (Method == InputMethod.ReWasdClick)
        {
            bool reWasdRunning = _reWasd.IsReWasdRunning();
            DebugTrace.Write("Input", $"ReWasdClick path reWasdRunning={reWasdRunning}.");
            if (TryViGemClick(p.X, p.Y, moveCursorFirst: true, "primary ReWasdClick/ViGEm"))
            {
                if (!FallbackToNormalClickWhenReWasdRunning)
                    return;
                DebugTrace.Write("Input", "ReWasdClick continuing to normal click fallback because fallback is enabled.");
            }
            else if (!FallbackToNormalClickWhenReWasdRunning)
            {
                DebugTrace.Write("Input", "ReWasdClick route stopped before normal click; ViGEm failed and normal fallback disabled.");
                return;
            }
        }

        SendNormalCursorClick(hwnd, p.X, p.Y);
    }

    private bool TryViiperClick(int screenX, int screenY)
    {
        int hold = VirtualClickHoldMs + _rng.Next(0, 25);
        if (Viiper?.ClickAtScreen(screenX, screenY, hold, out var moveMs) == true)
        {
            DebugTrace.Write("Input", $"Route step OK: VIIPER mouse screen={screenX},{screenY} holdMs={hold} moveMs={moveMs}.");
            InputRuntimeStatus.SetLastMouse($"VIIPER click {moveMs} ms move");
            return true;
        }

        DebugTrace.Write("Input", $"Route step failed: VIIPER mouse screen={screenX},{screenY} installed={Viiper?.IsInstalled == true} ready={Viiper?.IsReady == true}.");
        return false;
    }

    private bool TryVirtualHidClick(int screenX, int screenY, string reason)
    {
        int hold = VirtualClickHoldMs + _rng.Next(0, 25);
        if (VirtualHid?.ClickAtScreen(screenX, screenY, hold) == true)
        {
            DebugTrace.Write("Input", $"Route step OK: FakerInput/vmouse mouse reason='{reason}' screen={screenX},{screenY} holdMs={hold}.");
            InputRuntimeStatus.SetLastMouse("FakerInput/vmouse click");
            return true;
        }

        DebugTrace.Write("Input", $"Route step failed: FakerInput/vmouse mouse reason='{reason}' installed={IsVirtualHidInstalled()} ready={VirtualHid?.IsReady == true}.");
        return false;
    }

    private bool TryViGemClick(int screenX, int screenY, bool moveCursorFirst, string reason)
    {
        if (moveCursorFirst)
        {
            HumanMoveTo(screenX, screenY);
            Thread.Sleep(CalculatePostMoveSettleMs(screenX, screenY));
        }

        if (!_reWasd.EnsureConnected())
        {
            DebugTrace.Write("Input", $"Route step failed: ViGEm reason='{reason}' driverReady={_reWasd.IsVirtualDriverReady()} installed={_reWasd.IsVirtualDriverInstalled()}.");
            return false;
        }

        int hold = VirtualClickHoldMs + _rng.Next(0, 25);
        _reWasd.LeftClick(hold);
        DebugTrace.Write("Input", $"Route step OK: ViGEm reason='{reason}' button={VirtualLeftClickButton} holdMs={hold}.");
        InputRuntimeStatus.SetLastMouse($"ViGEm click {VirtualLeftClickButton}");
        return true;
    }

    private void SendNormalCursorClick(IntPtr hwnd, int screenX, int screenY)
    {
        HumanMoveTo(screenX, screenY);
        Thread.Sleep(CalculatePostMoveSettleMs(screenX, screenY));

        if (Method == InputMethod.MouseKeyEvent)
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            Thread.Sleep(45 + _rng.Next(0, 40));
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            DebugTrace.Write("Input", "mouse_event left click sent.");
            InputRuntimeStatus.SetLastMouse("mouse_event click");
        }
        else // SendInput
        {
            var down = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
            var up   = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
            var downSent = SendInput(1, down, Marshal.SizeOf<INPUT>());
            Thread.Sleep(45 + _rng.Next(0, 40));
            var upSent = SendInput(1, up, Marshal.SizeOf<INPUT>());
            DebugTrace.Write("Input", $"SendInput left click sent down={downSent} up={upSent}.");
            InputRuntimeStatus.SetLastMouse("SendInput click");
        }
    }

    private static string MouseRouteName(InputMethod method)
        => method switch
        {
            InputMethod.Viiper => "VIIPER -> FakerInput/vmouse -> ViGEm -> SendInput",
            InputMethod.VirtualHid => "FakerInput/vmouse -> ViGEm -> SendInput",
            InputMethod.ReWasdClick => "ViGEm -> SendInput",
            InputMethod.PostMessage => "PostMessage",
            InputMethod.MouseKeyEvent => "mouse_event",
            _ => "SendInput"
        };

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

    private static int CalculatePostMoveSettleMs(int targetX, int targetY)
    {
        if (!GetCursorPos(out var p))
            return 35;
        var distance = System.Math.Sqrt(System.Math.Pow(targetX - p.X, 2) + System.Math.Pow(targetY - p.Y, 2));
        return System.Math.Clamp((int)System.Math.Ceiling(distance / 4.0) + 12 + _rng.Next(0, 10), 12, 180);
    }
}
