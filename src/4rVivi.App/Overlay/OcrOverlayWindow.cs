using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FourRVivi.Core.Game;
using FourRVivi.Core.Ocr;

namespace FourRVivi.App.Overlay;

/// <summary>Click-through border drawn over the attached client while OCR runs: a colored frame (so the
/// user sees it's active + attached), the marked read-regions, and a status label.</summary>
public sealed class OcrOverlayWindow : Window
{
    private readonly GameSession _session;
    private readonly DispatcherTimer _timer;
    private readonly OcrStatusCanvas _canvas;

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;

    public OcrOverlayWindow(GameSession session)
    {
        _session = session;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true; ShowInTaskbar = false; CanResize = false; Focusable = false;
        _canvas = new OcrStatusCanvas();
        Content = _canvas;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Track();
        Opened += (_, _) => { MakeClickThrough(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    public void SetInfo(System.Collections.Generic.IReadOnlyList<OcrMark> marks, string label)
    {
        _canvas.Marks = marks; _canvas.Label = label; _canvas.InvalidateVisual();
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

public sealed class OcrStatusCanvas : Control
{
    public System.Collections.Generic.IReadOnlyList<OcrMark> Marks = System.Array.Empty<OcrMark>();
    public string Label = "";

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var accent = new SolidColorBrush(Color.FromArgb(255, 90, 220, 120));
        var pen = new Pen(accent, 3);
        ctx.DrawRectangle(null, pen, new Rect(1.5, 1.5, w - 3, h - 3));

        var boxPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 90, 220, 120)), 2);
        foreach (var m in Marks)
            ctx.DrawRectangle(null, boxPen, new Rect(m.X * w, m.Y * h, m.W * w, m.H * h));

        var bg = new SolidColorBrush(Color.FromArgb(210, 18, 20, 28));
        ctx.DrawRectangle(bg, null, new Rect(8, 8, 300, 26), 6, 6);
        var ft = new FormattedText("● OCR active — " + Label, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, accent);
        ctx.DrawText(ft, new Point(16, 11));
    }
}
