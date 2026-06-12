using FourRVivi.Core.Input;

namespace FourRVivi.Core.Macros;

/// <summary>Plays a recorded macro to a target window. Useful for login / reconnect sequences.</summary>
public sealed class MacroPlayer
{
    private readonly KeySender _keys = new();

    public async Task PlayAsync(IntPtr hwnd, MacroRecording rec, CancellationToken ct = default)
    {
        foreach (var s in rec.Steps)
        {
            ct.ThrowIfCancellationRequested();
            _keys.Tap(hwnd, s.Vk, s.HoldMs);
            await Task.Delay(s.GapMs, ct);
        }
    }
}
