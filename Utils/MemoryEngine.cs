// MemoryEngine.cs — ArtMoney-style memory scanner for 4RTools
// Drop into 4RTools/Utils/. Namespace matches the project's Utils folder.
// Read-only scanning + value filtering + AoB signature scan + pointer resolve.
// Target: .NET Framework 4.x, x86 build (RO clients are 32-bit).
//
// LICENSE: MIT (same as 4RTools). Private-server use only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace _4rVivi.Utils
{
    public enum ScanType { Int32, Int16, Float }

    /// <summary>
    /// Filters for "next scan" when refining a candidate set (ArtMoney-style).
    /// Exact uses the typed value; the others compare against the previous snapshot.
    /// </summary>
    public enum ScanFilter { Exact, Changed, Unchanged, Increased, Decreased, Unknown }

    public sealed class MemoryEngine : IDisposable
    {
        #region Win32
        [Flags]
        private enum ProcessAccess : uint
        {
            VmRead = 0x0010,
            VmWrite = 0x0020,
            VmOperation = 0x0008,
            QueryInformation = 0x0400
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(ProcessAccess access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
            int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern int VirtualQueryEx(
            IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_GUARD = 0x100;
        private const uint PAGE_NOACCESS = 0x01;
        // readable protections: RO=0x02, RW=0x04, WC=0x08, ER=0x20, ERW=0x40, ...
        private const uint READABLE_MASK = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;
        #endregion

        private IntPtr _handle = IntPtr.Zero;
        public Process Target { get; private set; }
        public IntPtr ModuleBase { get; private set; }
        public int ModuleSize { get; private set; }

        public bool Attach(Process p)
        {
            Detach();
            Target = p;
            _handle = OpenProcess(
                ProcessAccess.VmRead | ProcessAccess.QueryInformation | ProcessAccess.VmOperation,
                false, p.Id);
            if (_handle == IntPtr.Zero) return false;
            try
            {
                ModuleBase = p.MainModule.BaseAddress;
                ModuleSize = p.MainModule.ModuleMemorySize;
            }
            catch { /* 64-bit-from-32-bit or access denied */ }
            return true;
        }

        public void Detach()
        {
            if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
            Target = null;
        }

        // ---- typed reads ----------------------------------------------------
        public int ReadInt32(IntPtr addr) { var b = ReadBytes(addr, 4); return b == null ? 0 : BitConverter.ToInt32(b, 0); }
        public short ReadInt16(IntPtr addr) { var b = ReadBytes(addr, 2); return b == null ? (short)0 : BitConverter.ToInt16(b, 0); }
        public float ReadFloat(IntPtr addr) { var b = ReadBytes(addr, 4); return b == null ? 0f : BitConverter.ToSingle(b, 0); }

        public byte[] ReadBytes(IntPtr addr, int size)
        {
            var buf = new byte[size];
            return ReadProcessMemory(_handle, addr, buf, size, out int read) && read == size ? buf : null;
        }

        // ---- region enumeration --------------------------------------------
        private IEnumerable<KeyValuePair<IntPtr, byte[]>> ReadableRegions()
        {
            IntPtr addr = IntPtr.Zero;
            int mbiSize = Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));
            // 32-bit user space upper bound; raise for /3GB or x64 targets.
            long max = 0x7FFFFFFF;
            while ((long)addr < max)
            {
                if (VirtualQueryEx(_handle, addr, out var mbi, (uint)mbiSize) == 0) break;
                long regionSize = (long)mbi.RegionSize;
                if (regionSize <= 0) break;

                bool readable = mbi.State == MEM_COMMIT
                    && (mbi.Protect & PAGE_GUARD) == 0
                    && (mbi.Protect & PAGE_NOACCESS) == 0
                    && (mbi.Protect & READABLE_MASK) != 0;

                if (readable)
                {
                    var buf = new byte[regionSize];
                    if (ReadProcessMemory(_handle, mbi.BaseAddress, buf, (int)regionSize, out int read) && read > 0)
                        yield return new KeyValuePair<IntPtr, byte[]>(mbi.BaseAddress, buf);
                }
                addr = (IntPtr)((long)mbi.BaseAddress + regionSize);
            }
        }

        // ---- first scan (exact value) --------------------------------------
        public ScanSession FirstScan(ScanType type, object value)
        {
            var hits = new List<ScanResult>();
            byte[] needle = ToBytes(type, value);
            int step = type == ScanType.Int16 ? 2 : 1; // align step; 1 = unaligned-safe

            foreach (var region in ReadableRegions())
            {
                byte[] buf = region.Value;
                int limit = buf.Length - needle.Length;
                for (int i = 0; i <= limit; i += step)
                {
                    if (Match(buf, i, needle))
                    {
                        var addr = (IntPtr)((long)region.Key + i);
                        hits.Add(new ScanResult { Address = addr, Value = ReadTyped(type, buf, i) });
                    }
                }
            }
            return new ScanSession(this, type, hits);
        }

        /// <summary>Unknown-initial scan: snapshot everything for later change-based filtering.</summary>
        public ScanSession FirstScanUnknown(ScanType type)
        {
            var hits = new List<ScanResult>();
            int size = type == ScanType.Int16 ? 2 : 4;
            int step = size;
            foreach (var region in ReadableRegions())
            {
                byte[] buf = region.Value;
                for (int i = 0; i + size <= buf.Length; i += step)
                {
                    var addr = (IntPtr)((long)region.Key + i);
                    hits.Add(new ScanResult { Address = addr, Value = ReadTyped(type, buf, i) });
                }
            }
            return new ScanSession(this, type, hits);
        }

        // ---- AoB / signature scan (survives client updates) ----------------
        // pattern e.g. "A1 ?? ?? ?? ?? 8B 40 ?? 89"
        public IntPtr AobScanModule(string pattern)
        {
            ParsePattern(pattern, out byte[] bytes, out bool[] wild);
            if (ModuleBase == IntPtr.Zero || ModuleSize <= 0) return IntPtr.Zero;
            byte[] mod = ReadBytes(ModuleBase, ModuleSize);
            if (mod == null) return IntPtr.Zero;
            int idx = FindPattern(mod, bytes, wild);
            return idx < 0 ? IntPtr.Zero : (IntPtr)((long)ModuleBase + idx);
        }

        /// <summary>Scan signature, then read the 4-byte address operand at +operandOffset.</summary>
        public IntPtr ResolveSignature(string pattern, int operandOffset)
        {
            IntPtr sig = AobScanModule(pattern);
            if (sig == IntPtr.Zero) return IntPtr.Zero;
            return (IntPtr)ReadInt32((IntPtr)((long)sig + operandOffset));
        }

        // ---- pointer chain: [[base+o0]+o1]+... -----------------------------
        public IntPtr ResolvePointer(IntPtr baseAddr, int[] offsets)
        {
            IntPtr addr = (IntPtr)((long)baseAddr + offsets[0]);
            for (int i = 1; i < offsets.Length; i++)
                addr = (IntPtr)((long)ReadInt32(addr) + offsets[i]);
            return addr;
        }

        // ---- helpers --------------------------------------------------------
        private static byte[] ToBytes(ScanType t, object v)
        {
            switch (t)
            {
                case ScanType.Int16: return BitConverter.GetBytes(Convert.ToInt16(v));
                case ScanType.Float: return BitConverter.GetBytes(Convert.ToSingle(v));
                default: return BitConverter.GetBytes(Convert.ToInt32(v));
            }
        }

        private static object ReadTyped(ScanType t, byte[] buf, int i)
        {
            switch (t)
            {
                case ScanType.Int16: return BitConverter.ToInt16(buf, i);
                case ScanType.Float: return BitConverter.ToSingle(buf, i);
                default: return BitConverter.ToInt32(buf, i);
            }
        }

        private static bool Match(byte[] hay, int at, byte[] needle)
        {
            for (int j = 0; j < needle.Length; j++)
                if (hay[at + j] != needle[j]) return false;
            return true;
        }

        private static void ParsePattern(string pattern, out byte[] bytes, out bool[] wild)
        {
            var parts = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bytes = new byte[parts.Length];
            wild = new bool[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "?" || parts[i] == "??") { wild[i] = true; bytes[i] = 0; }
                else bytes[i] = Convert.ToByte(parts[i], 16);
            }
        }

        private static int FindPattern(byte[] hay, byte[] pat, bool[] wild)
        {
            int limit = hay.Length - pat.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool ok = true;
                for (int j = 0; j < pat.Length; j++)
                {
                    if (wild[j]) continue;
                    if (hay[i + j] != pat[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        public void Dispose() => Detach();
    }

    public sealed class ScanResult
    {
        public IntPtr Address;
        public object Value;
    }

    /// <summary>Holds candidate addresses; NextScan re-reads only candidates (fast refine).</summary>
    public sealed class ScanSession
    {
        private readonly MemoryEngine _m;
        private readonly ScanType _type;
        public List<ScanResult> Results { get; private set; }
        public int Count => Results.Count;

        public ScanSession(MemoryEngine m, ScanType type, List<ScanResult> initial)
        {
            _m = m; _type = type; Results = initial;
        }

        public void NextScan(ScanFilter filter, object exactValue = null)
        {
            var kept = new List<ScanResult>(Results.Count);
            foreach (var r in Results)
            {
                object now = ReadCurrent(r.Address);
                if (now == null) continue;
                bool keep = false;
                int cmp = Compare(now, r.Value);
                switch (filter)
                {
                    case ScanFilter.Exact: keep = Compare(now, exactValue) == 0; break;
                    case ScanFilter.Changed: keep = cmp != 0; break;
                    case ScanFilter.Unchanged: keep = cmp == 0; break;
                    case ScanFilter.Increased: keep = cmp > 0; break;
                    case ScanFilter.Decreased: keep = cmp < 0; break;
                    case ScanFilter.Unknown: keep = true; break;
                }
                if (keep) { r.Value = now; kept.Add(r); }
            }
            Results = kept;
        }

        private object ReadCurrent(IntPtr a)
        {
            switch (_type)
            {
                case ScanType.Int16: return _m.ReadInt16(a);
                case ScanType.Float: return _m.ReadFloat(a);
                default: return _m.ReadInt32(a);
            }
        }

        private static int Compare(object a, object b)
            => Convert.ToDouble(a).CompareTo(Convert.ToDouble(b));
    }
}
