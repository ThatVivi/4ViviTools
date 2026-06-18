using FourRVivi.Core.Game;

namespace FourRVivi.Core.Servers;

public sealed class ServerBindResult
{
    public bool Ok { get; set; }
    public string ServerName { get; set; } = "";
    public string Message { get; set; } = "";
    public Dictionary<string, (long addr, string type)> Roles { get; } = new();
}

/// <summary>Resolves a client's HP/MaxHP/SP/MaxSP/Name from fixed absolute addresses (4RTools model),
/// validating by reading HP &gt; 0. Returns the role-&gt;address map; the caller binds + persists.</summary>
public sealed class ServerBinder
{
    private readonly ServerProfileDb _db;
    public ServerBinder(ServerProfileDb db) => _db = db;

    private const long MaxPlausibleHp = 100_000_000;

    public ServerBindResult TryResolve(GameSession s, ServerProfile? forced = null)
    {
        var r = new ServerBindResult();
        if (!s.Reader.Attached) { r.Message = "Not attached. Pick your RO process in the top bar first."; return r; }

        var candidates = forced != null
            ? new List<ServerProfile> { forced }
            : _db.MatchByProcess(s.Reader.Target?.ProcessName ?? "").ToList();

        if (candidates.Count == 0)
        {
            r.Message = "This client isn't in the server list. Pick it from the dropdown, paste an HP address, or use the Scanner.";
            return r;
        }

        foreach (var p in candidates)
        {
            long hp = p.HpAddr;
            if (hp <= 0) continue;
            uint cur = (uint)s.Reader.ReadInt32((IntPtr)hp);
            uint max = (uint)s.Reader.ReadInt32((IntPtr)(hp + 4));
            if (cur > 0 && cur <= MaxPlausibleHp && max >= cur && max <= MaxPlausibleHp)
            {
                r.Roles["HP"] = (hp, "Int32");
                r.Roles["MaxHP"] = (hp + 4, "Int32");
                r.Roles["SP"] = (hp + 8, "Int32");
                r.Roles["MaxSP"] = (hp + 12, "Int32");
                if (p.NameAddr > 0) r.Roles["CharName"] = (p.NameAddr, "String");
                r.Ok = true;
                r.ServerName = p.Label;
                r.Message = $"Bound from '{p.Description}'. HP {cur}/{max}.";
                return r;
            }
        }
        r.Message = "Found the client name but the saved address didn't read valid HP (client may be a different build, or Gepard is blocking it).";
        return r;
    }
}
