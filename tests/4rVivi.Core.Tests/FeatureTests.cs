using FourRVivi.Core.Automation;
using FourRVivi.Core.Tools;
using FourRVivi.Core.Trackers;
using Xunit;

namespace FourRVivi.Core.Tests;

public class StatCalculatorTests
{
    [Fact]
    public void Computes_core_stats()
    {
        var r = StatCalculator.Compute(new CalcInput(99, 99, 1, 1, 1, 50, 50, 100));
        Assert.True(r["ATK"] > 100);
        Assert.True(r["HIT"] >= 99 + 50);
        Assert.True(r["CRIT"] >= 1);
        Assert.Contains("~Max HP", r.Keys);
    }
}

public class MvpEntryTests
{
    [Fact]
    public void Unkilled_shows_dash() => Assert.Equal("—", new MvpEntry().Status());

    [Fact]
    public void Recent_kill_is_pending()
    {
        var e = new MvpEntry { MinMinutes = 60, MaxMinutes = 70, KilledAt = DateTime.Now };
        Assert.StartsWith("in ", e.Status());
    }
}

public class BuffTimerTests
{
    [Fact]
    public void Idle_until_started() => Assert.Equal("idle", new BuffTimer().Display);

    [Fact]
    public void Counts_down_after_start()
    {
        var b = new BuffTimer { DurationSec = 120 };
        b.Start();
        Assert.InRange(b.RemainingSec, 118, 120);
    }
}

public class ChainMacroTests
{
    [Fact]
    public void Holds_steps()
    {
        var m = new ChainMacro { Name = "vend" };
        m.Steps.Add(new ChainStep { Key = "F9" });
        Assert.Single(m.Steps);
        Assert.Equal("F9", m.Steps[0].Key);
    }
}
