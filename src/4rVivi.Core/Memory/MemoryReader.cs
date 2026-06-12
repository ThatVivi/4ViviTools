using System.Diagnostics;
using FourRVivi.Core.Common;

namespace FourRVivi.Core.Memory;

/// <summary>Opens a target process and reads/writes typed values. Win32, x64-safe (IntPtr).</summary>
public sealed class MemoryReader : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;

    public Process? Target { get; private set; }
    public IntPtr ModuleBase { get; private set; }
    public int ModuleSize { get; private set; }
    public bool Attached => _handle != IntPtr.Zero;

    public OpResult Attach(Process p)
    {
        Detach();
        Target = p;
        _handle = Native.OpenProcess(
            Native.ProcessAccess.VmRead | Native.ProcessAccess.VmWrite |
            Native.ProcessAccess.QueryInformation | Native.ProcessAccess.VmOperation, false, p.Id);
        if (_handle == IntPtr.Zero)
            return OpResult.Fail("OpenProcess failed — run 4rVivi as Administrator.");
        try { ModuleBase = p.MainModule!.BaseAddress; ModuleSize = p.MainModule.ModuleMemorySize; }
        catch { /* cross-bitness / access denied — non-fatal */ }
        return OpResult.Success;
    }

    public void Detach()
    {
        if (_handle != IntPtr.Zero) { Native.CloseHandle(_handle); _handle = IntPtr.Zero; }
        Target = null;
    }

    public bool TargetIs64Bit()
    {
        try
        {
            if (!Environment.Is64BitOperatingSystem) return false;
            if (_handle != IntPtr.Zero && Native.IsWow64Process(_handle, out bool wow)) return !wow;
        }
        catch { }
        return false;
    }

    public byte[]? ReadBytes(IntPtr addr, int size)
    {
        if (_handle == IntPtr.Zero) return null;
        var buf = new byte[size];
        return Native.ReadProcessMemory(_handle, addr, buf, size, out int read) && read == size ? buf : null;
    }

    public byte[]? ReadPartial(IntPtr addr, int size)
    {
        if (_handle == IntPtr.Zero) return null;
        var buf = new byte[size];
        if (!Native.ReadProcessMemory(_handle, addr, buf, size, out int read) || read <= 0) return null;
        if (read == size) return buf;
        var t = new byte[read]; Buffer.BlockCopy(buf, 0, t, 0, read); return t;
    }

    /// <summary>Read up to <paramref name="size"/> bytes into a caller-provided buffer; returns bytes read.</summary>
    public int ReadInto(IntPtr addr, byte[] buffer, int size)
    {
        if (_handle == IntPtr.Zero) return 0;
        return Native.ReadProcessMemory(_handle, addr, buffer, size, out int read) || read > 0 ? read : 0;
    }

    public int ReadInt32(IntPtr a) { var b = ReadBytes(a, 4); return b is null ? 0 : BitConverter.ToInt32(b); }
    public short ReadInt16(IntPtr a) { var b = ReadBytes(a, 2); return b is null ? (short)0 : BitConverter.ToInt16(b); }
    public float ReadFloat(IntPtr a) { var b = ReadBytes(a, 4); return b is null ? 0f : BitConverter.ToSingle(b); }

    public bool WriteInt32(IntPtr a, int v)
    {
        if (_handle == IntPtr.Zero) return false;
        var b = BitConverter.GetBytes(v);
        return Native.WriteProcessMemory(_handle, a, b, b.Length, out int w) && w == b.Length;
    }

    /// <summary>Resolve [[base+o0]+o1]+... pointer chains.</summary>
    public IntPtr ResolvePointer(IntPtr baseAddr, params int[] offsets)
    {
        if (offsets.Length == 0) return baseAddr;
        IntPtr addr = baseAddr + offsets[0];
        for (int i = 1; i < offsets.Length; i++)
            addr = (IntPtr)((long)ReadInt32(addr) + offsets[i]);
        return addr;
    }

    internal IntPtr Handle => _handle;
    public void Dispose() => Detach();
}
