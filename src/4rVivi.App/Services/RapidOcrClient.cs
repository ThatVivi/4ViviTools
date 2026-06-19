using System.Diagnostics;
using System.IO;

namespace FourRVivi.App.Services;

/// <summary>Talks to the out-of-process PaddleOCR worker (4rVivi.OcrServer) over stdio, so its
/// SkiaSharp 3 / ONNX never clash with the app's Avalonia. Lazy start; returns null on any failure
/// so OcrService falls back to Windows OCR / Tesseract.</summary>
public sealed class RapidOcrClient : IDisposable
{
    private readonly object _lock = new();
    private Process? _proc;
    private bool _failed;
    private string _tmp = Path.Combine(Path.GetTempPath(), "4rvivi_ocr.png");

    public bool Available => !_failed;

    private static string? FindServer()
    {
        string b = AppContext.BaseDirectory;
        foreach (var p in new[]
        {
            Path.Combine(b, "OcrServer", "4rVivi.OcrServer.exe"),
            Path.Combine(b, "4rVivi.OcrServer.exe"),
        })
            if (File.Exists(p)) return p;
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
                int tab = resp.IndexOf('\t');
                if (resp.StartsWith("OK") && tab >= 0) return resp.Substring(tab + 1);
                return null;
            }
            catch { _failed = true; return null; }
        }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) { _proc.StandardInput.WriteLine("QUIT"); _proc.WaitForExit(800); } } catch { }
        try { _proc?.Kill(); } catch { }
        _proc = null;
    }
}
