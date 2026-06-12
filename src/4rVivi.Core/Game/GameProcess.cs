using System.Diagnostics;

namespace FourRVivi.Core.Game;

public sealed record GameProcess(int Pid, string Name, string WindowTitle, IntPtr WindowHandle)
{
    public string Display => $"{Name}.exe ({Pid}) — {WindowTitle}";
    public static GameProcess From(Process p) =>
        new(p.Id, p.ProcessName, p.MainWindowTitle ?? "", p.MainWindowHandle);
}
