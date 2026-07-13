using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using FourRVivi.Core.Common;

namespace FourRVivi.App.Services;

/// <summary>Talks to the out-of-process PaddleOCR worker (4rVivi.OcrServer) over stdio, so its
/// SkiaSharp 3 / ONNX never clash with the app's Avalonia. Lazy start; returns null on any failure
/// so OcrService falls back to Windows OCR / Tesseract.</summary>
public sealed class RapidOcrClient : IDisposable
{
    private readonly object _lock = new();
    private Process? _proc;
    private bool _failed;
    private string? _pendingCfg;
    private readonly string _tmpBase = Path.Combine(Path.GetTempPath(), "4rvivi_" + Guid.NewGuid().ToString("N"));
    private readonly string _tmp;
    private readonly string _tmpIcon;
    private readonly string _tmpDet;
    private readonly string _tmpScan;
    private readonly string _tmpBoth;
    private MemoryMappedFile? _rawFrameMap;
    private MemoryMappedViewAccessor? _rawFrameAccessor;
    private string _rawFrameName = "";
    private int _rawFrameCapacity;
    private const int RecTimeoutMs = 3500;
    private const int IconTimeoutMs = 2500;
    private const int TextScanTimeoutMs = 8000;
    private const int DetectTimeoutMs = 4500;

    public RapidOcrClient()
    {
        _tmp = _tmpBase + "_ocr.png";
        _tmpIcon = _tmpBase + "_icon.png";
        _tmpDet = _tmpBase + "_det.png";
        _tmpScan = _tmpBase + "_scan.png";
        _tmpBoth = _tmpBase + "_both.png";
    }

    public bool Available => !_failed;
    public float LastScore { get; private set; } = 1f;   // rec confidence of the last single-region Recognize
    public string LastRuntimeProvider { get; private set; } = "unknown";

    private static string? FindServer()
    {
        var dirs = new List<string>();
        try { var p = Environment.ProcessPath; if (!string.IsNullOrEmpty(p)) dirs.Add(Path.GetDirectoryName(p)!); } catch { }  // real exe dir (single-file safe)
        dirs.Add(AppContext.BaseDirectory);
        bool cuda = NormalizeRequestedProvider() == "cuda";
        foreach (var d in dirs)
        {
            var candidates = cuda
                ? new[] { Path.Combine(d, "OcrServerCuda", "4rVivi.OcrServer.exe"), Path.Combine(d, "OcrServer", "4rVivi.OcrServer.exe"), Path.Combine(d, "4rVivi.OcrServer.exe") }
                : new[] { Path.Combine(d, "OcrServer", "4rVivi.OcrServer.exe"), Path.Combine(d, "OcrServerDirectML", "4rVivi.OcrServer.exe"), Path.Combine(d, "4rVivi.OcrServer.exe") };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }
        return null;
    }

    private bool EnsureStarted()
    {
        if (_proc is { HasExited: false }) return true;
        if (_failed) return false;
        try
        {
            var exe = FindServer();
            if (exe == null)
            {
                DebugTrace.Write("OCR", "OcrServer executable not found.");
                _failed = true;
                return false;
            }
            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                }
            };
            _proc.StartInfo.Environment["PATH"] = BuildWorkerPath();
            var cudaRoot = ResolveCudaRoot();
            if (!string.IsNullOrWhiteSpace(cudaRoot))
                _proc.StartInfo.Environment["CUDA_PATH"] = cudaRoot;
            _proc.Start();
            DebugTrace.Write("OCR", $"Started OcrServer pid={_proc.Id} path='{exe}'.");
            if (_pendingCfg != null)
                try { _proc.StandardInput.WriteLine("CFG\t" + _pendingCfg); _proc.StandardInput.Flush(); } catch { }
            return true;
        }
        catch (Exception ex) { DebugTrace.Write("OCR", "Failed to start OcrServer.", ex); _failed = true; return false; }
    }

    /// <summary>Recognize a preprocessed PNG. Returns null if the worker isn't available.</summary>
    public string GetRuntimeInfo()
    {
        lock (_lock)
        {
            if (!EnsureStarted()) return LastRuntimeProvider;
            try
            {
                _proc!.StandardInput.WriteLine("INFO");
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("INFO", 1500);
                if (resp != null && resp.StartsWith("OK\t", StringComparison.Ordinal))
                    LastRuntimeProvider = NormalizeRuntimeProvider(resp.Substring(3).Trim());
            }
            catch { }
            return LastRuntimeProvider;
        }
    }

    private static string NormalizeRuntimeProvider(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";
        raw = raw.Trim();
        if (!raw.All(char.IsDigit))
            return raw;

        var requested = (Environment.GetEnvironmentVariable("OCR_ONNX_EP") ?? "auto").Trim().ToLowerInvariant();
        if (requested is "cuda" or "nvidia")
            return "CUDA device " + raw;
        if (requested is "directml" or "dml" or "amd" or "amd/directml" or "auto")
            return "DirectML";
        return "CPU";
    }

    private static string NormalizeRequestedProvider()
    {
        var requested = (Environment.GetEnvironmentVariable("OCR_ONNX_EP") ?? "auto").Trim().ToLowerInvariant();
        return requested is "cuda" or "nvidia" ? "cuda"
            : requested is "directml" or "dml" or "amd" or "amd/directml" ? "directml"
            : requested == "cpu" ? "cpu"
            : "auto";
    }

    private static string BuildWorkerPath()
    {
        var parts = new List<string>();
        string? cudaRoot = ResolveCudaRoot();
        if (!string.IsNullOrWhiteSpace(cudaRoot))
            AddIfDirectory(parts, Path.Combine(cudaRoot, "bin"));

        foreach (var dir in ResolveCudnnBins())
            AddIfDirectory(parts, dir);

        var existing = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in existing.Split(Path.PathSeparator))
            AddIfDirectory(parts, dir);
        return string.Join(Path.PathSeparator, parts);
    }

    private static string? ResolveCudaRoot()
    {
        var env = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        const string root = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        try
        {
            if (Directory.Exists(root))
                return Directory.GetDirectories(root, "v12.*")
                    .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
        }
        catch { }
        return null;
    }

    private static IEnumerable<string> ResolveCudnnBins()
    {
        const string root = @"C:\Program Files\NVIDIA\CUDNN";
        if (!Directory.Exists(root))
            yield break;

        IEnumerable<string> dirs;
        try { dirs = Directory.GetDirectories(root, "v9.*").OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            var bin = Path.Combine(dir, "bin");
            if (Directory.Exists(bin))
            {
                foreach (var child in Directory.GetDirectories(bin, "*", SearchOption.AllDirectories)
                             .Where(d => d.EndsWith(@"\x64", StringComparison.OrdinalIgnoreCase)))
                    yield return child;
                yield return bin;
            }
        }
    }

    private static void AddIfDirectory(List<string> parts, string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;
        string full;
        try { full = Path.GetFullPath(dir.Trim()); }
        catch { return; }
        if (!parts.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)))
            parts.Add(full);
    }

    /// <summary>Recognize a preprocessed PNG. Returns null if the worker isn't available.</summary>
    public string? Recognize(byte[] png)
    {
        if (png.Length == 0) return null;
        lock (_lock)
        {
            if (!EnsureStarted()) return null;
            try
            {
                File.WriteAllBytes(_tmp, png);
                _proc!.StandardInput.WriteLine(_tmp);
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("OCR", RecTimeoutMs);
                if (resp == null) { _failed = true; return null; }
                if (resp.StartsWith("OK\t"))
                {
                    var rest = resp.Substring(3);
                    int tab2 = rest.IndexOf('\t');
                    if (tab2 >= 0 && float.TryParse(rest.Substring(0, tab2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sc))
                    { LastScore = sc; return rest.Substring(tab2 + 1); }
                    LastScore = 1f; return rest;   // old single-field format
                }
                return null;
            }
            catch { _failed = true; return null; }
        }
    }

    /// <summary>Recognition-only read of a single text-line crop (REC): skips detection + angle. Returns
    /// the text and sets <see cref="LastScore"/>, or null if the worker is down.</summary>
    public string? RecognizeLine(byte[] png)
    {
        if (png.Length == 0) return null;
        lock (_lock)
        {
            if (!EnsureStarted()) return null;
            try
            {
                File.WriteAllBytes(_tmp, png);
                _proc!.StandardInput.WriteLine("REC\t" + _tmp);
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("REC", RecTimeoutMs);
                if (resp == null) { _failed = true; return null; }
                if (resp.StartsWith("OK\t"))
                {
                    var rest = resp.Substring(3);
                    int tab = rest.IndexOf('\t');
                    if (tab >= 0 && float.TryParse(rest.Substring(0, tab), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sc))
                    { LastScore = sc; return rest.Substring(tab + 1); }
                    LastScore = 1f; return rest;
                }
                return null;
            }
            catch { _failed = true; return null; }
        }
    }

    /// <summary>Recognize a game icon/sprite/minimap crop -> (label, cosine score), or null.</summary>
    public (string label, float score)? RecognizeIcon(byte[] png)
    {
        if (png.Length == 0) return null;
        lock (_lock)
        {
            if (!EnsureStarted()) return null;
            try
            {
                File.WriteAllBytes(_tmpIcon, png);
                _proc!.StandardInput.WriteLine("ICON\t" + _tmpIcon);
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("ICON", IconTimeoutMs);
                if (resp == null) { _failed = true; return null; }
                var parts = resp.Split('\t');
                if (parts.Length >= 3 && parts[0] == "OK" && parts[1].Length > 0 &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sc))
                    return (parts[1], sc);
                return null;
            }
            catch { _failed = true; return null; }
        }
    }

    /// <summary>A piece of text found anywhere on screen: pixel box + decoded string + det score.</summary>
    public readonly record struct TextFind(int X, int Y, int W, int H, float Score, string Text);
    private readonly record struct RawFrameLease(string Name, int Width, int Height, int Stride, int Length) : IDisposable
    {
        public void Dispose() { }
    }

    private static List<TextFind> ParseTextFinds(string payload)
    {
        var list = new List<TextFind>();
        foreach (var e in payload.Split(';'))
        {
            if (e.Length == 0) continue;
            var f = e.Split(',');
            if (f.Length < 6) continue;
            if (int.TryParse(f[0], out var x) && int.TryParse(f[1], out var y) &&
                int.TryParse(f[2], out var w) && int.TryParse(f[3], out var h) &&
                float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var sc))
            {
                string txt = "";
                try { txt = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(f[5])); } catch { }
                list.Add(new TextFind(x, y, w, h, sc, txt));
            }
        }
        return list;
    }

    /// <summary>Full-screen text detection+recognition. Finds and reads ALL text in the image
    /// (not just a user region). Returns [] if the worker/model is unavailable.</summary>
    public IReadOnlyList<TextFind> ScanText(byte[] png)
    {
        var list = new List<TextFind>();
        if (png.Length == 0) return list;
        lock (_lock)
        {
            if (!EnsureStarted()) return list;
            try
            {
                File.WriteAllBytes(_tmpScan, png);
                _proc!.StandardInput.WriteLine("SCAN\t" + _tmpScan);
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("SCAN", TextScanTimeoutMs);
                if (resp == null) { _failed = true; return list; }
                int tab = resp.IndexOf('\t');
                if (!resp.StartsWith("OK") || tab < 0) return list;
                return ParseTextFinds(resp.Substring(tab + 1));
            }
            catch { _failed = true; return list; }
        }
    }

    public IReadOnlyList<TextFind>? ScanTextRaw(Bitmap bitmap)
    {
        lock (_lock)
        {
            if (!EnsureStarted()) return null;
            var rawLease = CreateRawFrame(bitmap);
            if (rawLease == null) return null;
            using var raw = rawLease.Value;
            try
            {
                _proc!.StandardInput.WriteLine($"RAW_SCAN\t{raw.Name}\t{raw.Width}\t{raw.Height}\t{raw.Stride}\t{raw.Length}");
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("RAW_SCAN", TextScanTimeoutMs);
                if (resp == null) return null;
                int tab = resp.IndexOf('\t');
                if (!resp.StartsWith("OK") || tab < 0) return null;
                return ParseTextFinds(resp.Substring(tab + 1));
            }
            catch { return null; }
        }
    }

    /// <summary>A detected on-screen entity: pixel box + (best icon-embedder label, its cosine score).</summary>
    public readonly record struct Entity(int X, int Y, int W, int H, float Score, string Label, float LabelScore, string Cls);

    private static List<Entity> ParseEntities(string payload)
    {
        var list = new List<Entity>();
        foreach (var e in payload.Split(';'))
        {
            if (e.Length == 0) continue;
            var f = e.Split(',');
            if (f.Length < 7) continue;
            if (int.TryParse(f[0], out var x) && int.TryParse(f[1], out var y) &&
                int.TryParse(f[2], out var w) && int.TryParse(f[3], out var h) &&
                float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) &&
                float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var ls))
            {
                string cls = f.Length >= 8 ? f[7] : "";
                list.Add(new Entity(x, y, w, h, sc, f[5], ls, cls));
            }
        }
        return list;
    }

    /// <summary>Detect entities on a full screenshot PNG. Returns [] if the worker/model is unavailable.</summary>
    public IReadOnlyList<Entity> DetectEntities(byte[] png)
    {
        var list = new List<Entity>();
        if (png.Length == 0) return list;
        lock (_lock)
        {
            if (!EnsureStarted()) return list;
            try
            {
                File.WriteAllBytes(_tmpDet, png);
                _proc!.StandardInput.WriteLine("DETECT\t" + _tmpDet);
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("DETECT", DetectTimeoutMs);
                if (resp == null) { _failed = true; return list; }
                int tab = resp.IndexOf('\t');
                if (!resp.StartsWith("OK") || tab < 0) return list;
                return ParseEntities(resp.Substring(tab + 1));
            }
            catch { _failed = true; return list; }
        }
    }

    public IReadOnlyList<Entity>? DetectEntitiesRaw(Bitmap bitmap)
    {
        lock (_lock)
        {
            if (!EnsureStarted()) return null;
            var rawLease = CreateRawFrame(bitmap);
            if (rawLease == null) return null;
            using var raw = rawLease.Value;
            try
            {
                _proc!.StandardInput.WriteLine($"RAW_DETECT\t{raw.Name}\t{raw.Width}\t{raw.Height}\t{raw.Stride}\t{raw.Length}");
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("RAW_DETECT", DetectTimeoutMs);
                if (resp == null) return null;
                int tab = resp.IndexOf('\t');
                if (!resp.StartsWith("OK") || tab < 0) return null;
                return ParseEntities(resp.Substring(tab + 1));
            }
            catch { return null; }
        }
    }

    /// <summary>Combined full-frame text + entity scan. Saves one worker round trip and one image decode.</summary>
    public (IReadOnlyList<TextFind> Texts, IReadOnlyList<Entity> Entities) ScanTextAndEntities(byte[] png)
    {
        if (png.Length == 0) return (Array.Empty<TextFind>(), Array.Empty<Entity>());
        lock (_lock)
        {
            if (!EnsureStarted()) return (Array.Empty<TextFind>(), Array.Empty<Entity>());
            try
            {
                File.WriteAllBytes(_tmpBoth, png);
                _proc!.StandardInput.WriteLine("BOTH\t" + _tmpBoth);
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("BOTH", TextScanTimeoutMs + DetectTimeoutMs);
                if (resp == null) { _failed = true; return (Array.Empty<TextFind>(), Array.Empty<Entity>()); }
                var parts = resp.Split('\t');
                if (parts.Length < 3 || parts[0] != "OK") return (Array.Empty<TextFind>(), Array.Empty<Entity>());
                return (ParseTextFinds(parts[1]), ParseEntities(parts[2]));
            }
            catch { _failed = true; return (Array.Empty<TextFind>(), Array.Empty<Entity>()); }
        }
    }

    public (IReadOnlyList<TextFind> Texts, IReadOnlyList<Entity> Entities)? ScanTextAndEntitiesRaw(Bitmap bitmap)
    {
        lock (_lock)
        {
            if (!EnsureStarted()) return null;
            var rawLease = CreateRawFrame(bitmap);
            if (rawLease == null) return null;
            using var raw = rawLease.Value;
            try
            {
                _proc!.StandardInput.WriteLine($"RAW_BOTH\t{raw.Name}\t{raw.Width}\t{raw.Height}\t{raw.Stride}\t{raw.Length}");
                _proc.StandardInput.Flush();
                string? resp = ReadLineWithTimeout("RAW_BOTH", TextScanTimeoutMs + DetectTimeoutMs);
                if (resp == null) return null;
                var parts = resp.Split('\t');
                if (parts.Length < 3 || parts[0] != "OK") return null;
                return (ParseTextFinds(parts[1]), ParseEntities(parts[2]));
            }
            catch { return null; }
        }
    }

    private unsafe RawFrameLease? CreateRawFrame(Bitmap source)
    {
        if (source == null || source.Width <= 0 || source.Height <= 0)
            return null;

        Bitmap? clone = null;
        Bitmap? bitmap = null;
        BitmapData? data = null;
        try
        {
            int width = source.Width;
            int height = source.Height;
            int stride = checked(width * 4);
            int length = checked(stride * height);
            bitmap = IsRawCompatible(source.PixelFormat) ? source : null;
            if (bitmap == null)
            {
                clone = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(clone))
                    g.DrawImageUnscaled(source, 0, 0);
                bitmap = clone;
            }

            data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            int sourceStride = data.Stride;
            int rowBytes = Math.Min(stride, Math.Abs(sourceStride));
            EnsureRawFrameMap(length);
            var accessor = _rawFrameAccessor!;
            byte* dest = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref dest);
            try
            {
                dest += accessor.PointerOffset;
                for (int y = 0; y < height; y++)
                {
                    int srcOffset = sourceStride < 0 ? (height - 1 - y) * -sourceStride : y * sourceStride;
                    Buffer.MemoryCopy(
                        source: (byte*)data.Scan0 + srcOffset,
                        destination: dest + (y * stride),
                        destinationSizeInBytes: rowBytes,
                        sourceBytesToCopy: rowBytes);
                }
            }
            finally
            {
                accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            return new RawFrameLease(_rawFrameName, width, height, stride, length);
        }
        catch (Exception ex)
        {
            DebugTrace.Write("OCR", "Raw frame memory-map creation failed; PNG fallback will be used.", ex);
            return null;
        }
        finally
        {
            if (bitmap != null && data != null)
                try { bitmap.UnlockBits(data); } catch { }
            clone?.Dispose();
        }
    }

    private void EnsureRawFrameMap(int requiredLength)
    {
        if (_rawFrameMap != null && _rawFrameCapacity >= requiredLength)
            return;

        try { _rawFrameAccessor?.Dispose(); } catch { }
        try { _rawFrameMap?.Dispose(); } catch { }
        _rawFrameAccessor = null;
        _rawFrameMap = null;
        _rawFrameCapacity = Math.Max(requiredLength, _rawFrameCapacity * 2);
        if (_rawFrameCapacity <= 0)
            _rawFrameCapacity = requiredLength;
        _rawFrameName = "4rvivi_raw_" + Guid.NewGuid().ToString("N");
        _rawFrameMap = MemoryMappedFile.CreateNew(_rawFrameName, _rawFrameCapacity, MemoryMappedFileAccess.ReadWrite);
        _rawFrameAccessor = _rawFrameMap.CreateViewAccessor(0, _rawFrameCapacity, MemoryMappedFileAccess.Write);
    }

    private static bool IsRawCompatible(PixelFormat pixelFormat)
        => pixelFormat is PixelFormat.Format32bppArgb
        or PixelFormat.Format32bppPArgb
        or PixelFormat.Format32bppRgb;

    /// <summary>Queue a CFG line; sent on next worker start and immediately if running.</summary>
    public void SendConfig(string cfg)
    {
        lock (_lock)
        {
            _pendingCfg = cfg;
            if (_proc is { HasExited: false })
                try { _proc.StandardInput.WriteLine("CFG\t" + cfg); _proc.StandardInput.Flush(); } catch { _failed = true; }
        }
    }

    private string? ReadLineWithTimeout(string op, int timeoutMs)
    {
        if (_proc == null)
            return null;

        try
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (true)
            {
                int remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
                var task = _proc.StandardOutput.ReadLineAsync();
                if (task.Wait(remaining))
                {
                    var line = task.Result;
                    if (line == null)
                        return null;
                    if (line.StartsWith("OK", StringComparison.Ordinal) || line.StartsWith("ERR", StringComparison.Ordinal))
                        return line;
                    DebugTrace.Write("OCR", $"{op} ignored non-protocol worker output: {line}");
                    if (Environment.TickCount64 < deadline)
                        continue;
                }
                break;
            }

            DebugTrace.Write("OCR", $"{op} timed out after {timeoutMs} ms; restarting OcrServer.");
            RestartNoLock();
            return null;
        }
        catch (Exception ex)
        {
            DebugTrace.Write("OCR", $"{op} read failed; restarting OcrServer.", ex);
            RestartNoLock();
            return null;
        }
    }

    private void RestartNoLock()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
        try { _proc?.Dispose(); } catch { }
        _proc = null;
        _failed = false;
    }

    /// <summary>Kill the worker so the next call starts fresh (e.g. after a new model is installed).</summary>
    public void Restart()
    {
        lock (_lock)
        {
            try { if (_proc is { HasExited: false }) { _proc.StandardInput.WriteLine("QUIT"); _proc.WaitForExit(800); } } catch { }
            RestartNoLock();
        }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) { _proc.StandardInput.WriteLine("QUIT"); _proc.WaitForExit(800); } } catch { }
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { try { _proc?.Kill(); } catch { } }
        try { _proc?.Dispose(); } catch { }
        try { _rawFrameAccessor?.Dispose(); } catch { }
        try { _rawFrameMap?.Dispose(); } catch { }
        foreach (var path in new[] { _tmp, _tmpIcon, _tmpDet, _tmpScan, _tmpBoth })
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        _rawFrameAccessor = null;
        _rawFrameMap = null;
        _proc = null;
    }
}
