using FourRVivi.Core.Common;
using FourRVivi.Core.Game;

namespace FourRVivi.App.Services;

/// <summary>Lists candidate game windows and attaches the shared session to one.</summary>
public sealed class ProcessService
{
    private readonly ProcessWatcher _watcher;
    private readonly GameSession _session;

    public ProcessService(ProcessWatcher watcher, GameSession session)
    { _watcher = watcher; _session = session; }

    public IReadOnlyList<GameProcess> List(IEnumerable<string>? prefer = null) => _watcher.List(prefer);

    public OpResult Attach(GameProcess gp)
    {
        var p = _watcher.Open(gp.Pid);
        if (p is null) return OpResult.Fail("Process is gone — refresh the list.");
        return _session.Attach(p);
    }
}
