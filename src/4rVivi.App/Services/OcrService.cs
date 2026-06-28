using System.Collections.Generic;
using System.Linq;
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
    private readonly RapidOcrClient _rapid = new();
    private readonly FourRVivi.Core.Ocr.RegionProfiles _regions = new();  // guide §8 Stage 2/6: per-region pipelines

    /// <summary>Guide §8 region-specific preprocessing: choose a preprocess mode from the role's region
    /// profile (monster/map -> CLAHE, inventory -> Adaptive, monster names also -> Close). Returns "Auto"
    /// when the region wants no special preprocessing, so callers only use it when a mark is on "Auto".</summary>
    public string SuggestPreprocess(string role)
    {
        var p = _regions.For(role ?? "");
        if (p.AdaptiveThreshold) return "Adaptive";
        if (p.Close) return "Close";
        if (p.Clahe >= 3.0) return "CLAHE";
        return "Auto";
    }

    public static readonly string[] SupportedLanguages = { "eng", "por", "spa", "jpn", "kor", "chi_sim", "chi_tra" };
    public string Language { get; set; } = "eng";
    public string PreprocessMode { get; set; } = "Auto";   // Auto | Light text | Dark text | Invert | Grayscale | Red | Green | Blue | High contrast
    public double Sharpen { get; set; } = 1.0;   // 0 = none, higher = sharper edges (helps every digit/letter)
    public int Upscale { get; set; } = 4;        // OCR magnification (Zoom slider); higher = sharper read on tiny HUD text
    public bool NameEntitiesByIcon { get; set; } = true;    // crop each detected box -> icon recognizer -> monster name
    public bool NameEntitiesByText { get; set; } = false;   // GRF-with-names: read the floating name text above the box
    public float EntityIconMinScore { get; set; } = 0.45f;   // icon cosine floor raised (multi-frame refs -> true matches score higher)
    public string LastEngine { get; private set; } = "-";   // which engine produced the last read
    public string EngineWarning { get; private set; } = "";   // non-empty when PaddleOCR was unavailable and a fallback was used
    public float LastRecScore { get; private set; } = 1f;   // recognition confidence of the last region read (PaddleOCR drop_score)
    public bool ScanTextEnabled { get; set; } = true;     // full-frame text detection
    public bool ScanEntitiesEnabled { get; set; } = true; // full-frame monster (YOLO) detection
    public bool MultiPass { get; set; }                    // try several preprocessings per read, keep highest confidence
    public float EntityMinScore { get; set; } = 0.55f;    // drop YOLO boxes below this (cuts false positives)
    public float TextMinScore { get; set; } = 0.30f;      // drop_score (RO tiny text scores low; Verify+Stable handle correctness)
    // HUD exclusion zone in capture pixels: detections whose CENTER falls inside are ignored (0 = off)
    public int ExclX { get; set; }
    public int ExclY { get; set; }
    public int ExclW { get; set; }
    public int ExclH { get; set; }

    public FourRVivi.Core.Settings.OcrTuningConfig Tuning { get; set; } = new();

    /// <summary>For digit roles, keep only 0-9, '/', '.', stripping separators and stray letters.</summary>
    public static string ConstrainNumeric(string raw, bool numeric)
    {
        if (!numeric || string.IsNullOrEmpty(raw)) return raw;
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
            if ((c >= '0' && c <= '9') || c == '/' || c == '.') sb.Append(c);
        return sb.ToString();
    }

    public static byte[] BitmapToPng(System.Drawing.Bitmap bmp)
    { using var ms = new MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }

    public void ApplyTuning()
    {
        Environment.SetEnvironmentVariable("OCR_CPU_THREADS", Tuning.CpuThreads.ToString());
        _rapid.SendConfig(
            $"boxThresh={Tuning.DetBoxThresh};boxScore={Tuning.DetBoxScoreThresh};" +
            $"unclip={Tuning.DetUnclipRatio};textScore={Tuning.TextScore};maxSide={Tuning.MaxSideLen};" +
            $"doAngle={(Tuning.DoAngle ? 1 : 0)};limitSide={Tuning.LimitSideLen};imgResize={Tuning.ImgResize}");
    }

    public void ReloadWorker() { _rapid.Restart(); ApplyTuning(); }

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
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

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

    /// <summary>One thing found on screen by the auto-scanner: text read by OCR, or an entity
    /// (monster/item/skill/minimap) found by YOLO + named by the icon embedder. Box is in the
    /// scanned image's pixel coords. Role is the user region it falls inside, or "" if free-floating.</summary>
    public sealed class IconHit
    {
        public int X, Y, W, H;
        public string Label = "";
        public float Score;
        public int Timer = -1;   // buff seconds left, or -1 if none
    }

    public sealed class ScanFind
    {
        public string Kind = "Text";          // "Text" | "Entity"
        public int X, Y, W, H;
        public string Value = "";             // text string, or entity label
        public float Score;
        public string Role = "";              // assigned user-region name, else ""
        public int Cx => X + W / 2;
        public int Cy => Y + H / 2;
    }

    /// <summary>AUTO-DETECTION: read EVERYTHING on a full screenshot — all on-screen text (OCR
    /// detect+recognize) plus all entities (YOLO + embedder) — not just user boxes. If <paramref
    /// name="anchors"/> are given, each find whose center lands inside a named region is tagged with
    /// that region's name (so user boxes become typed anchors); everything else is still returned.
    /// Never throws.</summary>
    /// <summary>True if a point falls inside the configured HUD exclusion zone (capture px).</summary>
    private bool InExclusion(int x, int y)
        => ExclW > 0 && ExclH > 0 && x >= ExclX && x < ExclX + ExclW && y >= ExclY && y < ExclY + ExclH;

    private static string AlnumLower(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Drop the group prefix from an icon label: "mob__agav"/"spr_x__agav" -> "agav".</summary>
    private static string StripIconPrefix(string label)
    {
        if (string.IsNullOrEmpty(label)) return label ?? "";
        int i = label.LastIndexOf("__", StringComparison.Ordinal);
        return i >= 0 ? label.Substring(i + 2) : label;
    }

    /// <summary>Detect text + entities on one PNG, offsetting box coords by (ox,oy) into full-frame space.</summary>
    private void AddRawFinds(List<ScanFind> finds, byte[] png, int ox, int oy)
    {
        if (ScanTextEnabled)
            foreach (var t in _rapid.ScanText(png))
            {
                if (string.IsNullOrWhiteSpace(t.Text) || t.Score < TextMinScore) continue;
                int x = t.X + ox, y = t.Y + oy;
                if (InExclusion(x + t.W / 2, y + t.H / 2)) continue;
                finds.Add(new ScanFind { Kind = "Text", X = x, Y = y, W = t.W, H = t.H, Value = t.Text, Score = t.Score });
            }
        if (ScanEntitiesEnabled)
            foreach (var e in _rapid.DetectEntities(png))
            {
                // multi-class detector: gate on the YOLO detection confidence (works for every class,
                // not just icon-named monsters). Loot/portal/target/HP carry their class as the value.
                if (e.Score < EntityMinScore) continue;
                // HUD bars (target/target_hp/player_hp) aren't moving entities -> skip. HP% comes from the
                // HP/MaxHP marks (percent = HP / MaxHP * 100), not from reading the player_hp box.
                if (e.Cls.Equals("target", StringComparison.OrdinalIgnoreCase)
                    || e.Cls.Equals("target_hp", StringComparison.OrdinalIgnoreCase)
                    || e.Cls.Equals("player_hp", StringComparison.OrdinalIgnoreCase)) continue;
                bool monster = string.IsNullOrEmpty(e.Cls)
                    || e.Cls.Equals("monster", StringComparison.OrdinalIgnoreCase)
                    || e.Cls.Equals("entity", StringComparison.OrdinalIgnoreCase);
                int x = e.X + ox, y = e.Y + oy;
                if (InExclusion(x + e.W / 2, y + e.H / 2)) continue;
                string val; float sc;
                if (monster)
                {
                    // a confident icon match names the specific monster; otherwise keep it as generic "monster".
                    if (!string.IsNullOrEmpty(e.Label) && e.LabelScore >= EntityIconMinScore) { val = e.Label; sc = e.LabelScore; }
                    else { val = "monster"; sc = e.Score; }
                }
                else { val = e.Cls; sc = e.Score; }
                finds.Add(new ScanFind { Kind = "Entity", X = x, Y = y, W = e.W, H = e.H, Value = val, Score = sc });
            }
    }

    /// <summary>Drop near-duplicate finds produced by overlapping zone seams (keep the higher score).</summary>
    private static void DedupFinds(List<ScanFind> finds)
    {
        for (int i = 0; i < finds.Count; i++)
            for (int j = finds.Count - 1; j > i; j--)
            {
                var a = finds[i]; var b = finds[j];
                if (a.Kind != b.Kind) continue;
                if (Math.Abs(a.Cx - b.Cx) <= 14 && Math.Abs(a.Cy - b.Cy) <= 12 &&
                    (a.Kind == "Entity" || string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    if (b.Score > a.Score) finds[i] = b;
                    finds.RemoveAt(j);
                }
            }
    }

    /// <summary>Zoned scan: split the frame into cols x rows tiles (with seam overlap) and detect each
    /// separately, so the detector sees each region at higher effective resolution. Coords merge back to
    /// full-frame; duplicates on seams are removed.</summary>
    public IReadOnlyList<ScanFind> ScanScreenZoned(System.Drawing.Bitmap full, int cols, int rows, IEnumerable<OcrRegion>? anchors = null)
    {
        var finds = new List<ScanFind>();
        if (full == null) return finds;
        try
        {
            cols = Math.Clamp(cols, 1, 6); rows = Math.Clamp(rows, 1, 6);
            const int ov = 24;   // overlap so text on a seam isn't cut in half
            int cw = full.Width / cols, ch = full.Height / rows;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int zx = Math.Max(0, c * cw - ov), zy = Math.Max(0, r * ch - ov);
                    int zw = Math.Min(full.Width - zx, cw + 2 * ov), zh = Math.Min(full.Height - zy, ch + 2 * ov);
                    if (zw < 8 || zh < 8) continue;
                    using var sub = full.Clone(new System.Drawing.Rectangle(zx, zy, zw, zh), full.PixelFormat);
                    AddRawFinds(finds, BitmapToPng(sub), zx, zy);
                }
            DedupFinds(finds);
            PostProcessFinds(finds, full, anchors);
        }
        catch { }
        return finds;
    }

    public IReadOnlyList<ScanFind> ScanScreen(System.Drawing.Bitmap full, IEnumerable<OcrRegion>? anchors = null)
    {
        var finds = new List<ScanFind>();
        if (full == null) return finds;
        try
        {
            AddRawFinds(finds, BitmapToPng(full), 0, 0);
            PostProcessFinds(finds, full, anchors);
        }
        catch { }
        return finds;
    }

    private void PostProcessFinds(List<ScanFind> finds, System.Drawing.Bitmap full, IEnumerable<OcrRegion>? anchors)
    {
        try
        {
            // Name each detected entity: by the floating GRF name text above it, or by sprite recognition.
            if (NameEntitiesByIcon || NameEntitiesByText)
            {
                var texts = finds.Where(f => f.Kind == "Text").ToList();
                foreach (var ent in finds)
                {
                    if (ent.Kind != "Entity") continue;
                    if (NameEntitiesByText)
                    {
                        ScanFind? best = null; int bestGap = int.MaxValue;
                        foreach (var t in texts)
                        {
                            bool overlapX = t.Cx >= ent.X - 12 && t.Cx <= ent.X + ent.W + 12;
                            if (!overlapX) continue;
                            int gap = ent.Y - (t.Y + t.H);     // text bottom above the box top
                            if (gap < -ent.H) continue;        // ignore text well inside/below the box
                            int d = Math.Abs(gap);
                            if (d < bestGap) { bestGap = d; best = t; }
                        }
                        if (best != null && bestGap <= Math.Max(24, ent.H)) ent.Value = best.Value;
                    }
                    else
                    {
                        try
                        {
                            int x = Math.Clamp(ent.X, 0, full.Width - 1);
                            int y = Math.Clamp(ent.Y, 0, full.Height - 1);
                            int w = Math.Clamp(ent.W, 1, full.Width - x);
                            int h = Math.Clamp(ent.H, 1, full.Height - y);
                            using var crop = full.Clone(new System.Drawing.Rectangle(x, y, w, h), full.PixelFormat);
                            var rec = RecognizeIcon(crop);
                            if (rec is { } rr && rr.Score >= EntityIconMinScore) ent.Value = StripIconPrefix(rr.Label);
                        }
                        catch { }
                    }
                }
            }

            var regions = anchors?.ToList();
            if (regions != null && regions.Count > 0)
                foreach (var f in finds)
                    foreach (var r in regions)
                        if (f.Cx >= r.X && f.Cx < r.X + r.Width && f.Cy >= r.Y && f.Cy < r.Y + r.Height)
                        { f.Role = r.Name; break; }
        }
        catch { }
    }

    /// <summary>Identify a single icon/sprite/minimap crop by image (icon embedder + nearest-neighbor).
    /// Returns (label, cosineScore) or null if the model/worker is unavailable. Never throws.</summary>
    public (string Label, float Score)? RecognizeIcon(System.Drawing.Bitmap crop)
    {
        try { return crop == null ? null : _rapid.RecognizeIcon(BitmapToPng(crop)); }
        catch { return null; }
    }

    /// <summary>Detect all on-screen entities in a full screenshot (YOLO finds boxes; the embedder
    /// names each). Returns [] if the detector/worker is unavailable. Never throws.</summary>
    public IReadOnlyList<RapidOcrClient.Entity> DetectEntities(System.Drawing.Bitmap full)
    {
        try { return full == null ? System.Array.Empty<RapidOcrClient.Entity>() : _rapid.DetectEntities(BitmapToPng(full)); }
        catch { return System.Array.Empty<RapidOcrClient.Entity>(); }
    }

    /// <summary>OCR a preprocessed PNG: Windows OCR first, Tesseract (digit-tuned) fallback.</summary>
    private string Recognize(byte[] png, bool numeric)
    {
        try
        {
            // FORCED PaddleOCR: while the worker is alive it is the ONLY engine. If it answers at all
            // (even an empty string) we trust it and never silently fall back to a weaker engine.
            var rapid = _rapid.RecognizeLine(png);
            if (rapid != null)
            {
                LastEngine = "PaddleOCR"; EngineWarning = ""; LastRecScore = _rapid.LastScore;
                // PaddleOCR drop_score: discard low-confidence region reads (cuts garbage on the priority fields).
                if (!string.IsNullOrEmpty(rapid) && LastRecScore > 0f && LastRecScore < TextMinScore) return "";
                return ConstrainNumeric(rapid, numeric);
            }

            // Worker is DOWN -> degrade, but raise a loud warning so the user knows OCR is no longer Paddle.
            var win = _winOcr.Recognize(png);
            if (!string.IsNullOrWhiteSpace(win))
            {
                LastEngine = "Windows OCR";
                EngineWarning = "PaddleOCR worker unavailable — fell back to Windows OCR. Reads may be wrong; ship/restart the OcrServer worker.";
                return win;
            }

            string lang = Language;
            if (string.IsNullOrEmpty(_tessDir) || !File.Exists(Path.Combine(_tessDir, lang + ".traineddata")))
            {
                if (File.Exists(Path.Combine(_tessDir, "eng.traineddata"))) lang = "eng";
                else { EngineWarning = "PaddleOCR worker unavailable and no fallback OCR data found."; return ""; }
            }
            using var eng = new TesseractEngine(_tessDir, lang, EngineMode.Default);
            if (numeric) eng.SetVariable("tessedit_char_whitelist", "0123456789/ ");
            using var img = Pix.LoadFromMemory(png);
            using var page = eng.Process(img, numeric ? PageSegMode.SingleLine : PageSegMode.Auto);
            var txt = page.GetText();
            LastEngine = "Tesseract";
            EngineWarning = "PaddleOCR worker unavailable — fell back to Tesseract. Reads may be wrong; ship/restart the OcrServer worker.";
            return txt;
        }
        catch { return ""; }
    }

    /// <summary>Downsample a region to 32x32 grayscale bytes for cheap frame-to-frame motion diffing.</summary>
    public byte[]? CropGray(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh, int topOffset, int sideOffset)
    {
        try
        {
            var rect = ClientRect(full.Width, full.Height, fx, fy, fw, fh, topOffset, sideOffset);
            using var sub = full.Clone(rect, full.PixelFormat);
            using var small = new System.Drawing.Bitmap(32, 32);
            using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(sub, 0, 0, 32, 32); }
            var b = new byte[32 * 32];
            int i = 0;
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                { var c = small.GetPixel(x, y); b[i++] = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B); }
            return b;
        }
        catch { return null; }
    }

    /// <summary>Hard-attach capture of the target window's CLIENT area only (the game content, no title
    /// bar / borders), via PrintWindow PW_CLIENTONLY|PW_RENDERFULLCONTENT (works on occluded / GPU windows
    /// where CopyFromScreen returns black). Falls back to a client-rect screen copy. Because it reads the
    /// live client rect every call, the marks track the window wherever it is moved or resized. Caller disposes.</summary>
    public System.Drawing.Bitmap? CaptureWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var cr)) return null;
            int w = cr.Right - cr.Left, h = cr.Bottom - cr.Top;
            if (w <= 0 || h <= 0) return null;
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok;
                try { ok = PrintWindow(hwnd, hdc, 3 /* PW_CLIENTONLY | PW_RENDERFULLCONTENT */); }
                finally { g.ReleaseHdc(hdc); }
                if (!ok)
                {
                    // Fallback: copy the client rectangle straight off the screen (maps client (0,0) to screen).
                    var p = new POINT { X = 0, Y = 0 };
                    if (!ClientToScreen(hwnd, ref p)) return null;
                    g.CopyFromScreen(p.X, p.Y, 0, 0, new System.Drawing.Size(w, h));
                }
            }
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Capture a raw screen rectangle (a whole monitor). Caller disposes.</summary>
    public System.Drawing.Bitmap? CaptureMonitor(int x, int y, int w, int h)
    {
        try
        {
            if (w <= 0 || h <= 0) return null;
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
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
    public string ReadRectFrom(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh, bool numeric, int topOffset, int sideOffset, string? mode = null, double? sharpen = null, string engine = "Paddle")
    {
        try
        {
            var rect = ClientRect(full.Width, full.Height, fx, fy, fw, fh, topOffset, sideOffset);
            using var sub = full.Clone(rect, full.PixelFormat);
            using var pre = Preprocess(sub, mode, sharpen);
            using var ms = new MemoryStream();
            pre.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] png = ms.ToArray();
            if (string.Equals(engine, "Windows", StringComparison.OrdinalIgnoreCase))
            {
                var wtxt = _winOcr.Recognize(png);
                LastEngine = "Windows OCR";
                LastRecScore = string.IsNullOrWhiteSpace(wtxt) ? 0f : 0.9f;
                return ConstrainNumeric(wtxt ?? "", numeric);
            }
            if (string.Equals(engine, "Ensemble", StringComparison.OrdinalIgnoreCase))
            {
                string ptxt = Recognize(png, numeric);     // Paddle (sets LastRecScore/LastEngine)
                float pconf = LastRecScore;
                string wtxt = _winOcr.Recognize(png) ?? "";
                string pn = AlnumLower(ptxt), wn = AlnumLower(wtxt);
                if (pn.Length > 0 && pn == wn) { LastEngine = "Paddle+Win"; LastRecScore = Math.Min(1f, pconf + 0.10f); return ptxt; }
                if (!string.IsNullOrWhiteSpace(wtxt) && (string.IsNullOrWhiteSpace(ptxt) || pconf < 0.85f))
                { LastEngine = "Windows OCR"; LastRecScore = 0.88f; return ConstrainNumeric(wtxt, numeric); }
                return ptxt;
            }
            return Recognize(png, numeric);
        }
        catch { return ""; }
    }

    /// <summary>Multi-pass read: try the mark's settings plus a few strong fallbacks (CLAHE, Adaptive,
    /// high-contrast) and return the highest-confidence result. Best for un-calibrated fields.</summary>
    public string ReadRectBest(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh,
        bool numeric, int topOffset, int sideOffset, string baseMode, double baseSharpen, string engine = "Paddle")
    {
        var combos = new (string m, double s)[]
        {
            (baseMode, baseSharpen), ("CLAHE", baseSharpen), ("Adaptive", baseSharpen),
            ("High contrast", 1.0), ("Auto", 2.0)
        };
        string best = ""; float bestScore = -1f;
        foreach (var (md, sp) in combos)
        {
            string txt;
            try { txt = ReadRectFrom(full, fx, fy, fw, fh, numeric, topOffset, sideOffset, md, sp, engine); }
            catch { continue; }
            float sc = LastRecScore;
            if (!string.IsNullOrWhiteSpace(txt) && sc > bestScore) { bestScore = sc; best = txt; }
        }
        LastRecScore = bestScore < 0 ? 0f : bestScore;
        return best;
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
        // The fill is always the LEFT part. Find the strongest colour transition = fill boundary.
        // (Polarity-independent: works whether the fill is brighter OR darker than the empty part.)
        double bestDiff = 0; int boundary = -1;
        for (int cx = 2; cx < w - 1; cx++) { double d = Math.Abs(col[cx] - col[cx - 1]); if (d > bestDiff) { bestDiff = d; boundary = cx; } }
        if (boundary > 0 && bestDiff >= (max - min) * 0.35)
            return Math.Clamp((int)Math.Round(boundary * 100.0 / (w - 1)), 0, 100);
        // uniform-ish bar (near 0 or 100): fall back to bright-count
        int bright = 0, total = 0;
        for (int cx = 1; cx < w - 1; cx++) { total++; if (col[cx] >= thr) bright++; }
        return total > 0 ? Math.Clamp((int)Math.Round(bright * 100.0 / total), 0, 100) : 0;
    }

    /// <summary>Grayscale → 2x upscale → binary threshold. Greatly improves OCR on small HUD text.</summary>
    /// <summary>Slice a marked skill/buff bar into icon cells, recognise each via the icon embedder, and
    /// (for buffs) read the countdown number in the cell. Coords returned in full-frame pixels.</summary>
    public IReadOnlyList<IconHit> ScanIcons(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh,
        int topOffset, int sideOffset, int cellPx, bool readTimer, float minScore = 0.35f)
    {
        var hits = new List<IconHit>();
        try
        {
            var rect = ClientRect(full.Width, full.Height, fx, fy, fw, fh, topOffset, sideOffset);
            using var sub = full.Clone(rect, full.PixelFormat);
            int cell = Math.Clamp(cellPx, 12, 128);
            int cols = Math.Max(1, sub.Width / cell), rows = Math.Max(1, sub.Height / cell);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int cx = c * cell, cy = r * cell;
                    int cw = Math.Min(cell, sub.Width - cx), chh = Math.Min(cell, sub.Height - cy);
                    if (cw < 8 || chh < 8) continue;
                    using var cellBmp = sub.Clone(new System.Drawing.Rectangle(cx, cy, cw, chh), sub.PixelFormat);
                    var rec = RecognizeIcon(cellBmp);
                    if (rec is not { } rr || rr.Score < minScore) continue;
                    int timer = -1;
                    if (readTimer)
                    {
                        using var ms = new MemoryStream();
                        using var pre = Preprocess(cellBmp, "Auto", 1.0);
                        pre.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        int t = ParseFirstInt(Recognize(ms.ToArray(), true) ?? "");
                        if (t >= 0) timer = t;
                    }
                    hits.Add(new IconHit { X = rect.X + cx, Y = rect.Y + cy, W = cw, H = chh, Label = rr.Label, Score = rr.Score, Timer = timer });
                }
        }
        catch { }
        return hits;
    }

    /// <summary>Ground-truth calibration: sweep colour layer x sharpness x position offset until the
    /// region reads EXACTLY the user-provided value. Returns the winning settings (matched=true) or the
    /// closest attempt. Pins reliable per-field settings instead of guessing thresholds.</summary>
    public (string mode, double sharpen, double dx, double dy, bool matched) CalibrateToValue(
        System.Drawing.Bitmap full, double fx, double fy, double fw, double fh,
        string expected, bool numeric, bool combined, int topOffset, int sideOffset)
    {
        string[] modes = { "Auto", "Light text", "Dark text", "Invert", "Grayscale", "High contrast", "Red", "Green", "Blue", "Cyan", "Yellow", "Magenta", "Saturation", "Max RGB", "Min RGB", "R-G", "R-B", "G-B", "Adaptive", "CLAHE", "Median", "Close" };
        double[] sharps = { 0.0, 1.0, 2.0, 4.0, 8.0, 20.0, 60.0, 150.0, 300.0 };
        string bestMode = "Auto"; double bestSharp = 1.0, bestDx = 0, bestDy = 0; int bestScore = -1;

        // local read+score helper; returns true (and sets the result) on an exact match.
        bool Probe(double dx, double dy, string md, double sh, out (string, double, double, double, bool) hit)
        {
            hit = default;
            double ny = Math.Clamp(fy + dy, 0, 1), nx = Math.Clamp(fx + dx, 0, 1);
            string read;
            try { read = ReadRectFrom(full, nx, ny, fw, fh, numeric, topOffset, sideOffset, md, sh); }
            catch { return false; }
            if (ValueMatches(read, expected, numeric, combined)) { hit = (md, sh, dx, dy, true); return true; }
            int sc = Overlap(read, expected);
            if (sc > bestScore) { bestScore = sc; bestMode = md; bestSharp = sh; bestDx = dx; bestDy = dy; }
            return false;
        }

        // Stage 1 (fast, ~198 reads): no positional offset — sweep every colour/filter mode x sharpness.
        // This resolves the large majority of fields and early-exits on the first exact match.
        foreach (var md in modes)
            foreach (var sh in sharps)
                if (Probe(0, 0, md, sh, out var h1)) return h1;

        // Stage 2 (only on a miss): nudge the ROI by small offsets, but ONLY around the best mode found
        // in stage 1 (plus a couple of strong fallbacks) and a coarse sharpness set — keeps it fast.
        double[] offs = { -0.006, 0.006, -0.012, 0.012 };
        double[] coarseSharps = { 0.0, 2.0, 8.0, 60.0, 300.0 };
        var probeModes = new System.Collections.Generic.List<string> { bestMode };
        foreach (var m in new[] { "Auto", "CLAHE", "Adaptive" }) if (!probeModes.Contains(m)) probeModes.Add(m);
        foreach (var dy in offs)
            foreach (var dx in offs)
                foreach (var md in probeModes)
                    foreach (var sh in coarseSharps)
                        if (Probe(dx, dy, md, sh, out var h2)) return h2;

        return (bestMode, bestSharp, bestDx, bestDy, false);
    }

    private static bool ValueMatches(string read, string expected, bool numeric, bool combined)
    {
        if (string.IsNullOrWhiteSpace(read)) return false;
        if (combined)
        {
            var a = ParseTwoInts(read); var b = ParseTwoInts(expected);
            return a is { } av && b is { } bv && av.Item1 == bv.Item1 && av.Item2 == bv.Item2;
        }
        if (numeric)
        {
            int a = ParseFirstInt(read), b = ParseFirstInt(expected);
            return a >= 0 && a == b;
        }
        return read.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Overlap(string read, string expected)
    {
        read = (read ?? "").Trim(); expected = (expected ?? "").Trim();
        if (read.Length == 0) return -1;
        int n = 0, m = Math.Min(read.Length, expected.Length);
        for (int i = 0; i < m; i++) if (char.ToLowerInvariant(read[i]) == char.ToLowerInvariant(expected[i])) n++;
        return n - Math.Abs(read.Length - expected.Length);
    }

    /// <summary>Brute-search colour layer x sharpness for the read that best matches the mark's type.
    /// Returns the winning (mode, sharpen) so each mark can use its own settings instead of one global filter.</summary>
    public (string mode, double sharpen) AutoTuneMark(System.Drawing.Bitmap full, double fx, double fy, double fw, double fh,
        bool numeric, bool combined, int topOffset, int sideOffset)
    {
        string[] modes = { "Auto", "Light text", "Dark text", "Invert", "Grayscale", "High contrast", "Red", "Green", "Blue", "Cyan", "Yellow", "Magenta", "Saturation", "Max RGB", "Min RGB", "R-G", "R-B", "G-B", "Adaptive", "CLAHE", "Median", "Close" };
        double[] sharps = { 0.0, 1.0, 2.0, 4.0, 8.0, 20.0, 60.0, 150.0, 300.0 };
        string bestMode = "Auto"; double bestSharp = 1.0; int bestScore = -1;
        try
        {
            var rect = ClientRect(full.Width, full.Height, fx, fy, fw, fh, topOffset, sideOffset);
            using var sub = full.Clone(rect, full.PixelFormat);
            foreach (var md in modes)
                foreach (var sh in sharps)
                {
                    string txt;
                    using (var pre = Preprocess(sub, md, sh))
                    using (var ms = new MemoryStream())
                    {
                        pre.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        txt = Recognize(ms.ToArray(), numeric) ?? "";
                    }
                    int score = ScoreRead(txt, numeric, combined);
                    if (score > bestScore) { bestScore = score; bestMode = md; bestSharp = sh; }
                }
        }
        catch { }
        return (bestMode, bestSharp);
    }

    private static int ScoreRead(string txt, bool numeric, bool combined)
    {
        if (string.IsNullOrWhiteSpace(txt)) return 0;
        if (combined) { var p = ParseTwoInts(txt); return p is { } v ? v.Item1.ToString().Length + v.Item2.ToString().Length + 5 : 0; }
        if (numeric) { int n = ParseFirstInt(txt); return n >= 0 ? n.ToString().Length + 5 : 0; }
        int alnum = 0; foreach (var ch in txt) if (char.IsLetterOrDigit(ch)) alnum++;
        return alnum;
    }

    /// <summary>Pad the processed crop with a white quiet zone so glyphs touching the mark edge are
    /// still detected/recognized (a standard OCR best practice for tight crops).</summary>
    private static System.Drawing.Bitmap AddQuietBorder(System.Drawing.Bitmap src, int bw)
    {
        if (bw <= 0) return src;
        var outb = new System.Drawing.Bitmap(src.Width + 2 * bw, src.Height + 2 * bw);
        using (var g = Graphics.FromImage(outb)) { g.Clear(System.Drawing.Color.White); g.DrawImageUnscaled(src, bw, bw); }
        src.Dispose();
        return outb;
    }

    private System.Drawing.Bitmap Preprocess(System.Drawing.Bitmap src) => Preprocess(src, null, null);

    private System.Drawing.Bitmap Preprocess(System.Drawing.Bitmap src, string? modeStr, double? sharpenOv)
    {
        string modeSel = string.IsNullOrEmpty(modeStr) ? PreprocessMode : modeStr;
        double sharpenVal = sharpenOv ?? Sharpen;
        int f = Math.Clamp(Upscale, 2, 10);
        int w = src.Width * f, h = src.Height * f;
        var big = new System.Drawing.Bitmap(w, h);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }

        string mode = modeSel ?? "Auto";
        Func<System.Drawing.Color, int> val = mode switch
        {
            "Red" => c => c.R,
            "Green" => c => c.G,
            "Blue" => c => c.B,
            "Cyan" => c => (c.G + c.B) / 2,
            "Yellow" => c => (c.R + c.G) / 2,
            "Magenta" => c => (c.R + c.B) / 2,
            "Max RGB" => c => Math.Max(c.R, Math.Max(c.G, c.B)),
            "Min RGB" => c => Math.Min(c.R, Math.Min(c.G, c.B)),
            "Saturation" => c => { int mx = Math.Max(c.R, Math.Max(c.G, c.B)); int mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx == 0 ? 0 : (mx - mn) * 255 / mx; },
            "R-G" => c => Math.Clamp(128 + c.R - c.G, 0, 255),
            "R-B" => c => Math.Clamp(128 + c.R - c.B, 0, 255),
            "G-B" => c => Math.Clamp(128 + c.G - c.B, 0, 255),
            _ => c => (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B),
        };

        var raw = new int[w * h];
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
                raw[yy * w + xx] = Math.Clamp(val(big.GetPixel(xx, yy)), 0, 255);

        // Unsharp mask: sharpens digit edges so loops in 8/0/9 survive thresholding
        var map = new int[w * h];
        var hist = new int[256];
        int min = 255, max = 0;
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                int i = yy * w + xx, c = raw[i];
                int up = yy > 0 ? raw[i - w] : c, dn = yy < h - 1 ? raw[i + w] : c;
                int lf = xx > 0 ? raw[i - 1] : c, rt = xx < w - 1 ? raw[i + 1] : c;
                int lap = 4 * c - up - dn - lf - rt;
                int v = Math.Clamp(c + (int)(sharpenVal * lap), 0, 255);
                map[i] = v; hist[v]++;
                if (v < min) min = v; if (v > max) max = v;
            }

        if (mode == "CLAHE")
        {
            int tw = Math.Max(8, w / 8), th = Math.Max(8, h / 8);
            int gx = (w + tw - 1) / tw, gy = (h + th - 1) / th;
            const double clip = 3.0;
            var luts = new byte[gx * gy][];
            for (int ty = 0; ty < gy; ty++)
                for (int tx = 0; tx < gx; tx++)
                {
                    int x0 = tx * tw, y0 = ty * th, x1 = Math.Min(w, x0 + tw), y1 = Math.Min(h, y0 + th);
                    var hg = new int[256]; int cnt = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++) { hg[Math.Clamp(map[yy * w + xx], 0, 255)]++; cnt++; }
                    int limit = (int)Math.Max(1, clip * cnt / 256);
                    int excess = 0;
                    for (int i = 0; i < 256; i++) if (hg[i] > limit) { excess += hg[i] - limit; hg[i] = limit; }
                    int inc = excess / 256;
                    for (int i = 0; i < 256; i++) hg[i] += inc;
                    var lut = new byte[256]; int acc = 0; double sc = cnt > 0 ? 255.0 / cnt : 0;
                    for (int i = 0; i < 256; i++) { acc += hg[i]; lut[i] = (byte)Math.Clamp(acc * sc, 0, 255); }
                    luts[ty * gx + tx] = lut;
                }
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    double fxp = (xx / (double)tw) - 0.5, fyp = (yy / (double)th) - 0.5;
                    int ix = (int)Math.Floor(fxp), iy = (int)Math.Floor(fyp);
                    double ddx = fxp - ix, ddy = fyp - iy;
                    int ix0 = Math.Clamp(ix, 0, gx - 1), ix1 = Math.Clamp(ix + 1, 0, gx - 1);
                    int iy0 = Math.Clamp(iy, 0, gy - 1), iy1 = Math.Clamp(iy + 1, 0, gy - 1);
                    int v0 = Math.Clamp(map[yy * w + xx], 0, 255);
                    double a = luts[iy0 * gx + ix0][v0], b = luts[iy0 * gx + ix1][v0], c = luts[iy1 * gx + ix0][v0], d = luts[iy1 * gx + ix1][v0];
                    double top = a + (b - a) * ddx, bot = c + (d - c) * ddx, val2 = top + (bot - top) * ddy;
                    int vv = Math.Clamp((int)val2, 0, 255);
                    big.SetPixel(xx, yy, System.Drawing.Color.FromArgb(vv, vv, vv));
                }
            return AddQuietBorder(big, f * 3);
        }

        if (mode == "Grayscale" || mode == "High contrast")
        {
            double range = Math.Max(1, max - min);
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int v = (int)Math.Clamp((map[yy * w + xx] - min) * 255.0 / range, 0, 255);
                    big.SetPixel(xx, yy, System.Drawing.Color.FromArgb(v, v, v));
                }
            return AddQuietBorder(big, f * 3);
        }

        if (mode == "Median")
        {
            var outm = new int[w * h]; int[] win = new int[9];
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int k = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        { int ny = Math.Clamp(yy + dy, 0, h - 1), nx = Math.Clamp(xx + dx, 0, w - 1); win[k++] = map[ny * w + nx]; }
                    System.Array.Sort(win); outm[yy * w + xx] = win[4];
                }
            for (int i = 0; i < outm.Length; i++) { int vv = Math.Clamp(outm[i], 0, 255); big.SetPixel(i % w, i / w, System.Drawing.Color.FromArgb(vv, vv, vv)); }
            return AddQuietBorder(big, f * 3);
        }

        if (mode == "Close")
        {
            int tc = Otsu(hist, w * h);
            var bin = new bool[w * h];
            for (int i = 0; i < map.Length; i++) bin[i] = map[i] < tc;   // dark text
            var dil = new bool[w * h];
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    bool any = false;
                    for (int dy = -1; dy <= 1 && !any; dy++)
                        for (int dx = -1; dx <= 1; dx++) { int ny = Math.Clamp(yy + dy, 0, h - 1), nx = Math.Clamp(xx + dx, 0, w - 1); if (bin[ny * w + nx]) { any = true; break; } }
                    dil[yy * w + xx] = any;
                }
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    bool all = true;
                    for (int dy = -1; dy <= 1 && all; dy++)
                        for (int dx = -1; dx <= 1; dx++) { int ny = Math.Clamp(yy + dy, 0, h - 1), nx = Math.Clamp(xx + dx, 0, w - 1); if (!dil[ny * w + nx]) { all = false; break; } }
                    big.SetPixel(xx, yy, all ? System.Drawing.Color.Black : System.Drawing.Color.White);
                }
            return AddQuietBorder(big, f * 3);
        }

        if (mode == "Adaptive")
        {
            int blk = Math.Max(7, f * 4), C = 8, W1 = w + 1;
            var integ = new long[W1 * (h + 1)];
            for (int yy = 0; yy < h; yy++)
            {
                long row = 0;
                for (int xx = 0; xx < w; xx++) { row += map[yy * w + xx]; integ[(yy + 1) * W1 + (xx + 1)] = integ[yy * W1 + (xx + 1)] + row; }
            }
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int x0 = Math.Max(0, xx - blk), x1 = Math.Min(w - 1, xx + blk), y0 = Math.Max(0, yy - blk), y1 = Math.Min(h - 1, yy + blk);
                    long sum = integ[(y1 + 1) * W1 + (x1 + 1)] - integ[y0 * W1 + (x1 + 1)] - integ[(y1 + 1) * W1 + x0] + integ[y0 * W1 + x0];
                    int area = (x1 - x0 + 1) * (y1 - y0 + 1);
                    int mean = (int)(sum / area);
                    bool dark = map[yy * w + xx] < mean - C;
                    big.SetPixel(xx, yy, dark ? System.Drawing.Color.Black : System.Drawing.Color.White);
                }
            return AddQuietBorder(big, f * 3);
        }

        int t = Otsu(hist, w * h);
        int light = 0; for (int i = 0; i < map.Length; i++) if (map[i] >= t) light++;
        bool textIsLight = mode switch
        {
            "Light text" => true,
            "Dark text" => false,
            "Invert" => !(light * 2 < map.Length),
            _ => light * 2 < map.Length,
        };
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                bool bright = map[yy * w + xx] >= t;
                bool isText = textIsLight ? bright : !bright;
                big.SetPixel(xx, yy, isText ? System.Drawing.Color.Black : System.Drawing.Color.White);
            }
        return AddQuietBorder(big, f * 3);
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
