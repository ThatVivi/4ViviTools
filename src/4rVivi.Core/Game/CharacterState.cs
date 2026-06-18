namespace FourRVivi.Core.Game;

/// <summary>Single source of truth for live character data (PDF "Unified Character State
/// Engine"). RPC, trackers, autopot and the dashboard all read this instead of each running
/// their own memory/OCR reads.</summary>
public sealed class CharacterState
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MapName { get; set; } = "";
    public int BaseLevel { get; set; }
    public int JobLevel { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Sp { get; set; }
    public int MaxSp { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public long Zeny { get; set; }
    public string Activity { get; set; } = "Idle";

    public int HpPct => MaxHp > 0 ? (int)Math.Round(Hp * 100.0 / MaxHp) : 0;
    public int SpPct => MaxSp > 0 ? (int)Math.Round(Sp * 100.0 / MaxSp) : 0;
}

/// <summary>Builds a validated <see cref="CharacterState"/> from the bound memory roles and
/// derives the current activity (Idle / Walking / Grinding) from frame-to-frame deltas.</summary>
public sealed class CharacterStateReader
{
    private readonly GameSession _gs;
    private CharacterState _last = new();
    private DateTime _lastMoveUtc = DateTime.UtcNow;

    public CharacterStateReader(GameSession gs) => _gs = gs;

    public CharacterState? Snapshot()
    {
        if (_gs.Process is null) return null;

        var s = new CharacterState
        {
            Name      = Clean(_gs.ReadRoleString(Roles.CharName), _last.Name),
            ClassName = Clean(_gs.ReadRoleString(Roles.ClassName), _last.ClassName),
            MapName   = Clean(_gs.ReadRoleString(Roles.MapName, 16), _last.MapName),
            BaseLevel = Valid(_gs.ReadRole(Roles.BaseLevel), 1, 999, _last.BaseLevel),
            JobLevel  = Valid(_gs.ReadRole(Roles.JobLevel), 1, 999, _last.JobLevel),
            MaxHp     = Valid(_gs.ReadRole(Roles.MaxHp), 1, 100_000_000, _last.MaxHp),
            MaxSp     = Valid(_gs.ReadRole(Roles.MaxSp), 1, 100_000_000, _last.MaxSp),
            X         = Valid(_gs.ReadRole(Roles.PosX), 0, 100_000, _last.X),
            Y         = Valid(_gs.ReadRole(Roles.PosY), 0, 100_000, _last.Y),
            Zeny      = Valid(_gs.ReadRole(Roles.Zeny), 0, 10_000_000, (int)Math.Min(_last.Zeny, int.MaxValue)),
        };
        // HP/SP validated against their own max (validation layer: reject <0 or >max)
        int maxHp = s.MaxHp > 0 ? s.MaxHp : int.MaxValue;
        int maxSp = s.MaxSp > 0 ? s.MaxSp : int.MaxValue;
        s.Hp = Valid(_gs.ReadRole(Roles.Hp), 0, maxHp, _last.Hp);
        s.Sp = Valid(_gs.ReadRole(Roles.Sp), 0, maxSp, _last.Sp);

        s.Activity = DeriveActivity(s);
        _last = s;
        return s;
    }

    private string DeriveActivity(CharacterState s)
    {
        bool moved = s.X != _last.X || s.Y != _last.Y;
        bool fighting = s.Hp < _last.Hp || s.Sp < _last.Sp;
        if (moved) { _lastMoveUtc = DateTime.UtcNow; return fighting ? "Grinding" : "Walking"; }
        if (fighting) return "Grinding";
        if ((DateTime.UtcNow - _lastMoveUtc).TotalMinutes >= 15) return "AFK";
        return "Idle";
    }

    private static int Valid(int? v, int min, int max, int fallback)
        => (v.HasValue && v.Value >= min && v.Value <= max) ? v.Value : fallback;
    private static string Clean(string v, string fallback)
        => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
}
