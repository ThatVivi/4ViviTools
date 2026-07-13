using FourRVivi.Core.Game;

namespace FourRVivi.Core.Trackers;

/// <summary>EXP/hr and Zeny/hr from bound addresses, plus elapsed. Baseline captured on reset.</summary>
public sealed class SessionTracker
{
    private readonly StatReader _stat;
    public DateTime StartedAt { get; private set; } = DateTime.Now;
    private int _baseExp = -1, _baseZeny = -1;

    public SessionTracker(GameSession session) => _stat = new StatReader(session);

    public LootLog? Loot { get; set; }
    public int Deaths { get; private set; }
    private double _lastHpPct = -1;

    public void Reset() { StartedAt = DateTime.Now; _baseExp = _stat.Exp; _baseZeny = _stat.Zeny; Deaths = 0; _lastHpPct = -1; }

    /// <summary>Feed trusted HP percent each tick to auto-count deaths.</summary>
    public void ObserveTrustedHpPercent(double hpPct)
    {
        if (_lastHpPct > 0 && hpPct <= 0) Deaths++;
        if (hpPct >= 0) _lastHpPct = hpPct;
    }

    public TimeSpan Elapsed => DateTime.Now - StartedAt;
    private double Hours => Math.Max(Elapsed.TotalHours, 1.0 / 3600);

    public long ExpGained { get { int e = _stat.Exp; return e < 0 || _baseExp < 0 ? 0 : Math.Max(0, e - _baseExp); } }
    public long ZenyGained { get { int z = _stat.Zeny; return z < 0 || _baseZeny < 0 ? 0 : Math.Max(0, z - _baseZeny); } }
    public long ExpPerHour => (long)(ExpGained / Hours);
    public long ZenyPerHour => (long)(ZenyGained / Hours);

    // loot + profit analytics (doc: Loot/Hour, Profit/Hour, Deaths)
    public int LootCount => LiveStats.Instance.TryGetNumber("Loot", out var l) ? l : (Loot?.TotalCount ?? 0);
    public long LootValue => Loot?.TotalValue ?? 0;
    public long LootPerHour => (long)(LootCount / Hours);
    public long ProfitGained => ZenyGained + LootValue;
    public long ProfitPerHour => (long)(ProfitGained / Hours);
    public long DeathsPerHour => (long)(Deaths / Hours);
}
