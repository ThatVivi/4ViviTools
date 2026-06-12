using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Base for all automation: one async loop, cancellation, status callback.</summary>
public abstract class AutomationEngine
{
    protected readonly GameSession Session;
    protected readonly KeySender Keys;
    protected readonly HumanizedTiming Timing;
    private CancellationTokenSource? _cts;

    public bool Running => _cts is { IsCancellationRequested: false };
    public event Action<string>? Status;
    public string Name { get; }

    protected AutomationEngine(string name, GameSession session, KeySender keys, HumanizedTiming timing)
    { Name = name; Session = session; Keys = keys; Timing = timing; }

    public void Start()
    {
        if (Running) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            Report(Name + " started.");
            try { await LoopAsync(ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Report(Name + " error: " + ex.Message); }
            Report(Name + " stopped.");
        }, ct);
    }

    public void Stop() { try { _cts?.Cancel(); } catch { } }
    protected void Report(string s) => Status?.Invoke(s);
    protected IntPtr Hwnd => Session.WindowHandle;

    protected abstract Task LoopAsync(CancellationToken ct);
}
