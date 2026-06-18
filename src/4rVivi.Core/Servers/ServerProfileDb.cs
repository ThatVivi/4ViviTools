using System.Text.Json;

namespace FourRVivi.Core.Servers;

/// <summary>Loads the shipped supported_servers.json (4RTools, MIT) plus any user additions.</summary>
public sealed class ServerProfileDb
{
    public IReadOnlyList<ServerProfile> All { get; private set; } = new List<ServerProfile>();

    public ServerProfileDb() => Load();

    public void Load()
    {
        var list = new List<ServerProfile>();
        foreach (var path in new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Servers", "supported_servers.json"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "supported_servers.json"),
        })
        {
            try
            {
                if (File.Exists(path))
                {
                    var part = JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(path));
                    if (part != null) list.AddRange(part);
                }
            }
            catch { }
        }
        All = list;
    }

    /// <summary>Candidate profiles whose process name matches (case-insensitive), distinct by HP address.</summary>
    public IEnumerable<ServerProfile> MatchByProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) yield break;
        var seen = new HashSet<long>();
        foreach (var p in All)
            if (string.Equals(p.Name, processName, StringComparison.OrdinalIgnoreCase) && p.HpAddr > 0 && seen.Add(p.HpAddr))
                yield return p;
    }
}
