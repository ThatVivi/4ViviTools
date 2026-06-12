using System.Diagnostics;

namespace FourRVivi.Core.Game;

/// <summary>Lists candidate game windows. RO client names vary, so we list every visible window.</summary>
public sealed class ProcessWatcher
{
    public IReadOnlyList<GameProcess> List(IEnumerable<string>? preferredNames = null)
    {
        var prefer = preferredNames?.Select(n => n.ToLowerInvariant()).ToHashSet() ?? new();
        var list = new List<GameProcess>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                list.Add(GameProcess.From(p));
            }
            catch { }
        }
        return list
            .OrderByDescending(g => prefer.Contains(g.Name.ToLowerInvariant()))
            .ThenBy(g => g.Name)
            .ToList();
    }

    public Process? Open(int pid)
    {
        try { return Process.GetProcessById(pid); } catch { return null; }
    }
}
