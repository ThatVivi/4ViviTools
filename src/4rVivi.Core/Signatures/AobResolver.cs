using FourRVivi.Core.Memory;

namespace FourRVivi.Core.Signatures;

/// <summary>Array-of-bytes (signature) scan over the main module image. Used to anchor a pointer base
/// to a code/data pattern when a fixed module offset drifts across client patches. '?' = wildcard.</summary>
public sealed class AobResolver
{
    private readonly MemoryReader _r;
    public AobResolver(MemoryReader r) => _r = r;

    /// <summary>Parse "48 8B ?? 0D" style pattern into bytes + mask, then scan the module.</summary>
    public IntPtr FindByPattern(string pattern)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sig = new byte[tokens.Length];
        var mask = new bool[tokens.Length]; // true = must match
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] is "?" or "??") { mask[i] = false; sig[i] = 0; }
            else { mask[i] = true; sig[i] = Convert.ToByte(tokens[i], 16); }
        }
        return Scan(sig, mask);
    }

    private IntPtr Scan(byte[] sig, bool[] mask)
    {
        if (_r.ModuleBase == IntPtr.Zero || _r.ModuleSize <= 0) return IntPtr.Zero;
        long start = _r.ModuleBase.ToInt64(), size = _r.ModuleSize;
        const int CHUNK = 1 * 1024 * 1024;
        long done = 0;
        while (done < size)
        {
            int want = (int)Math.Min(CHUNK + sig.Length, size - done);
            var buf = _r.ReadPartial((IntPtr)(start + done), want);
            if (buf != null)
            {
                int n = buf.Length - sig.Length;
                for (int i = 0; i <= n; i++)
                {
                    bool ok = true;
                    for (int j = 0; j < sig.Length; j++)
                        if (mask[j] && buf[i + j] != sig[j]) { ok = false; break; }
                    if (ok) return (IntPtr)(start + done + i);
                }
            }
            done += CHUNK;
        }
        return IntPtr.Zero;
    }
}
