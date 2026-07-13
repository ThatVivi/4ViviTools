using System.Runtime.InteropServices;
using FourRVivi.Core.Common;

namespace FourRVivi.Core.Game;

public sealed record FocusGateSnapshot(
    bool CanRead,
    bool CanAct,
    string Reason,
    int SelectedPid,
    int ForegroundPid,
    IntPtr WindowHandle,
    bool RectValid);

public sealed class FocusGate
{
    private readonly GameSession _session;
    private long _lastLogTick;

    public FocusGate(GameSession session) => _session = session;

    public FocusGateSnapshot Snapshot()
    {
        var proc = _session.Process;
        int selectedPid = proc?.Pid ?? 0;
        var hwnd = _session.RefreshWindowHandle();
        bool attached = _session.Reader.Attached && selectedPid > 0;
        bool hwndValid = hwnd != IntPtr.Zero && IsWindow(hwnd);
        bool minimized = hwndValid && IsIconic(hwnd);
        bool rectValid = hwndValid && TryClientSize(hwnd, out var w, out var h) && w > 2 && h > 2;
        bool canRead = attached && hwndValid && !minimized && rectValid;

        var foreground = GetForegroundWindow();
        int foregroundPid = ProcessIdForWindow(foreground);
        bool canAct = canRead && foregroundPid == selectedPid;

        string reason =
            !attached ? "not-attached" :
            !hwndValid ? "window-invalid" :
            minimized ? "minimized" :
            !rectValid ? "client-rect-invalid" :
            !canAct ? "not-foreground" :
            "ok";

        return new FocusGateSnapshot(canRead, canAct, reason, selectedPid, foregroundPid, hwnd, rectValid);
    }

    public bool CanRead(out FocusGateSnapshot snapshot)
    {
        snapshot = Snapshot();
        Log(snapshot);
        return snapshot.CanRead;
    }

    public bool CanAct(out FocusGateSnapshot snapshot)
    {
        snapshot = Snapshot();
        Log(snapshot);
        return snapshot.CanAct;
    }

    public static bool EvaluateCanAct(int selectedPid, int foregroundPid, bool canRead)
        => canRead && selectedPid > 0 && selectedPid == foregroundPid;

    private void Log(FocusGateSnapshot s)
    {
        long now = Environment.TickCount64;
        if (now - _lastLogTick < 1000)
            return;
        _lastLogTick = now;
        DebugTrace.Write("FocusGate",
            $"read={s.CanRead} act={s.CanAct} reason={s.Reason} fgPid={s.ForegroundPid} selPid={s.SelectedPid} hwnd=0x{s.WindowHandle.ToInt64():X} rectValid={s.RectValid}");
    }

    private static int ProcessIdForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return unchecked((int)pid);
    }

    private static bool TryClientSize(IntPtr hwnd, out int width, out int height)
    {
        width = height = 0;
        if (!GetClientRect(hwnd, out var r)) return false;
        width = r.Right - r.Left;
        height = r.Bottom - r.Top;
        return true;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
