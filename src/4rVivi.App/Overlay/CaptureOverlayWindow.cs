using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FourRVivi.Core.Game;

namespace FourRVivi.App.Overlay;

/// <summary>Borderless, click-through, top-most banner shown over the game during auto-capture:
/// a big countdown plus the live candidate counts so the player knows what to do.</summary>
public sealed class CaptureOverlayWindow : Window
{
    private readonly GameSession _session;
    private readonly DispatcherTimer _timer;
    private readonly CaptureCanvas _canvas;

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;

    public CaptureOverlayWindow(GameSession session)
    {
        _session = session;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true; ShowInTaskbar = false; CanResize = false; Focusable = false;
        _canvas = new CaptureCanvas();
        Content = _canvas;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Track();
        Opened += (_, _) => { MakeClickThrough(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    public void SetStatus(int secondsLeft, string lines)
    {
        _canvas.Seconds = secondsLeft; _canvas.Lines = lines; _canvas.InvalidateVisual();
    }

    private void MakeClickThrough()
    {
        var h = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (h == IntPtr.Zero) return;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    }

    private void Track()
    {
        var hwnd = _session.WindowHandle;
        if (hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var r)) return;
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        Position = new PixelPoint(r.Left, r.Top);
        Width = (r.Right - r.Left) / scale;
        Height = (r.Bottom - r.Top) / scale;
        if (!IsVisible) Show();
        _canvas.InvalidateVisual();
    }
}

public sealed class CaptureCanvas : Control
{
    public int Seconds;
    public string Lines = "";

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, panelW = 520, panelH = 96;
        double x = (w - panelW) / 2, y = 16;
        var bg = new SolidColorBrush(Color.FromArgb(220, 18, 20, 28));
        var accent = new SolidColorBrush(Color.FromArgb(255, 130, 108, 247));
        ctx.DrawRectangle(bg, new Pen(accent, 2), new Rect(x, y, panelW, panelH), 10, 10);

        var big = new FormattedText($"Auto-capture: {Seconds}s  — move, fight, gain EXP",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, Brushes.White);
        ctx.DrawText(big, new Point(x + 16, y + 12));

        var sub = new FormattedText(Lines, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 13, new SolidColorBrush(Color.FromArgb(230, 200, 200, 210)));
        sub.MaxTextWidth = panelW - 32;
        ctx.DrawText(sub, new Point(x + 16, y + 46));
    }
}
