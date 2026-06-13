using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace FourRVivi.Core.Memory;

public enum ScanType { Byte, Int16, Int32, Int64, UInt16, UInt32, Float, Double, String }
public enum ScanFilter { Exact, Changed, Unchanged, Increased, Decreased, Unknown }

public sealed record ScanHit(IntPtr Address, object Value)
{
    public string AddressHex => "0x" + ((long)Address).ToString("X8");
    public string Display => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "";
}

public sealed class ScanDiagnostics
{
    public int RegionsSeen, RegionsRead;
    public long BytesRead;
    public bool TargetIs64Bit;
    public long ElapsedMs;
}

/// <summary>Value scanning + refinement. Aligned + parallel for speed.</summary>
public sealed class MemoryScanner
{
    private readonly MemoryReader _r;
    public ScanDiagnostics LastDiagnostics { get; private set; } = new();
    /// <summary>Aligned scan (step = value size) — much faster and matches how games store values.</summary>
    public bool Aligned { get; set; } = true;

    public MemoryScanner(MemoryReader reader) => _r = reader;

    public List<ScanHit> FirstScan(ScanType type, object value)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var diag = new ScanDiagnostics { TargetIs64Bit = _r.TargetIs64Bit() };
        byte[] needle = ToBytes(type, value);
        int vsize = needle.Length;
        int step = (type == ScanType.String || type == ScanType.Byte) ? 1 : (Aligned ? vsize : 1);

        var regions = EnumerateRegions(diag);
        var bag = new ConcurrentBag<ScanHit>();

        Parallel.ForEach(regions, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            region =>
            {
                var (baseAddr, size) = region;
                byte[] buf = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    int got = _r.ReadInto(baseAddr, buf, size);
                    if (got <= 0) return;
                    Interlocked.Increment(ref diag.RegionsRead);
                    Interlocked.Add(ref diag.BytesRead, got);
                    int limit = got - vsize;
                    for (int i = 0; i <= limit; i += step)
                        if (Match(buf, i, needle))
                            bag.Add(new ScanHit((IntPtr)((long)baseAddr + i), ReadTyped(type, buf, i)));
                }
                finally { ArrayPool<byte>.Shared.Return(buf); }
            });

        sw.Stop(); diag.ElapsedMs = sw.ElapsedMilliseconds;
        LastDiagnostics = diag;
        return bag.OrderBy(h => (long)h.Address).ToList();
    }

    public List<ScanHit> NextScan(List<ScanHit> previous, ScanType type, ScanFilter filter, object? exact = null)
    {
        var kept = new List<ScanHit>(previous.Count);
        foreach (var h in previous)
        {
            object? now = ReadCurrent(type, h.Address);
            if (now is null) continue;
            int cmp = Compare(now, h.Value);
            bool keep = filter switch
            {
                ScanFilter.Exact => exact is not null && Compare(now, exact) == 0,
                ScanFilter.Changed => cmp != 0,
                ScanFilter.Unchanged => cmp == 0,
                ScanFilter.Increased => cmp > 0,
                ScanFilter.Decreased => cmp < 0,
                _ => true
            };
            if (keep) kept.Add(h with { Value = now });
        }
        return kept;
    }

    private object? ReadCurrent(ScanType t, IntPtr a) => t switch
    {
        ScanType.Byte => _r.ReadByte(a),
        ScanType.Int16 => _r.ReadInt16(a),
        ScanType.UInt16 => _r.ReadUInt16(a),
        ScanType.Int32 => _r.ReadInt32(a),
        ScanType.UInt32 => _r.ReadUInt32(a),
        ScanType.Int64 => _r.ReadInt64(a),
        ScanType.Float => _r.ReadFloat(a),
        ScanType.Double => _r.ReadDouble(a),
        _ => _r.ReadInt32(a)
    };

    /// <summary>Fast pass: just collect committed readable region descriptors (no reads yet).</summary>
    private List<(IntPtr baseAddr, int size)> EnumerateRegions(ScanDiagnostics diag)
    {
        var list = new List<(IntPtr, int)>();
        IntPtr addr = IntPtr.Zero;
        long max = diag.TargetIs64Bit ? 0x7FFFFFFFFFFFL : 0x7FFFFFFFL;
        const int CHUNK = 8 * 1024 * 1024;
        int mbiSize = Marshal.SizeOf<Native.MEMORY_BASIC_INFORMATION>();

        while ((long)addr < max)
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
                diag.RegionsSeen++;
                long done = 0;
                while (done < size)
                {
                    int want = (int)Math.Min(CHUNK, size - done);
                    list.Add(((IntPtr)((long)mbi.BaseAddress + done), want));
                    done += want;
                }
            }
            addr = (IntPtr)((long)mbi.BaseAddress + size);
        }
        return list;
    }

    private static byte[] ToBytes(ScanType t, object v) => t switch
    {
        ScanType.Byte => new[] { Convert.ToByte(v) },
        ScanType.Int16 => BitConverter.GetBytes(Convert.ToInt16(v)),
        ScanType.UInt16 => BitConverter.GetBytes(Convert.ToUInt16(v)),
        ScanType.Int32 => BitConverter.GetBytes(Convert.ToInt32(v)),
        ScanType.UInt32 => BitConverter.GetBytes(Convert.ToUInt32(v)),
        ScanType.Int64 => BitConverter.GetBytes(Convert.ToInt64(v)),
        ScanType.Float => BitConverter.GetBytes(Convert.ToSingle(v)),
        ScanType.Double => BitConverter.GetBytes(Convert.ToDouble(v)),
        ScanType.String => System.Text.Encoding.ASCII.GetBytes(Convert.ToString(v) ?? ""),
        _ => BitConverter.GetBytes(Convert.ToInt32(v))
    };

    private static object ReadTyped(ScanType t, byte[] b, int i) => t switch
    {
        ScanType.Byte => b[i],
        ScanType.Int16 => BitConverter.ToInt16(b, i),
        ScanType.UInt16 => BitConverter.ToUInt16(b, i),
        ScanType.Int32 => BitConverter.ToInt32(b, i),
        ScanType.UInt32 => BitConverter.ToUInt32(b, i),
        ScanType.Int64 => BitConverter.ToInt64(b, i),
        ScanType.Float => BitConverter.ToSingle(b, i),
        ScanType.Double => BitConverter.ToDouble(b, i),
        _ => BitConverter.ToInt32(b, i)
    };

    private static bool Match(byte[] hay, int at, byte[] needle)
    {
        for (int j = 0; j < needle.Length; j++) if (hay[at + j] != needle[j]) return false;
        return true;
    }

    private static int Compare(object a, object b)
    {
        if (a is string || b is string) return string.Equals(a?.ToString(), b?.ToString(), StringComparison.Ordinal) ? 0 : 1;
        return Convert.ToDouble(a).CompareTo(Convert.ToDouble(b));
    }

    public static object ParseValue(ScanType t, string text) => t switch
    {
        ScanType.String => text,
        ScanType.Float => float.Parse(text.Trim(), CultureInfo.InvariantCulture),
        ScanType.Double => double.Parse(text.Trim(), CultureInfo.InvariantCulture),
        ScanType.Byte => byte.Parse(text.Trim()),
        ScanType.Int16 => short.Parse(text.Trim()),
        ScanType.UInt16 => ushort.Parse(text.Trim()),
        ScanType.UInt32 => uint.Parse(text.Trim()),
        ScanType.Int64 => long.Parse(text.Trim()),
        _ => int.Parse(text.Trim())
    };
}
