using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Tesseract;

namespace FourRVivi.App.Services;

/// <summary>A named OCR capture region (absolute screen coords, or window-relative when used
/// via <see cref="OcrService.ReadRegion"/>).</summary>
public sealed class OcrRegion
{
    public string Name { get; set; } = "Custom";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 260;
    public int Height { get; set; } = 170;
}

/// <summary>Captures a region of the game window and OCRs RO values. Adds a preprocessing
/// pipeline (grayscale → upscale → threshold), configurable regions + presets, multi-language
/// tessdata, and extended parsing (HP/SP/Weight/Zeny/Base/Job/EXP%/coords). Never throws.</summary>
public sealed class OcrService
{
    private readonly string _tessDir = ResolveTessDir();
    private readonly WindowsOcrEngine _winOcr = new();

    public static readonly string[] SupportedLanguages = { "eng", "por", "spa", "jpn", "kor", "chi_sim", "chi_tra" };
    public string Language { get; set; } = "eng";

    public IReadOnlyList<OcrRegion> Presets { get; } = new List<OcrRegion>
    {
        new() { Name = "Classic (top-left)", X = 0, Y = 0, Width = 260, Height = 170 },
        new() { Name = "Renewal (top-left)", X = 8, Y = 8, Width = 290, Height = 185 },
        new() { Name = "1080p basic info",   X = 0, Y = 0, Width = 300, Height = 200 },
        new() { Name = "Custom",             X = 0, Y = 0, Width = 260, Height = 170 },
    };
    public OcrRegion CurrentRegion { get; private set; } = new() { Name = "Classic (top-left)", Width = 260, Height = 170 };

    public void SetRegion(OcrRegion region) => CurrentRegion = region;
    public void SetRegion(int x, int y, int w, int h) => CurrentRegion = new OcrRegion { Name = "Custom", X = x, Y = y, Width = w, Height = h };

    private static string ResolveTessDir()
    {
        string? b = null;
        try { b = AppContext.BaseDirectory; } catch { }
        if (string.IsNullOrEmpty(b)) { try { b = AppDomain.CurrentDomain.BaseDirectory; } catch { } }
        if (string.IsNullOrEmpty(b)) { try { b = Path.GetDirectoryName(Environment.ProcessPath); } catch { } }
        if (string.IsNullOrEmpty(b)) b = Directory.GetCurrentDirectory();
        return Path.Combine(b!, "tessdata");
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public Task<bool> EnsureDataAsync() => EnsureLanguageAsync(Language);

    public async Task<bool> EnsureLanguageAsync(string lang)
    {
        string p = Path.Combine(_tessDir, lang + ".traineddata");
        if (File.Exists(p)) return true;
        try
        {
            Directory.CreateDirectory(_tessDir);
            using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var b = await h.GetByteArrayAsync($"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata");
            await File.WriteAllBytesAsync(p, b);
            return true;
        }
        catch { return false; }
    }

    /// <summary>OCR a window-relative fractional rectangle (0..1). Used by the calibrated OCR loop.</summary>
    public string ReadRect(IntPtr hwnd, double fx, double fy, double fw, double fh, bool numeric = false, int topOffset = 0, int sideOffset = 0)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return "";
        int W = r.Right - r.Left, H = r.Bottom - r.Top;
        if (W <= 0 || H <= 0) return "";
        // windowed: skip the title bar / borders so fractions map to the client area
        int left = r.Left + sideOffset, top = r.Top + topOffset;
        int cw = Math.Max(1, W - 2 * sideOffset), ch = Math.Max(1, H - topOffset - sideOffset);
        int x = left + (int)(fx * cw), y = top + (int)(fy * ch);
        int w = Math.Max(1, (int)(fw * cw)), h = Math.Max(1, (int)(fh * ch));
        return Read(IntPtr.Zero, x, y, w, h, numeric);
    }

    /// <summary>Measure how full a HP/SP/EXP bar is (0..100) by the colored fill width — no OCR.</summary>
    public int ReadBarPercent(IntPtr hwnd, double fx, double fy, double fw, double fh, int topOffset = 0, int sideOffset = 0)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return -1;
            int W = r.Right - r.Left, H = r.Bottom - r.Top;
            if (W <= 0 || H <= 0) return -1;
            int left = r.Left + sideOffset, top = r.Top + topOffset;
            int cw = Math.Max(1, W - 2 * sideOffset), ch = Math.Max(1, H - topOffset - sideOffset);
            int x = left + (int)(fx * cw), y = top + (int)(fy * ch);
            int w = Math.Max(2, (int)(fw * cw)), h = Math.Max(2, (int)(fh * ch));

            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));

            int y0 = h / 4, y1 = Math.Max(y0 + 1, h * 3 / 4);
            var col = new double[w];
            double min = double.MaxValue, max = double.MinValue;
            for (int cx = 0; cx < w; cx++)
            {
                double sum = 0; int n = 0;
                for (int cy = y0; cy < y1; cy++) { var c = bmp.GetPixel(cx, cy); sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; n++; }
                col[cx] = n > 0 ? sum / n : 0;
                if (col[cx] < min) min = col[cx];
                if (col[cx] > max) max = col[cx];
            }
            if (max - min < 12) return 0;   // flat = empty bar
            double thr = min + 0.5 * (max - min);
            int lastFilled = -1;
            for (int cx = 1; cx < w - 1; cx++) if (col[cx] >= thr) lastFilled = cx;   // rightmost bright = fill boundary
            if (lastFilled < 0) return 0;
            return Math.Clamp((int)Math.Round((lastFilled + 1) * 100.0 / w), 0, 100);
        }
        catch { return -1; }
    }

    /// <summary>Two integers separated by '/' (e.g. "96 / 96", "670 / 2030"). Null if not two found.</summary>
    public static (int, int)? ParseTwoInts(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text.Replace(",", ""), @"(\d+)\D+(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int a) && int.TryParse(m.Groups[2].Value, out int b)) return (a, b);
        return null;
    }

    /// <summary>First integer found in OCR text (handles "91", "91 / 91", " 1,234 ").</summary>
    public static int ParseFirstInt(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        var m = System.Text.RegularExpressions.Regex.Match(text.Replace(",", ""), @"\d+");
        return m.Success && int.TryParse(m.Value, out int v) ? v : -1;
    }

    /// <summary>OCR the configured region, interpreted relative to the window top-left.</summary>
    public string ReadRegion(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
            return Read(IntPtr.Zero, r.Left + CurrentRegion.X, r.Top + CurrentRegion.Y, CurrentRegion.Width, CurrentRegion.Height);
        return "";
    }

    /// <summary>OCR a region; if w/h are 0 it grabs the configured region of the game window.</summary>
    public string Read(IntPtr hwnd, int x, int y, int w, int h, bool numeric = false)
    {
        try
        {
            if (w <= 0 || h <= 0)
            {
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
                { x = r.Left + CurrentRegion.X; y = r.Top + CurrentRegion.Y; w = CurrentRegion.Width; h = CurrentRegion.Height; }
                else return "";
            }
            byte[] png;
            using (var raw = new System.Drawing.Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(raw)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
                using var pre = Preprocess(raw);
                using var ms = new MemoryStream();
                pre.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                png = ms.ToArray();
            }

            return Recognize(png, numeric);
        }
        catch { return ""; }
    }

    /// <summary>OCR a preprocessed PNG: Windows OCR first, Tesseract (digit-tuned) fallback.</summary>
    private string Recognize(byte[] png, bool numeric)
    {
        try
        {
            var win = _winOcr.Recognize(png);
            if (!string.IsNullOrWhiteSpace(win)) return win;

            string lang = Language;
            if (string.IsNullOrEmpty(_tessDir) || !File.Exists(Path.Combine(_tessDir, lang + ".traineddata")))
            {
                if (File.Exists(Path.Combine(_tessDir, "eng.traineddata"))) lang = "eng";
                else return "";
            }
            using var eng = new TesseractEngine(_tessDir, lang, EngineMode.Default);
            if (numeric) eng.SetVariable("tessedit_char_whitelist", "0123456789/ ");
            using var img = Pix.LoadFromMemory(png);
            using var page = eng.Process(img, numeric ? PageSegMode.SingleLine : PageSegMode.Auto);
            return page.GetText();
        }
        catch { return ""; }
    }

    /// <summary>Capture the whole target window via PrintWindow (works when occluded / on many GPU
    /// windows where CopyFromScreen returns black). Falls back to CopyFromScreen. Caller disposes.</summary>
    public System.Drawing.Bitmap? CaptureWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return null;
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok;
                try { ok = PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */); }
                finally { g.ReleaseHdc(hdc); }
                if (!ok) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
            }
            return bmp;
        }
        catch { return null; }
    }

    private static System.Drawing.Rectangle ClientRect(int W, int H, double fx, double fy, double fw, double fh, int topOffset, int sideOffset)
    {
        int cw = Math.Max(1, W - 2 * sideOffset), ch = Math.Max(1, H - topOffset - sideOffset);
        int x = Math.Clamp(sideOffset + (int)(fx * cw), 0, W - 1);
        int y = Math.Clamp(topOffset + (int)(fy * ch), 0, H - 1);
        int w = Math.Clamp((int)(fw * cw), 1, W - x);
        int h = Math.Clamp((int)(fh * ch), 1, H - y);
        return new System.Drawing.Rectangle(x, y, w, h);
    }

    /// <summary>OCR a fractional region cropped from a pre-captured full-window bitmap.</summary>
    public string ReadRectFrom(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh, bool numeric, int topOffset, int sideOffset)
    {
        try
        {
            var rect = ClientRect(full.Width, full.Height, fx, fy, fw, fh, topOffset, sideOffset);
            using var sub = full.Clone(rect, full.PixelFormat);
            using var pre = Preprocess(sub);
            using var ms = new MemoryStream();
            pre.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Recognize(ms.ToArray(), numeric);
        }
        catch { return ""; }
    }

    /// <summary>Bar fill % cropped from a pre-captured full-window bitmap.</summary>
    public int ReadBarPercentFrom(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh, int topOffset, int sideOffset)
    {
        try
        {
            var rect = ClientRect(full.Width, full.Height, fx, fy, fw, fh, topOffset, sideOffset);
            using var sub = full.Clone(rect, full.PixelFormat);
            return BarFill(sub);
        }
        catch { return -1; }
    }

    /// <summary>Fraction (0..100) of a bar that is filled (bright/colored), left-anchored.</summary>
    private static int BarFill(System.Drawing.Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        if (w < 2 || h < 2) return -1;
        int y0 = h / 4, y1 = Math.Max(y0 + 1, h * 3 / 4);
        var col = new double[w];
        double min = double.MaxValue, max = double.MinValue;
        for (int cx = 0; cx < w; cx++)
        {
            double sum = 0; int n = 0;
            for (int cy = y0; cy < y1; cy++) { var c = bmp.GetPixel(cx, cy); sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; n++; }
            col[cx] = n > 0 ? sum / n : 0;
            if (col[cx] < min) min = col[cx];
            if (col[cx] > max) max = col[cx];
        }
        if (max - min < 12) return 0;
        double thr = min + 0.5 * (max - min);
        int lastFilled = -1;
        for (int cx = 1; cx < w - 1; cx++) if (col[cx] >= thr) lastFilled = cx;
        if (lastFilled < 0) return 0;
        return Math.Clamp((int)Math.Round((lastFilled + 1) * 100.0 / w), 0, 100);
    }

    /// <summary>Grayscale → 2x upscale → binary threshold. Greatly improves OCR on small HUD text.</summary>
    private static System.Drawing.Bitmap Preprocess(System.Drawing.Bitmap src)
    {
        int w = src.Width * 3, h = src.Height * 3;
        var big = new System.Drawing.Bitmap(w, h);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        var lumMap = new int[w * h];
        var hist = new int[256];
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                var c = big.GetPixel(xx, yy);
                int lum = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                lumMap[yy * w + xx] = lum; hist[lum]++;
            }
        int t = Otsu(hist, w * h);
        int light = 0; for (int i = 0; i < lumMap.Length; i++) if (lumMap[i] >= t) light++;
        bool textIsLight = light * 2 < lumMap.Length;   // light pixels are the minority -> text is the light part (RO HUD)
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                bool bright = lumMap[yy * w + xx] >= t;
                bool isText = textIsLight ? bright : !bright;   // OCR wants BLACK text on WHITE
                big.SetPixel(xx, yy, isText ? System.Drawing.Color.Black : System.Drawing.Color.White);
            }
        return big;
    }

    /// <summary>Otsu's method: pick the luminance threshold that best separates text from background.</summary>
    private static int Otsu(int[] hist, int total)
    {
        long sum = 0; for (int i = 0; i < 256; i++) sum += (long)i * hist[i];
        long sumB = 0; int wB = 0; double max = 0; int thr = 130;
        for (int i = 0; i < 256; i++)
        {
            wB += hist[i]; if (wB == 0) continue;
            int wF = total - wB; if (wF == 0) break;
            sumB += (long)i * hist[i];
            double mB = (double)sumB / wB, mF = (double)(sum - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > max) { max = between; thr = i; }
        }
        return thr;
    }

    // ---- parsing ----------------------------------------------------------

    public Dictionary<string, int> Parse(string text) => ParseExtended(text);

    /// <summary>Extract every value we can: HP/SP/Weight (cur+max), Base/Job level, Zeny,
    /// Base/Job EXP %, and map coordinates (X/Y).</summary>
    public Dictionary<string, int> ParseExtended(string text)
    {
        var d = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(text)) return d;

        void Two(string pat, string a, string b)
        {
            var m = Regex.Match(text, pat, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var x) && int.TryParse(m.Groups[2].Value, out var y)) { d[a] = x; d[b] = y; }
        }
        void One(string pat, string a)
        {
            var m = Regex.Match(text, pat, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var x)) d[a] = x;
        }

        Two(@"HP\D*(\d+)\s*/\s*(\d+)", "HP", "MaxHP");
        Two(@"SP\D*(\d+)\s*/\s*(\d+)", "SP", "MaxSP");
        Two(@"(?:Weight|Peso)\D*(\d+)\s*/\s*(\d+)", "Weight", "MaxWeight");
        One(@"(?:Base ?Lv\.?|Base Level)\D*(\d+)", "BaseLevel");
        One(@"(?:Job ?Lv\.?|Classe ?Lv\.?|Job Level)\D*(\d+)", "JobLevel");
        One(@"Zeny\D*(\d+)", "Zeny");
        One(@"Base ?EXP?\D*(\d+)\s*%", "BaseExpPct");
        One(@"Job ?EXP?\D*(\d+)\s*%", "JobExpPct");
        // "prontera 155, 180" style coordinates
        var c = Regex.Match(text, @"([A-Za-z_]{3,})\s+(\d{1,3})\s*,\s*(\d{1,3})");
        if (c.Success && int.TryParse(c.Groups[2].Value, out var cx) && int.TryParse(c.Groups[3].Value, out var cy)) { d["PosX"] = cx; d["PosY"] = cy; }
        return d;
    }
}
