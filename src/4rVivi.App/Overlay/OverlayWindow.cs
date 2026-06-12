using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FourRVivi.Core.Game;

namespace FourRVivi.App.Overlay;

/// <summary>Borderless, click-through, top-most window that draws helper geometry over the game.</summary>
public sealed class OverlayWindow : Window
{
    private readonly GameSession _session;
    private readonly DispatcherTimer _timer;
    private readonly OverlayCanvas _canvas;

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;

    public OverlayWindow(GameSession session)
    {
        _session = session;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Focusable = false;
        _canvas = new OverlayCanvas();
        Content = _canvas;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Track();
        Opened += (_, _) => { MakeClickThrough(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    public void Configure(int castRange, int aoe, bool gutter)
    {
        _canvas.CastRange = castRange; _canvas.Aoe = aoe; _canvas.Gutter = gutter;
        _canvas.InvalidateVisual();
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
        if (hwnd == IntPtr.Zero) { Hide(); return; }
        if (!GetWindowRect(hwnd, out var r)) return;
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        Position = new PixelPoint(r.Left, r.Top);
        Width = (r.Right - r.Left) / scale;
        Height = (r.Bottom - r.Top) / scale;
        if (!IsVisible) Show();
        _canvas.InvalidateVisual();
    }
}

/// <summary>Draws the cast-range circle, AoE box, and center cross, all centered.</summary>
public sealed class OverlayCanvas : Control
{
    public int CastRange, Aoe;
    public bool Gutter;

    public override void Render(DrawingContext ctx)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 130, 108, 247)), 2);
        double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
        if (CastRange > 0) ctx.DrawEllipse(null, pen, new Point(cx, cy), CastRange, CastRange);
        if (Aoe > 0) ctx.DrawRectangle(null, pen, new Rect(cx - Aoe, cy - Aoe, Aoe * 2, Aoe * 2));
        if (Gutter)
        {
            ctx.DrawLine(pen, new Point(cx, 0), new Point(cx, Bounds.Height));
            ctx.DrawLine(pen, new Point(0, cy), new Point(Bounds.Width, cy));
        }
    }
}
