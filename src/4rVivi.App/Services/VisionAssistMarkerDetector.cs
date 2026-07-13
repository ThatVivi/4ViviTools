using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using FourRVivi.Core.Common;
using FourRVivi.Core.Data;

namespace FourRVivi.App.Services;

public sealed record VisionAssistMarker(int X, int Y, int W, int H, int MobId, string Name, float Score);

public sealed record VisionAssistDetectionResult(
    IReadOnlyList<VisionAssistMarker> Markers,
    int RawBoxes,
    int Decoded,
    int NameUnknown);

public sealed class VisionAssistMarkerDetector
{
    private sealed record MobCode(int MobId, string Name, int[] Rgb);

    private readonly List<MobCode> _codes = new();
    private string _manifestPath = "";
    private int _boxPx = 2;
    private int _codeCellPx = 5;

    public bool Loaded => _codes.Count > 0;
    public string Status => Loaded ? $"VisionAssist table loaded mobs={_codes.Count}" : "VisionAssist table not loaded";

    public void LoadManifest(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? "" : path.Trim();
        bool useBuiltIn = string.IsNullOrWhiteSpace(path) || !File.Exists(path);
        string cacheKey = useBuiltIn ? "__builtin__:" + path : path;
        if (string.Equals(_manifestPath, cacheKey, StringComparison.OrdinalIgnoreCase) && Loaded)
            return;

        _manifestPath = cacheKey;
        _codes.Clear();
        if (useBuiltIn)
        {
            LoadBuiltInMobCodes(path);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            _boxPx = TryInt(doc.RootElement, "boxPx", 2);
            _codeCellPx = TryInt(doc.RootElement, "codeCellPx", TryInt(doc.RootElement, "codeCell", 5));
            if (!doc.RootElement.TryGetProperty("mobs", out var mobs) || mobs.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in mobs.EnumerateObject())
            {
                if (!int.TryParse(prop.Name, out int mobId))
                    continue;
                var obj = prop.Value;
                var name = obj.TryGetProperty("name", out var n) ? n.GetString() ?? $"mob_{mobId}" : $"mob_{mobId}";
                if (!obj.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Array)
                    continue;
                var rgb = new List<int>(9);
                foreach (var cell in code.EnumerateArray())
                {
                    if (cell.ValueKind != JsonValueKind.Array)
                        continue;
                    var vals = cell.EnumerateArray().Select(v => Math.Clamp(v.GetInt32(), 0, 255)).Take(3).ToArray();
                    if (vals.Length == 3)
                        rgb.AddRange(vals);
                }
                if (rgb.Count >= 9)
                    _codes.Add(new MobCode(mobId, name, rgb.Take(9).ToArray()));
            }
            DebugTrace.Write("OCR", $"VisionAssist manifest loaded path='{path}' mobs={_codes.Count} boxPx={_boxPx} codeCellPx={_codeCellPx}.");
        }
        catch (Exception ex)
        {
            _codes.Clear();
            DebugTrace.Write("OCR", $"VisionAssist manifest load failed path='{path}'.", ex);
            LoadBuiltInMobCodes(path);
        }
    }

    private void LoadBuiltInMobCodes(string? requestedPath)
    {
        try
        {
            _boxPx = 2;
            _codeCellPx = 5;
            _codes.Clear();
            var db = new GameDatabase();
            foreach (var mob in db.AllMobs())
            {
                if (mob.Id <= 0)
                    continue;
                _codes.Add(new MobCode(mob.Id, string.IsNullOrWhiteSpace(mob.Name) ? mob.Aegis : mob.Name, ColorCode(mob.Id)));
            }
            DebugTrace.Write("OCR", $"VisionAssist built-in table loaded mobs={_codes.Count} requestedManifest='{requestedPath}'.");
        }
        catch (Exception ex)
        {
            _codes.Clear();
            DebugTrace.Write("OCR", $"VisionAssist built-in table load failed requestedManifest='{requestedPath}'.", ex);
        }
    }

    private static int[] ColorCode(int mobId)
    {
        int[] levels = { 48, 96, 144, 192, 240 };
        int[] digits = new int[6];
        int n = mobId;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            digits[i] = n % levels.Length;
            n /= levels.Length;
        }

        return new[]
        {
            255, levels[digits[0]], levels[digits[1]],
            levels[digits[2]], 255, levels[digits[3]],
            levels[digits[4]], levels[digits[5]], 255
        };
    }

    public IReadOnlyList<VisionAssistMarker> Detect(Bitmap frame) => DetectWithDiagnostics(frame).Markers;

    public VisionAssistDetectionResult DetectWithDiagnostics(Bitmap frame)
    {
        if (!Loaded || frame == null || frame.Width < 16 || frame.Height < 16)
            return new VisionAssistDetectionResult(Array.Empty<VisionAssistMarker>(), 0, 0, 0);

        using var bmp = frame.PixelFormat == PixelFormat.Format32bppArgb
            ? (Bitmap)frame.Clone()
            : frame.Clone(new Rectangle(0, 0, frame.Width, frame.Height), PixelFormat.Format32bppArgb);

        int width = bmp.Width;
        int height = bmp.Height;
        var rect = new Rectangle(0, 0, width, height);
        var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bits.Stride;
            var bytes = new byte[Math.Abs(stride) * height];
            Marshal.Copy(bits.Scan0, bytes, 0, bytes.Length);
            var redMask = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int p = row + x * 4;
                    byte b = bytes[p];
                    byte g = bytes[p + 1];
                    byte r = bytes[p + 2];
                    redMask[y * width + x] = IsMarkerRed(r, g, b);
                }
            }

            var visited = new bool[redMask.Length];
            var queue = new int[Math.Min(redMask.Length, 1_000_000)];
            var markers = new List<VisionAssistMarker>();
            int rawBoxes = 0;
            int decodedCount = 0;
            for (int i = 0; i < redMask.Length; i++)
            {
                if (!redMask[i] || visited[i])
                    continue;
                var component = Flood(redMask, visited, queue, width, height, i);
                if (component.Area < 20 || component.W < 8 || component.H < 8 || component.W > 320 || component.H > 320)
                    continue;
                if (!LooksLikeRectangleBorder(redMask, width, component))
                    continue;

                rawBoxes++;
                var decoded = DecodeMob(bytes, stride, width, height, component.X, component.Y);
                if (decoded == null)
                {
                    markers.Add(new VisionAssistMarker(component.X, component.Y, component.W, component.H,
                        -1, "Monster", 0.30f));
                    continue;
                }
                var mob = decoded.Value;
                decodedCount++;
                markers.Add(new VisionAssistMarker(component.X, component.Y, component.W, component.H,
                    mob.MobId, mob.Name, mob.Score));
            }

            var deduped = Dedup(markers);
            int unknown = Math.Max(0, rawBoxes - decodedCount);
            return new VisionAssistDetectionResult(deduped, rawBoxes, decodedCount, unknown);
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
    }

    private static bool IsMarkerRed(byte r, byte g, byte b)
    {
        int hi = Math.Max(g, b);
        return r >= 70 && r - hi >= 35 && r >= g * 2 && r >= b * 2;
    }

    private static (int X, int Y, int W, int H, int Area) Flood(
        bool[] mask, bool[] visited, int[] queue, int width, int height, int start)
    {
        int head = 0, tail = 0;
        queue[tail++] = start;
        visited[start] = true;
        int minX = start % width, maxX = minX, minY = start / width, maxY = minY, area = 0;

        while (head < tail)
        {
            int idx = queue[head++];
            int x = idx % width;
            int y = idx / width;
            area++;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            TryPush(idx - 1, x > 0);
            TryPush(idx + 1, x < width - 1);
            TryPush(idx - width, y > 0);
            TryPush(idx + width, y < height - 1);
        }

        return (minX, minY, maxX - minX + 1, maxY - minY + 1, area);

        void TryPush(int next, bool inside)
        {
            if (!inside || visited[next] || !mask[next] || tail >= queue.Length)
                return;
            visited[next] = true;
            queue[tail++] = next;
        }
    }

    private static bool LooksLikeRectangleBorder(bool[] mask, int width, (int X, int Y, int W, int H, int Area) c)
    {
        int top = 0, bottom = 0, left = 0, right = 0;
        for (int x = c.X; x < c.X + c.W; x++)
        {
            if (mask[c.Y * width + x]) top++;
            if (mask[(c.Y + c.H - 1) * width + x]) bottom++;
        }
        for (int y = c.Y; y < c.Y + c.H; y++)
        {
            if (mask[y * width + c.X]) left++;
            if (mask[y * width + c.X + c.W - 1]) right++;
        }
        double edge = (top + bottom + left + right) / (double)Math.Max(1, c.W * 2 + c.H * 2);
        double filled = c.Area / (double)Math.Max(1, c.W * c.H);
        return edge >= 0.45 && filled <= 0.45;
    }

    private (int MobId, string Name, float Score)? DecodeMob(byte[] bytes, int stride, int width, int height, int boxX, int boxY)
    {
        var sampled = new int[9];
        for (int i = 0; i < 3; i++)
        {
            int cx = boxX + _boxPx + i * _codeCellPx + _codeCellPx / 2;
            int cy = boxY + _boxPx + _codeCellPx / 2;
            if (cx < 0 || cy < 0 || cx >= width || cy >= height)
                return null;
            var rgb = SampleMedianRgb(bytes, stride, width, height, cx, cy);
            sampled[i * 3 + 0] = rgb.R;
            sampled[i * 3 + 1] = rgb.G;
            sampled[i * 3 + 2] = rgb.B;
        }

        MobCode? best = null;
        double bestScore = double.MaxValue;
        foreach (var code in _codes)
        {
            double score = 0;
            for (int i = 0; i < 9; i += 3)
                score += NormalizedRgbDistance(sampled, code.Rgb, i);
            if (score < bestScore)
            {
                bestScore = score;
                best = code;
            }
        }

        if (best == null || bestScore > 1.45)
            return null;
        float confidence = (float)Math.Clamp(1.0 - bestScore / 1.45, 0.05, 1.0);
        return (best.MobId, best.Name, confidence);
    }

    private static (int R, int G, int B) SampleMedianRgb(byte[] bytes, int stride, int width, int height, int cx, int cy)
    {
        Span<int> rs = stackalloc int[9];
        Span<int> gs = stackalloc int[9];
        Span<int> bs = stackalloc int[9];
        int count = 0;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(height - 1, cy + 1); y++)
        {
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(width - 1, cx + 1); x++)
            {
                int p = y * stride + x * 4;
                bs[count] = bytes[p + 0];
                gs[count] = bytes[p + 1];
                rs[count] = bytes[p + 2];
                count++;
            }
        }

        rs[..count].Sort();
        gs[..count].Sort();
        bs[..count].Sort();
        int mid = count / 2;
        return (rs[mid], gs[mid], bs[mid]);
    }

    private static int TryInt(JsonElement root, string property, int fallback)
    {
        try
        {
            if (root.TryGetProperty(property, out var value) && value.TryGetInt32(out int n))
                return Math.Clamp(n, 1, 64);
        }
        catch { }
        return fallback;
    }

    private static double NormalizedRgbDistance(int[] a, int[] b, int offset)
    {
        double am = Math.Max(1, Math.Max(a[offset], Math.Max(a[offset + 1], a[offset + 2])));
        double bm = Math.Max(1, Math.Max(b[offset], Math.Max(b[offset + 1], b[offset + 2])));
        double dr = a[offset] / am - b[offset] / bm;
        double dg = a[offset + 1] / am - b[offset + 1] / bm;
        double db = a[offset + 2] / am - b[offset + 2] / bm;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static IReadOnlyList<VisionAssistMarker> Dedup(List<VisionAssistMarker> markers)
    {
        if (markers.Count <= 1)
            return markers;
        var ordered = markers.OrderByDescending(m => m.Score).ThenByDescending(m => m.W * m.H).ToList();
        var kept = new List<VisionAssistMarker>();
        foreach (var m in ordered)
        {
            if (kept.Any(k => IoU(k, m) > 0.45))
                continue;
            kept.Add(m);
        }
        return kept.OrderBy(m => m.Y).ThenBy(m => m.X).ToList();
    }

    private static double IoU(VisionAssistMarker a, VisionAssistMarker b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.W, b.X + b.W), y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        int iw = Math.Max(0, x2 - x1), ih = Math.Max(0, y2 - y1);
        int inter = iw * ih;
        int union = a.W * a.H + b.W * b.H - inter;
        return union <= 0 ? 0 : inter / (double)union;
    }
}
