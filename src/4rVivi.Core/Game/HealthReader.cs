using FourRVivi.Core.Memory;

namespace FourRVivi.Core.Game;

/// <summary>Reads HP/SP using the profile's saved addresses. Returns -1 when an address is unknown.</summary>
public sealed class HealthReader
{
    private readonly MemoryReader _reader;
    private readonly MemoryAddressBook _book;

    public HealthReader(MemoryReader reader, MemoryAddressBook book) { _reader = reader; _book = book; }

    public int Hp => Read("HP");
    public int MaxHp => Read("MaxHP");
    public int Sp => Read("SP");
    public int MaxSp => Read("MaxSP");

    // Percentage from the numbers first (HP / MaxHP * 100); only fall back to a bar-% read if MaxHP is unknown.
    public double HpPercent { get { var f = Percent(Hp, MaxHp); return f >= 0 ? f : (LiveStats.Instance.TryGetNumber("HpPercent", out var p) ? p : -1); } }
    public double SpPercent { get { var f = Percent(Sp, MaxSp); return f >= 0 ? f : (LiveStats.Instance.TryGetNumber("SpPercent", out var p) ? p : -1); } }

    private static double Percent(int cur, int max) => max > 0 ? Math.Clamp(cur * 100.0 / max, 0, 100) : -1;

    private int Read(string role)
    {
        if (LiveStats.Instance.TryGetNumber(role, out int live)) return live;   // OCR mode
        var a = _book.Get(role);
        if (a is null || !_reader.Attached) return -1;
        return _reader.ReadInt32(a.Resolve(_reader.ModuleBase));
    }
}
