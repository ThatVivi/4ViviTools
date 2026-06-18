using System.Collections.Generic;
using FourRVivi.Core.Memory;
using Xunit;

namespace FourRVivi.Core.Tests;

public class PointerScannerTests
{
    private const long ModuleBase = 0x400000;
    private const long ModuleSize = 0x100000; // module range 0x400000..0x500000

    [Fact]
    public void Finds_Depth1_StaticPointer()
    {
        // static slot 0x401000 holds 0x500000; target = 0x500010 (offset 0x10)
        var map = new PointerMap(ModuleBase, ModuleSize, new (long, long)[]
        {
            (0x401000, 0x500000),
        });
        var paths = PointerScanner.FindPaths(map, 0x500010, new PointerScanOptions(), "game.exe");
        Assert.Contains(paths, p => p.BaseOffset == 0x1000 && p.Offsets.Length == 1 && p.Offsets[0] == 0x10);
    }

    [Fact]
    public void Finds_Depth2_PointerChain()
    {
        // [base+0x1000] -> 0x600000 ; (0x600000+0x40)=0x600040 holds 0x700000 ; target 0x700008
        var map = new PointerMap(ModuleBase, ModuleSize, new (long, long)[]
        {
            (0x401000, 0x600000),  // static root
            (0x600040, 0x700000),  // heap mid-pointer
        });
        var paths = PointerScanner.FindPaths(map, 0x700008, new PointerScanOptions(), "game.exe");
        Assert.Contains(paths, p =>
            p.BaseOffset == 0x1000 && p.Offsets.Length == 2 && p.Offsets[0] == 0x40 && p.Offsets[1] == 0x08);
    }
}
