using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;

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
    private string _tmp = Path.Combine(Path.GetTempPath(), "4rvivi_ocr.png");
    private string _tmpIcon = Path.Combine(Path.GetTempPath(), "4rvivi_icon.png");
    private string _tmpDet = Path.Combine(Path.GetTempPath(), "4rvivi_det.png");
    private string _tmpScan = Path.Combine(Path.GetTempPath(), "4rvivi_scan.png");

    public bool Available => !_failed;
    public float LastScore { get; private set; } = 1f;   // rec confidence of the last single-region Recognize

    private static string? FindServer()
    {
        var dirs = new List<string>();
        try { var p = Environment.ProcessPath; if (!string.IsNullOrEmpty(p)) dirs.Add(Path.GetDirectoryName(p)!); } catch { }  // real exe dir (single-file safe)
        dirs.Add(AppContext.BaseDirectory);
        foreach (var d in dirs)
            foreach (var c in new[] { Path.Combine(d, "OcrServer", "4rVivi.OcrServer.exe"), Path.Combine(d, "4rVivi.OcrServer.exe") })
                if (File.Exists(c)) return c;
        return null;
    }

    private bool EnsureStarted()
    {
        if (_proc is { HasExited: false }) return true;
        if (_failed) return false;
        try
        {
            var exe = FindServer();
            if (exe == null) { _failed = true; return false; }
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
            _proc.Start();
            if (_pendingCfg != null)
                try { _proc.StandardInput.WriteLine("CFG\t" + _pendingCfg); _proc.StandardInput.Flush(); } catch { }
            return true;
        }
        catch { _failed = true; return false; }
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
                string? resp = _proc.StandardOutput.ReadLine();
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
                string? resp = _proc.StandardOutput.ReadLine();
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
                string? resp = _proc.StandardOutput.ReadLine();
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
                string? resp = _proc.StandardOutput.ReadLine();
                if (resp == null) { _failed = true; return list; }
                int tab = resp.IndexOf('\t');
                if (!resp.StartsWith("OK") || tab < 0) return list;
                foreach (var e in resp.Substring(tab + 1).Split(';'))
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
            catch { _failed = true; return list; }
        }
    }

    /// <summary>A detected on-screen entity: pixel box + (best icon-embedder label, its cosine score).</summary>
    public readonly record struct Entity(int X, int Y, int W, int H, float Score, string Label, float LabelScore, string Cls);

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
                string? resp = _proc.StandardOutput.ReadLine();
                if (resp == null) { _failed = true; return list; }
                int tab = resp.IndexOf('\t');
                if (!resp.StartsWith("OK") || tab < 0) return list;
                foreach (var e in resp.Substring(tab + 1).Split(';'))
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
            catch { _failed = true; return list; }
        }
    }

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

    /// <summary>Kill the worker so the next call starts fresh (e.g. after a new model is installed).</summary>
    public void Restart()
    {
        lock (_lock)
        {
            try { if (_proc is { HasExited: false }) { _proc.StandardInput.WriteLine("QUIT"); _proc.WaitForExit(800); } } catch { }
            try { _proc?.Kill(); } catch { }
            _proc = null; _failed = false;
        }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) { _proc.StandardInput.WriteLine("QUIT"); _proc.WaitForExit(800); } } catch { }
        try { _proc?.Kill(); } catch { }
        _proc = null;
    }
}
