using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Memory;
using Xunit;

namespace FourRVivi.Core.Tests;

public class KeyNameTests
{
    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("A", 'A')]
    [InlineData("5", '5')]
    [InlineData("SPACE", 0x20)]
    [InlineData("nonsense", 0)]
    public void Maps_keys(string name, int expected) => Assert.Equal(expected, KeyName.ToVk(name));
}

public class HumanizedTimingTests
{
    [Fact]
    public void Disabled_returns_base()
    {
        var t = new HumanizedTiming { Enabled = false };
        Assert.Equal(500, t.Apply(500));
    }

    [Fact]
    public void Enabled_stays_within_jitter_band()
    {
        var t = new HumanizedTiming { Enabled = true, JitterFraction = 0.2 };
        for (int i = 0; i < 200; i++)
        {
            int v = t.Apply(1000);
            Assert.InRange(v, 800, 1200);
        }
    }
}

public class ScanParseTests
{
    [Fact] public void Parses_int() => Assert.Equal(1234, (int)MemoryScanner.ParseValue(ScanType.Int32, "1234"));
    [Fact] public void Parses_float() => Assert.Equal(1.5f, (float)MemoryScanner.ParseValue(ScanType.Float, "1.5"));
}

public class SavedAddressTests
{
    [Fact]
    public void Resolves_module_offset_when_present()
    {
        var a = new SavedAddress { Runtime = 0x1000, ModuleOffset = 0x40 };
        Assert.Equal((IntPtr)0x2040, a.Resolve((IntPtr)0x2000));
    }

    [Fact]
    public void Falls_back_to_runtime_without_offset()
    {
        var a = new SavedAddress { Runtime = 0x9999 };
        Assert.Equal((IntPtr)0x9999, a.Resolve((IntPtr)0x2000));
    }
}

public class OpResultTests
{
    [Fact] public void Success_is_truthy() => Assert.True(OpResult.Success);
    [Fact] public void Fail_carries_message() { OpResult r = OpResult.Fail("x"); Assert.False(r); Assert.Equal("x", r.Error); }
}

public class GameDatabaseTests
{
    [Fact]
    public void Loads_embedded_database()
    {
        var db = new GameDatabase();
        Assert.True(db.IsLoaded, db.Diagnostics());
        Assert.NotEmpty(db.SearchMobs("a"));
    }
}
