using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FourRVivi.App.Services;

/// <summary>Runs the bundled Python trainer (tools/ocr-train/run.py) as a child process and streams
/// its stdout/stderr line-by-line. Cancellable. Returns true only on exit code 0.</summary>
public sealed class OcrTrainerRunner
{
    public event Action<string>? Line;
    private Process? _proc;

    public bool Running => _proc is { HasExited: false };

    public string UserImagesDir()
    {
        var dir = Path.Combine(OcrTemplateExporter.ToolDir(), "user_images");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<bool> RunAsync(CancellationToken ct)
    {
        var tool = OcrTemplateExporter.ToolDir();
        var py = OperatingSystem.IsWindows() ? "python" : "python3";
        var psi = new ProcessStartInfo
        {
            FileName = py,
            Arguments = "run.py",
            WorkingDirectory = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try { _proc = Process.Start(psi); }
        catch (Exception e) { Line?.Invoke("ERROR: Python not found — install Python 3.10+. " + e.Message); return false; }
        if (_proc == null) { Line?.Invoke("ERROR: could not start trainer."); return false; }

        _proc.OutputDataReceived += (_, e) => { if (e.Data != null) Line?.Invoke(e.Data); };
        _proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Line?.Invoke(e.Data); };
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        using (ct.Register(() => { try { _proc.Kill(true); } catch { } }))
        {
            try { await _proc.WaitForExitAsync(ct); }
            catch (OperationCanceledException) { return false; }
        }
        return _proc.ExitCode == 0;
    }
}
