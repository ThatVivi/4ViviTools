using System.Text.Json;
using FourRVivi.Core.Memory;
using FourRVivi.Core.Signatures;
using Xunit;

namespace FourRVivi.Core.Tests;

public class SignatureProfileTests
{
    [Fact]
    public void Profile_Roundtrips_Through_Json()
    {
        var p = new SignatureProfile { ClientId = "ragexe.exe|123|1.0", DisplayName = "Test" };
        p.Roles["HP"] = new RoleBinding { Type = "Int32", Path = new PointerPath { ModuleName = "ragexe.exe", BaseOffset = 0x1000, Offsets = new[] { 0x40, 0x8 } } };

        var json = JsonSerializer.Serialize(p);
        var back = JsonSerializer.Deserialize<SignatureProfile>(json)!;

        Assert.Equal(p.ClientId, back.ClientId);
        Assert.True(back.Roles.ContainsKey("HP"));
        Assert.Equal(0x1000, back.Roles["HP"].Path.BaseOffset);
        Assert.Equal(new[] { 0x40, 0x8 }, back.Roles["HP"].Path.Offsets);
    }
}
