using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Ocr;

namespace FourRVivi.App.Overlay;

/// <summary>One auto-detection box drawn on the overlay. Coords are in CAPTURE PIXELS (the same frame
/// the OCR ran on); the canvas scales them to the overlay.</summary>
public readonly record struct DetBox(string Kind, int X, int Y, int W, int H, string Label, float Score);
public readonly record struct WalkBox(int X, int Y, int W, int H);

/// <summary>Click-through frame drawn over the attached client (or a whole monitor) while OCR runs:
/// a colored border, the user's marked read-regions, the live auto-detection boxes, and a status label.</summary>
public sealed class OcrOverlayWindow : Window
{
    private readonly GameSession _session;
    private readonly DispatcherTimer _timer;
    private readonly OcrStatusCanvas _canvas;
    private PixelRect? _monitorRect;   // when set, the overlay covers this monitor instead of the window

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
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
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Track();
        Opened += (_, _) => { MakeClickThrough(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Cover this monitor rect (screen pixels) instead of tracking the game window.</summary>
    public void SetMonitor(int x, int y, int w, int h) => _monitorRect = new PixelRect(x, y, w, h);
    public void ClearMonitor() => _monitorRect = null;

    public void SetInfo(System.Collections.Generic.IReadOnlyList<OcrMark> marks, string label, int topOffset = 0, int sideOffset = 0)
    {
        _canvas.Marks = marks; _canvas.Label = label; _canvas.TopOffset = topOffset; _canvas.SideOffset = sideOffset; _canvas.InvalidateVisual();
    }

    /// <summary>Push the live auto-detection boxes (capture-pixel coords) + the capture frame size.</summary>
    public void SetDetections(System.Collections.Generic.IReadOnlyList<DetBox> dets, int capW, int capH)
    {
        _canvas.SetDetections(dets, capW, capH);
    }

    public void ClearEntityTracks(string reason)
    {
        _canvas.ClearEntityTracks(reason);
    }

    /// <summary>Show the detected priority values in a small adjustable panel on the client.</summary>
    public void SetValues(System.Collections.Generic.IReadOnlyList<string> lines, double scale)
    {
        _canvas.Values = lines; _canvas.ValueScale = scale; _canvas.InvalidateVisual();
    }

    public void SetWalkBox(WalkBox? box)
    {
        _canvas.WalkBox = box; _canvas.InvalidateVisual();
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
        double scale = RenderScaling <= 0 ? 1 : RenderScaling;

        // Monitor mode: cover the whole selected monitor; boxes are in monitor-pixel coords.
        if (_monitorRect is { } mr)
        {
            Position = new PixelPoint(mr.X, mr.Y);
            Width = mr.Width / scale;
            Height = mr.Height / scale;
            _canvas.FrameMode = false;   // no client-frame border over the desktop
            if (!IsVisible) Show();
            _canvas.InvalidateVisual();
            return;
        }

        var hwnd = _session.WindowHandle;
        if (hwnd == IntPtr.Zero) { Hide(); return; }
        // Attach to the CLIENT area only (matches OcrService.CaptureWindow, which captures client pixels).
        if (!GetClientRect(hwnd, out var cr)) return;
        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin)) return;
        Position = new PixelPoint(origin.X, origin.Y);
        Width = (cr.Right - cr.Left) / scale;
        Height = (cr.Bottom - cr.Top) / scale;
        _canvas.FrameMode = true;
        if (!IsVisible) Show();
        _canvas.InvalidateVisual();
    }
}

public sealed class OcrStatusCanvas : Control
{
    public System.Collections.Generic.IReadOnlyList<OcrMark> Marks = System.Array.Empty<OcrMark>();
    public System.Collections.Generic.IReadOnlyList<DetBox> Dets = System.Array.Empty<DetBox>();
    public int CapW, CapH;
    public string Label = "";
    public int TopOffset, SideOffset;
    public bool FrameMode = true;   // draw the green client frame (window mode) vs none (monitor mode)
    public System.Collections.Generic.IReadOnlyList<string> Values = System.Array.Empty<string>();
    public double ValueScale = 1.0;
    public WalkBox? WalkBox;
    private System.Collections.Generic.IReadOnlyList<DetBox> _textDets = System.Array.Empty<DetBox>();

    public void SetDetections(System.Collections.Generic.IReadOnlyList<DetBox> dets, int capW, int capH)
    {
        CapW = capW;
        CapH = capH;
        var text = new System.Collections.Generic.List<DetBox>();
        var entities = new System.Collections.Generic.List<DetBox>();
        foreach (var d in dets ?? System.Array.Empty<DetBox>())
        {
            if (d.Kind == "Entity") entities.Add(d);
            else text.Add(d);
        }

        _textDets = text;
        Dets = text.Concat(entities).ToArray();
        InvalidateVisual();
    }

    public void ClearEntityTracks(string reason)
    {
        int count = Dets.Count(d => d.Kind == "Entity");
        Dets = _textDets;
        DebugTrace.Write("Overlay", $"Cleared visual entity tracks count={count} reason='{reason}'.");
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double fw = Bounds.Width, fh = Bounds.Height;
        var accent = new SolidColorBrush(Color.FromArgb(255, 90, 220, 120));
        var renderDets = Dets;

        if (FrameMode)
        {
            double cx = SideOffset, cy = TopOffset, cw = Math.Max(1, fw - 2 * SideOffset), ch = Math.Max(1, fh - TopOffset - SideOffset);
            var pen = new Pen(accent, 3);
            ctx.DrawRectangle(null, pen, new Rect(cx + 1.5, cy + 1.5, cw - 3, ch - 3));

            foreach (var m in Marks)
            {
                var col = RoleColor(m.Role);
                var r = new Rect(cx + m.X * cw, cy + m.Y * ch, m.W * cw, m.H * ch);
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(col), 2), r);
                if (!string.IsNullOrEmpty(m.Role))
                {
                    var rf = new FormattedText(m.Role, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, new SolidColorBrush(col));
                    ctx.DrawText(rf, new Point(r.X, Math.Max(0, r.Y - 13)));
                }
            }
        }

        // Auto-detection boxes: scale capture-pixel coords to the overlay.
        if (CapW > 0 && CapH > 0 && renderDets.Count > 0)
        {
            double sx = fw / CapW, sy = fh / CapH;
            var entPen = new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 95, 95)), 2);
            var txtPen = new Pen(new SolidColorBrush(Color.FromArgb(190, 95, 175, 255)), 1.5);
            var lblBrush = new SolidColorBrush(Color.FromArgb(255, 255, 235, 120));
            foreach (var d in renderDets)
            {
                bool ent = d.Kind == "Entity";
                var rect = new Rect(d.X * sx, d.Y * sy, Math.Max(1, d.W * sx), Math.Max(1, d.H * sy));
                ctx.DrawRectangle(null, ent ? entPen : txtPen, rect);
                if (ent && !string.IsNullOrEmpty(d.Label))
                {
                    var ft = new FormattedText(d.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 12, lblBrush);
                    ctx.DrawText(ft, new Point(rect.X, Math.Max(0, rect.Y - 14)));
                }
            }
        }

        if (WalkBox is { } wb)
        {
            double sx = CapW > 0 ? fw / CapW : 1.0;
            double sy = CapH > 0 ? fh / CapH : 1.0;
            var rect = new Rect(wb.X * sx, wb.Y * sy, Math.Max(1, wb.W * sx), Math.Max(1, wb.H * sy));
            var fill = new SolidColorBrush(Color.FromArgb(28, 255, 196, 76));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 196, 76)), 2);
            ctx.DrawRectangle(fill, pen, rect);
            var ft = new FormattedText("Smart Bot walk box", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromArgb(255, 255, 226, 140)));
            ctx.DrawText(ft, new Point(rect.X, Math.Max(0, rect.Y - 16)));
        }

        if (Values.Count > 0)
        {
            double sc = Math.Clamp(ValueScale, 0.5, 3.0);
            double fs = 13 * sc, padp = 8, lh = fs + 5, pw = 230 * sc;
            double ph = padp * 2 + Values.Count * lh;
            double px = Math.Max(8, fw - pw - 12), py = 42;
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(215, 12, 12, 14)), new Pen(accent, 1), new Rect(px, py, pw, ph), 6, 6);
            double ty = py + padp;
            foreach (var line in Values)
            {
                string role = line; int eq = line.IndexOf('=');
                if (eq < 0) eq = line.IndexOf(':');
                if (eq > 0) role = line.Substring(0, eq).Trim();
                var lf = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), fs, new SolidColorBrush(RoleColor(role)));
                ctx.DrawText(lf, new Point(px + padp, ty)); ty += lh;
            }
        }

        var bg = new SolidColorBrush(Color.FromArgb(210, 18, 20, 28));
        ctx.DrawRectangle(bg, null, new Rect(8, 8, 320, 26), 6, 6);
        int n = renderDets.Count;
        var ft2 = new FormattedText($"* OCR active - {Label}" + (n > 0 ? $"  ({n} detections)" : ""),
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, accent);
        ctx.DrawText(ft2, new Point(16, 11));

        var input = InputRuntimeStatus.Snapshot();
        double iw = Math.Min(Math.Max(360, fw - 24), 620);
        ctx.DrawRectangle(bg, null, new Rect(8, 40, iw, 46), 6, 6);
        var inputBrush = new SolidColorBrush(Color.FromArgb(255, 230, 230, 235));
        var mouseFt = new FormattedText(input.Mouse, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, inputBrush);
        var keyFt = new FormattedText(input.Keyboard, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, inputBrush);
        ctx.DrawText(mouseFt, new Point(16, 44));
        ctx.DrawText(keyFt, new Point(16, 64));
    }

    /// <summary>Stable distinct colour per role (hash -> hue), so every mark/value reads in its own colour.</summary>
    public static Color RoleColor(string role)
    {
        if (string.IsNullOrEmpty(role)) return Color.FromArgb(230, 200, 200, 205);
        int h = 0; foreach (var ch in role) h = (h * 31 + ch) & 0x7fffffff;
        return FromHsv(h % 360, 0.62, 0.98);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb(230, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
