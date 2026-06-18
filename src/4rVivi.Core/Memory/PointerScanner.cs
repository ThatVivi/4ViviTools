using System.Runtime.InteropServices;

namespace FourRVivi.Core.Memory;

/// <summary>A restart-stable pointer path: module-relative base + offset chain.
/// Resolution: cur = Read(ModuleBase+BaseOffset); for each offset except last: cur = Read(cur+offset);
/// result = cur + lastOffset.</summary>
public sealed class PointerPath
{
    public string ModuleName { get; set; } = "";
    public long BaseOffset { get; set; }
    public int[] Offsets { get; set; } = Array.Empty<int>();

    public override string ToString()
        => $"[{ModuleName}+0x{BaseOffset:X}]" + string.Concat(Offsets.Select(o => $" -> +0x{o:X}"));
}

public sealed class PointerScanOptions
{
    public int MaxDepth { get; set; } = 3;     // levels of offsets (1..3)
    public int MaxOffset { get; set; } = 0x1000;
    public int MaxResults { get; set; } = 20;
    public long MaxPointers { get; set; } = 16_000_000;  // safety cap on the pointer map
}

/// <summary>In-memory snapshot of "pointer slots" (address that holds a pointer -> the value it holds),
/// sorted by value so we can ask "which slots point into [lo,hi]". Pure + unit-testable.</summary>
public sealed class PointerMap
{
    public long ModuleBase { get; }
    public long ModuleEnd { get; }
    private readonly long[] _values;  // sorted ascending
    private readonly long[] _slots;   // slot[i] holds _values[i]

    public PointerMap(long moduleBase, long moduleSize, IEnumerable<(long slot, long value)> entries)
    {
        ModuleBase = moduleBase;
        ModuleEnd = moduleBase + moduleSize;
        var list = entries.ToList();
        list.Sort((a, b) => a.value.CompareTo(b.value));
        _values = new long[list.Count];
        _slots = new long[list.Count];
        for (int i = 0; i < list.Count; i++) { _values[i] = list[i].value; _slots[i] = list[i].slot; }
    }

    public int Count => _values.Length;
    public bool IsStatic(long slot) => slot >= ModuleBase && slot < ModuleEnd;

    /// <summary>All slots whose stored value is in [lo, hi]. (value <= addr <= value+MaxOffset).</summary>
    public IEnumerable<(long slot, long value)> SlotsPointingInto(long lo, long hi)
    {
        int i = LowerBound(lo);
        for (; i < _values.Length && _values[i] <= hi; i++)
            yield return (_slots[i], _values[i]);
    }

    private int LowerBound(long v)
    {
        int lo = 0, hi = _values.Length;
        while (lo < hi) { int m = (lo + hi) >> 1; if (_values[m] < v) lo = m + 1; else hi = m; }
        return lo;
    }
}

/// <summary>Builds a pointer map from a live process and finds restart-stable pointer paths to a
/// target address (built-in pointer scanner, "C" in the design). Bounded BFS, module-anchored.</summary>
public sealed class PointerScanner
{
    private readonly MemoryReader _r;
    public PointerScanner(MemoryReader r) => _r = r;

    private int PtrSize => _r.TargetIs64Bit() ? 8 : 4;
    private long ReadPtr(long addr)
    {
        var b = _r.ReadBytes((IntPtr)addr, PtrSize);
        if (b is null) return 0;
        return PtrSize == 8 ? BitConverter.ToInt64(b) : BitConverter.ToUInt32(b);
    }

    public string ModuleName => _r.Target?.MainModule?.ModuleName ?? "game.exe";

    /// <summary>Scan the process and return candidate paths to <paramref name="target"/>, shortest first.</summary>
    public List<PointerPath> Find(IntPtr target, PointerScanOptions? opts = null)
    {
        opts ??= new PointerScanOptions();
        var map = BuildMap(opts);
        var paths = FindPaths(map, (long)target, opts, ModuleName);
        // validate against the live process and keep only those that re-resolve
        return paths.Where(p => Resolve(p) == target).Take(opts.MaxResults).ToList();
    }

    /// <summary>Resolve a path in the live process.</summary>
    public IntPtr Resolve(PointerPath p)
    {
        long cur = ReadPtr(_r.ModuleBase.ToInt64() + p.BaseOffset);
        for (int i = 0; i < p.Offsets.Length - 1; i++)
            cur = ReadPtr(cur + p.Offsets[i]);
        long result = cur + (p.Offsets.Length > 0 ? p.Offsets[^1] : 0);
        return (IntPtr)result;
    }

    /// <summary>Pure path finder over a snapshot — unit-testable without a process.</summary>
    public static List<PointerPath> FindPaths(PointerMap map, long target, PointerScanOptions opts, string moduleName)
    {
        var results = new List<PointerPath>();

        // depth 1: static slot whose value points near target
        foreach (var (slot, val) in map.SlotsPointingInto(target - opts.MaxOffset, target))
        {
            if (!map.IsStatic(slot)) continue;
            results.Add(new PointerPath { ModuleName = moduleName, BaseOffset = slot - map.ModuleBase, Offsets = new[] { (int)(target - val) } });
            if (results.Count >= opts.MaxResults) return results;
        }

        // depth 2: any slot P near target, then static slot near P.slot
        if (opts.MaxDepth >= 2)
        {
            foreach (var (pSlot, pVal) in map.SlotsPointingInto(target - opts.MaxOffset, target))
            {
                int offLast = (int)(target - pVal);
                foreach (var (sSlot, sVal) in map.SlotsPointingInto(pSlot - opts.MaxOffset, pSlot))
                {
                    if (!map.IsStatic(sSlot)) continue;
                    results.Add(new PointerPath
                    {
                        ModuleName = moduleName,
                        BaseOffset = sSlot - map.ModuleBase,
                        Offsets = new[] { (int)(pSlot - sVal), offLast }
                    });
                    if (results.Count >= opts.MaxResults) return results;
                }
            }
        }
        return results;
    }

    private PointerMap BuildMap(PointerScanOptions opts)
    {
        var entries = new List<(long slot, long value)>();
        long max = _r.TargetIs64Bit() ? 0x7FFFFFFFFFFFL : 0x7FFFFFFFL;
        int ptr = PtrSize;
        int mbiSize = Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>();
        IntPtr addr = IntPtr.Zero;
        const int CHUNK = 4 * 1024 * 1024;

        while ((long)addr < max && entries.Count < opts.MaxPointers)
        {
            if (Native.VirtualQueryEx(_r.Handle, addr, out var mbi, (uint)mbiSize) == 0) break;
            long size = (long)mbi.RegionSize;
            if (size <= 0) { addr = (IntPtr)((long)addr + 0x1000); continue; }

            bool readable = mbi.State == Native.MEM_COMMIT
                && (mbi.Protect & Native.PAGE_GUARD) == 0
                && (mbi.Protect & Native.PAGE_NOACCESS) == 0
                && (mbi.Protect & Native.READABLE_MASK) != 0;

            if (readable)
            {
                long regionBase = (long)mbi.BaseAddress, done = 0;
                while (done < size && entries.Count < opts.MaxPointers)
                {
                    int want = (int)Math.Min(CHUNK, size - done);
                    var buf = _r.ReadPartial((IntPtr)(regionBase + done), want);
                    if (buf != null)
                    {
                        int n = buf.Length - ptr;
                        for (int i = 0; i <= n; i += ptr)
                        {
                            long val = ptr == 8 ? BitConverter.ToInt64(buf, i) : BitConverter.ToUInt32(buf, i);
                            if (val >= 0x10000 && val < max)
                                entries.Add((regionBase + done + i, val));
                            if (entries.Count >= opts.MaxPointers) break;
                        }
                    }
                    done += want;
                }
            }
            addr = (IntPtr)((long)mbi.BaseAddress + size);
        }
        return new PointerMap(_r.ModuleBase.ToInt64(), _r.ModuleSize, entries);
    }
}
