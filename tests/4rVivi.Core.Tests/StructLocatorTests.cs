using System.Collections.Generic;
using FourRVivi.Core.Memory;
using Xunit;

namespace FourRVivi.Core.Tests;

public class StructLocatorTests
{
    [Fact]
    public void Locates_Cluster_And_Maps_Roles()
    {
        // HP/MaxHP/SP sit together near 0x1000; decoys are far away.
        var cands = new Dictionary<string, List<long>>
        {
            ["HP"]    = new() { 0x1000, 0x90000 },
            ["MaxHP"] = new() { 0x1004, 0x80000 },
            ["SP"]    = new() { 0x1008 },
        };
        var map = StructLocator.Locate(cands, 0x100);
        Assert.Equal(0x1000, map["HP"]);
        Assert.Equal(0x1004, map["MaxHP"]);
        Assert.Equal(0x1008, map["SP"]);
    }

    [Fact]
    public void Returns_Empty_When_No_Candidates()
        => Assert.Empty(StructLocator.Locate(new Dictionary<string, List<long>>(), 0x100));
}
