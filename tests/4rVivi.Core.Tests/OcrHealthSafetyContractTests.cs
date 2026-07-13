using System.Drawing;
using FourRVivi.App.Services;
using FourRVivi.Core.Game;
using Xunit;

namespace FourRVivi.Core.Tests;

public sealed class OcrHealthSafetyContractTests
{
    [Theory]
    [InlineData(Roles.HpPercent)]
    [InlineData(Roles.SpPercent)]
    [InlineData("HP Bar")]
    [InlineData("SP Bar")]
    public void Vital_health_roles_never_return_bar_fill_percent(string role)
    {
        using var bmp = HalfFilledBar();
        using var ocr = new OcrService();

        var pct = ocr.ReadBarPercentFrom(bmp, 0, 0, 1, 1, 0, 0, role);

        Assert.Equal(-1, pct);
    }

    [Fact]
    public void Non_vital_bar_fill_still_reads_the_fixture()
    {
        using var bmp = HalfFilledBar();
        using var ocr = new OcrService();

        var pct = ocr.ReadBarPercentFrom(bmp, 0, 0, 1, 1, 0, 0, "BaseExpBar");

        Assert.InRange(pct, 45, 55);
    }

    [Fact]
    public void Held_percent_remains_visible_but_not_trusted()
    {
        var stats = LiveStats.Instance;
        stats.Clear();
        stats.Active = true;
        stats.SetNumber(Roles.HpPercent, 100, LiveStatSource.PercentText, 0.96, "100%", LiveStatQuality.Trusted);

        stats.HoldNumber(Roles.HpPercent, 2, LiveStatSource.PercentText, 0.42, "2");

        Assert.True(stats.TryGetNumber(Roles.HpPercent, out var raw));
        Assert.Equal(2, raw);
        Assert.True(stats.TryGetNumberMeta(Roles.HpPercent, out var meta));
        Assert.Equal(LiveStatQuality.Held, meta.Quality);
        Assert.False(stats.TryGetTrustedNumber(Roles.HpPercent, out _));

        using var session = new GameSession();
        var stat = new StatReader(session);
        Assert.Equal(-1, session.Health.HpPercent);
        Assert.Equal(-1, stat.HpPercent);
    }

    private static Bitmap HalfFilledBar()
    {
        var bmp = new Bitmap(100, 12);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        using var fill = new SolidBrush(Color.White);
        g.FillRectangle(fill, 0, 0, 50, 12);
        return bmp;
    }
}
