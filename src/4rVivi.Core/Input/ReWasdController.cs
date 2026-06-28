using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace FourRVivi.Core.Input;

public sealed class ReWasdController : IDisposable
{
    private readonly object _lock = new();

    private ViGEmClient? _client;
    private IXbox360Controller? _controller;

    public int TapDurationMs { get; set; } = 45;

    public bool IsReWasdRunning()
    {
        return Process.GetProcessesByName("reWASD").Any()
            || Process.GetProcessesByName("reWASDService").Any()
            || Process.GetProcessesByName("reWASDUI").Any();
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
                return true;
            }
            catch
            {
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

        SetButton(button, true);
        Thread.Sleep(duration);
        SetButton(button, false);
    }

    public void LeftClick(int holdMs = 0)
    {
        Tap(ReWasdMouseMap.LeftClickButton, holdMs);
    }

    public void RightClick(int holdMs = 0)
    {
        Tap(ReWasdMouseMap.RightClickButton, holdMs);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
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
