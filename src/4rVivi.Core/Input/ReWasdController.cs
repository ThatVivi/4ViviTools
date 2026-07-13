using System.Diagnostics;
using FourRVivi.Core.Common;
using Microsoft.Win32;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace FourRVivi.Core.Input;

public sealed class ReWasdController : IDisposable
{
    private readonly object _lock = new();

    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private string _leftClickButtonName = "A";
    private string _rightClickButtonName = "B";

    public int TapDurationMs { get; set; } = 100;

    public string LeftClickButtonName
    {
        get => _leftClickButtonName;
        set => _leftClickButtonName = ReWasdMouseMap.NormalizeName(value);
    }

    public string RightClickButtonName
    {
        get => _rightClickButtonName;
        set => _rightClickButtonName = ReWasdMouseMap.NormalizeName(value);
    }

    public bool IsReWasdRunning()
    {
        return Process.GetProcessesByName("reWASD").Any()
            || Process.GetProcessesByName("reWASDService").Any()
            || Process.GetProcessesByName("reWASDUI").Any();
    }

    public bool IsVirtualDriverInstalled()
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null)
                return false;

            foreach (var name in services.GetSubKeyNames())
                if (name.Contains("ViGEm", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("VirtualGamepad", StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch
        {
        }

        return false;
    }

    public bool IsVirtualDriverReady()
    {
        lock (_lock)
            return _controller != null;
    }

    public bool EnsureConnected()
    {
        lock (_lock)
        {
            if (_controller != null)
                return true;

            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.Connect();
                ReleaseAllButtons();
                Thread.Sleep(120);
                DebugTrace.Write("ViGEm", "Connected virtual Xbox360 controller.");
                return true;
            }
            catch (Exception ex)
            {
                DebugTrace.Write("ViGEm", "Failed to connect virtual Xbox360 controller.", ex);
                _controller = null;
                _client?.Dispose();
                _client = null;
                return false;
            }
        }
    }

    public void SetButton(Xbox360Button button, bool pressed)
    {
        lock (_lock)
        {
            if (!EnsureConnected())
                return;

            _controller!.SetButtonState(button, pressed);
        }
    }

    public void Tap(Xbox360Button button, int holdMs = 0)
    {
        int duration = Math.Max(30, holdMs > 0 ? holdMs : TapDurationMs);

        try
        {
            DebugTrace.Write("ViGEm", $"Tap {button} down duration={duration}ms.");
            SetButton(button, true);
            Thread.Sleep(duration);
        }
        finally
        {
            SetButton(button, false);
            DebugTrace.Write("ViGEm", $"Tap {button} up.");
        }
    }

    public void LeftClick(int holdMs = 0)
    {
        Tap(ReWasdMouseMap.FromName(LeftClickButtonName), holdMs);
    }

    public void RightClick(int holdMs = 0)
    {
        Tap(ReWasdMouseMap.FromName(RightClickButtonName), holdMs);
    }

    public void ReleaseAllButtons()
    {
        if (_controller == null)
            return;

        foreach (var button in ReWasdMouseMap.AllButtons())
            _controller.SetButtonState(button, false);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                ReleaseAllButtons();
                _controller?.Disconnect();
            }
            catch
            {
            }

            _client?.Dispose();
            _controller = null;
            _client = null;
        }
    }
}
