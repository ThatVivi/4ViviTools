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

    public double HpPercent => Percent(Hp, MaxHp);
    public double SpPercent => Percent(Sp, MaxSp);

    private static double Percent(int cur, int max) => max > 0 ? Math.Clamp(cur * 100.0 / max, 0, 100) : -1;

    private int Read(string role)
    {
        var a = _book.Get(role);
        if (a is null || !_reader.Attached) return -1;
        return _reader.ReadInt32(a.Resolve(_reader.ModuleBase));
    }
}
