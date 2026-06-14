using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Tesseract;

namespace FourRVivi.App.Services;

/// <summary>Captures the game window's Basic-Info corner and OCRs HP/SP/Base/Job/Weight/Zeny + coords.
/// English tessdata downloads on first use (user's machine).</summary>
public sealed class OcrService
{
    private readonly string _tessDir = Path.Combine(AppContext.BaseDirectory, "tessdata");

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public async Task<bool> EnsureDataAsync()
    {
        string p = Path.Combine(_tessDir, "eng.traineddata");
        if (File.Exists(p)) return true;
        try
        {
            Directory.CreateDirectory(_tessDir);
            using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var b = await h.GetByteArrayAsync("https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata");
            await File.WriteAllBytesAsync(p, b);
            return true;
        }
        catch { return false; }
    }

    /// <summary>OCR a region; if w/h are 0 it grabs the top-left 260x170 of the game window.</summary>
    public string Read(IntPtr hwnd, int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
        {
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
            { x = r.Left; y = r.Top; w = 260; h = 170; }
            else return "";
        }
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        using var eng = new TesseractEngine(_tessDir, "eng", EngineMode.Default);
        using var img = Pix.LoadFromMemory(ms.ToArray());
        using var page = eng.Process(img);
        return page.GetText();
    }

    public Dictionary<string, int> Parse(string text)
    {
        var d = new Dictionary<string, int>();
        void Two(string label, string a, string b) { var m = Regex.Match(text, label); if (m.Success) { d[a] = int.Parse(m.Groups[1].Value); d[b] = int.Parse(m.Groups[2].Value); } }
        void One(string label, string a) { var m = Regex.Match(text, label); if (m.Success) d[a] = int.Parse(m.Groups[1].Value); }
        Two(@"HP\D*(\d+)\s*/\s*(\d+)", "HP", "MaxHP");
        Two(@"SP\D*(\d+)\s*/\s*(\d+)", "SP", "MaxSP");
        Two(@"(?:Weight|Peso)\D*(\d+)\s*/\s*(\d+)", "Weight", "MaxWeight");
        One(@"(?:Base Lv|Base Lv\.)\D*(\d+)", "BaseLevel");
        One(@"(?:Job Lv|Classe Lv)\D*(\d+)", "JobLevel");
        One(@"Zeny\D*(\d+)", "Zeny");
        return d;
    }
}
