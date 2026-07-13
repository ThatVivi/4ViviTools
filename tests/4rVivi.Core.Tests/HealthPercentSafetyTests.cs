using FourRVivi.App.Services;
using FourRVivi.Core.Game;
using Xunit;

namespace FourRVivi.Core.Tests;

public class HealthPercentSafetyTests
{
    [Fact]
    public void Suspect_health_number_is_not_trusted()
    {
        var stats = LiveStats.Instance;
        stats.Clear();
        stats.Active = true;

        stats.SetNumber(Roles.HpPercent, 2, LiveStatSource.PercentText, 0.42, "2", LiveStatQuality.Suspect);

        Assert.True(stats.TryGetNumber(Roles.HpPercent, out var raw));
        Assert.Equal(2, raw);
        Assert.False(stats.TryGetTrustedNumber(Roles.HpPercent, out _));
    }

    [Fact]
    public void Trusted_health_number_is_trusted()
    {
        var stats = LiveStats.Instance;
        stats.Clear();
        stats.Active = true;

        stats.SetNumber(Roles.HpPercent, 100, LiveStatSource.PercentText, 0.95, "100%", LiveStatQuality.Trusted);

        Assert.True(stats.TryGetTrustedNumber(Roles.HpPercent, out var hp));
        Assert.Equal(100, hp);
    }

    [Theory]
    [InlineData("100%", 0.50, 100)]
    [InlineData(" 75 % ", 0.50, 75)]
    [InlineData("l00%", 0.50, 100)]
    [InlineData("98", 0.92, 98)]
    public void Parses_safe_percent_text(string raw, double confidence, int expected)
    {
        Assert.True(OcrService.TryParsePercentText(raw, confidence, out var pct, out _));
        Assert.Equal(expected, pct);
    }

    [Theory]
    [InlineData("2", 0.92)]
    [InlineData("2", 0.50)]
    [InlineData("200%", 0.95)]
    [InlineData("", 0.95)]
    public void Rejects_unsafe_percent_text(string raw, double confidence)
    {
        Assert.False(OcrService.TryParsePercentText(raw, confidence, out _, out _));
    }
}
