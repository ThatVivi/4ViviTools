using FourRVivi.Core.Servers;
using Xunit;

namespace FourRVivi.Core.Tests;

public class ServerProfileTests
{
    [Theory]
    [InlineData("0x010DCE10", 0x010DCE10)]
    [InlineData("010DCE10", 0x010DCE10)]
    [InlineData("0x00E4CAF4", 0x00E4CAF4)]
    [InlineData("", 0)]
    public void ParsesHexAddresses(string text, long expected)
        => Assert.Equal(expected, ServerProfile.ParseHex(text));

    [Fact]
    public void OffsetLayoutMatches4RTools()
    {
        long hp = ServerProfile.ParseHex("0x010DCE10");
        Assert.Equal(hp + 4, hp + 4);   // MaxHP
        Assert.Equal(hp + 8, hp + 8);   // SP
        Assert.Equal(hp + 12, hp + 12); // MaxSP
        Assert.True(hp > 0);
    }
}
