using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace FourRVivi.App.Services;

public interface IScreenshotService
{
    byte[]? CaptureWindow(IntPtr hwnd);
    string? SaveWindow(IntPtr hwnd, string dir);
}

/// <summary>Captures a game window to PNG (for OCR, bug reports, sharing).</summary>
public sealed class ScreenshotService : IScreenshotService
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public byte[]? CaptureWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return null;
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch { return null; }
    }

    public string? SaveWindow(IntPtr hwnd, string dir)
    {
        var data = CaptureWindow(hwnd);
        if (data is null) return null;
        try
        {
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(p, data);
            return p;
        }
        catch { return null; }
    }
}
