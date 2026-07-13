using System.Runtime.InteropServices;
using System.Reflection;
using FourRVivi.Core.Common;
using Microsoft.Win32;

namespace FourRVivi.Core.Input;

public sealed class VirtualHidInput : IDisposable
{
    private readonly object _lock = new();
    private IntPtr _fakerHandle;
    private bool _fakerConnected;
    private long _nextFakerRetryTick;
    private string? _lastFakerError;
    private IntPtr _vmouseHandle = IntPtr.Zero;
    private static readonly object DllLock = new();
    private static IntPtr _fakerDllHandle;
    private static string? _fakerDllPath;
    private static readonly string[] RequiredFakerExports =
    {
        "fakerinput_alloc",
        "fakerinput_free",
        "fakerinput_connect",
        "fakerinput_disconnect",
        "fakerinput_update_keyboard",
        "fakerinput_update_relative_mouse",
    };
    private static readonly HashSet<string> LoggedIncompatibleFakerDlls = new(StringComparer.OrdinalIgnoreCase);

    private const byte MouseLeft = 0x01;
    private const byte VmouseReportId = 0x02;
    private const int GenericWrite = 0x40000000;
    private const int FileShareWrite = 0x00000002;
    private const int OpenExisting = 3;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll")] private static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_Interface_List_Size(out int pulLen, ref Guid interfaceClassGuid, string? pDeviceID, int ulFlags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_Interface_List(ref Guid interfaceClassGuid, string? pDeviceID, char[] buffer, int bufferLen, int ulFlags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFile(string lpFileName, int dwDesiredAccess, int dwShareMode, IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("FakerInputDll.dll", EntryPoint = "fakerinput_alloc", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr FakerAlloc();
    [DllImport("FakerInputDll.dll", EntryPoint = "fakerinput_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FakerFree(IntPtr handle);
    [DllImport("FakerInputDll.dll", EntryPoint = "fakerinput_connect", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool FakerConnect(IntPtr handle);
    [DllImport("FakerInputDll.dll", EntryPoint = "fakerinput_disconnect", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FakerDisconnect(IntPtr handle);
    [DllImport("FakerInputDll.dll", EntryPoint = "fakerinput_update_keyboard", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool FakerKeyboard(IntPtr handle, byte shiftKeyFlags, byte[] keyCodes);
    [DllImport("FakerInputDll.dll", EntryPoint = "fakerinput_update_relative_mouse", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool FakerRelativeMouse(IntPtr handle, byte button, short x, short y, byte wheel, byte hWheel);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    static VirtualHidInput()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(VirtualHidInput).Assembly, ResolveDllImport);
        }
        catch
        {
            // Another resolver may already be registered for this assembly. In that case the normal
            // P/Invoke probing path still runs and connection diagnostics below explain failures.
        }
    }

    private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals("FakerInputDll.dll", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        lock (DllLock)
        {
            if (_fakerDllHandle != IntPtr.Zero)
                return _fakerDllHandle;

            _fakerDllPath ??= FindFakerInputDll();
            if (string.IsNullOrWhiteSpace(_fakerDllPath))
                return IntPtr.Zero;

            return NativeLibrary.TryLoad(_fakerDllPath, out _fakerDllHandle)
                ? _fakerDllHandle
                : IntPtr.Zero;
        }
    }

    public bool IsFakerInputInstalled()
    {
        try
        {
            using var rootDevice = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\FakerInput");
            if (rootDevice != null) return true;
        }
        catch { }

        return FindFakerInputDll() != null;
    }

    public bool IsVmouseInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vmouse");
            if (key != null) return true;
        }
        catch { }

        return TryOpenVmouse() != IntPtr.Zero;
    }

    public bool IsReady => _fakerConnected || _vmouseHandle != IntPtr.Zero;

    public bool EnsureConnected()
    {
        lock (_lock)
        {
            var faker = EnsureFakerConnected();
            var vmouse = EnsureVmouseConnected();
            return faker || vmouse;
        }
    }

    public bool TapKey(string key, int holdMs)
    {
        var usage = KeyToHidUsage(key);
        if (usage == 0) return false;
        lock (_lock)
        {
            if (!EnsureFakerConnected())
            {
                DebugTrace.Write("VirtualHID", $"FakerInput keyboard unavailable for '{key}'.");
                return false;
            }

            var down = new byte[6];
            down[0] = usage;
            var okDown = FakerKeyboard(_fakerHandle, 0, down);
            Thread.Sleep(Math.Max(30, holdMs));
            var okUp = FakerKeyboard(_fakerHandle, 0, new byte[6]);
            DebugTrace.Write("VirtualHID", $"FakerInput key '{key}' usage=0x{usage:X2} down={okDown} up={okUp}.");
            return okDown && okUp;
        }
    }

    public bool ClickAtScreen(int x, int y, int holdMs)
    {
        lock (_lock)
        {
            if (EnsureFakerConnected())
            {
                MoveWithFaker(x, y);
                var down = FakerRelativeMouse(_fakerHandle, MouseLeft, 0, 0, 0, 0);
                Thread.Sleep(Math.Max(30, holdMs));
                var up = FakerRelativeMouse(_fakerHandle, 0, 0, 0, 0, 0);
                DebugTrace.Write("VirtualHID", $"FakerInput click screen={x},{y} down={down} up={up}.");
                return down && up;
            }

            if (EnsureVmouseConnected())
            {
                MoveWithVmouse(x, y);
                var down = SendVmouseReport(MouseLeft, 0, 0);
                Thread.Sleep(Math.Max(30, holdMs));
                var up = SendVmouseReport(0, 0, 0);
                DebugTrace.Write("VirtualHID", $"vmouse click screen={x},{y} down={down} up={up}.");
                return down && up;
            }
        }

        DebugTrace.Write("VirtualHID", "Virtual HID click failed: no FakerInput/vmouse device connected.");
        return false;
    }

    private bool EnsureFakerConnected()
    {
        if (_fakerConnected) return true;
        long nowTick = Environment.TickCount64;
        if (nowTick < _nextFakerRetryTick)
            return false;

        var dll = _fakerDllPath ??= FindFakerInputDll();
        if (dll != null)
        {
            try
            {
                NativeLibrary.Load(dll);
                DebugTrace.Write("VirtualHID", $"Loaded FakerInput DLL: {dll}");
            }
            catch (Exception ex)
            {
                DebugTrace.Write("VirtualHID", $"Could not pre-load FakerInput DLL '{dll}'.", ex);
            }
        }

        try
        {
            _fakerHandle = FakerAlloc();
            _fakerConnected = _fakerHandle != IntPtr.Zero && FakerConnect(_fakerHandle);
            DebugTrace.Write("VirtualHID", $"FakerInput connect={_fakerConnected}.");
            if (!_fakerConnected)
                _nextFakerRetryTick = Environment.TickCount64 + 3000;
            return _fakerConnected;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            var msg = ex.GetType().Name + ": " + ex.Message;
            if (!string.Equals(msg, _lastFakerError, StringComparison.Ordinal))
            {
                DebugTrace.Write("VirtualHID", "FakerInput connect failed.", ex);
                _lastFakerError = msg;
            }
            else
            {
                DebugTrace.Write("VirtualHID", "FakerInput connect still unavailable; retry delayed.");
            }
            if (_fakerHandle != IntPtr.Zero)
            {
                try { FakerFree(_fakerHandle); } catch { }
            }
            _fakerHandle = IntPtr.Zero;
            _fakerConnected = false;
            _nextFakerRetryTick = Environment.TickCount64 + 3000;
            return false;
        }
    }

    private bool EnsureVmouseConnected()
    {
        if (_vmouseHandle != IntPtr.Zero) return true;
        _vmouseHandle = TryOpenVmouse();
        DebugTrace.Write("VirtualHID", $"vmouse connect={_vmouseHandle != IntPtr.Zero}.");
        return _vmouseHandle != IntPtr.Zero;
    }

    private static string? FindFakerInputDll()
    {
        var directCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "FakerInputDll.dll"),
            Path.Combine(AppContext.BaseDirectory, "FakerInput.dll"),
            Path.Combine(AppContext.BaseDirectory, "Drivers", "FakerInput", "FakerInputDll.dll"),
            Path.Combine(AppContext.BaseDirectory, "Drivers", "FakerInput", "FakerInput.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ryochan7", "FakerInput", "FakerInputDll.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ryochan7", "FakerInput", "FakerInput.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ryochan7", "FakerInput", "FakerInputDll.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ryochan7", "FakerInput", "FakerInput.dll"),
        };

        foreach (var path in directCandidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsCompatibleFakerDll(path))
                    return path;
            }
            catch { }
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            AppContext.BaseDirectory,
        };

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)))
        {
            try
            {
                var direct = Directory.EnumerateFiles(root, "FakerInputDll.dll", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(root, "FakerInput.dll", SearchOption.AllDirectories))
                    .FirstOrDefault(IsCompatibleFakerDll);
                if (direct != null) return direct;
            }
            catch { }
        }
        return null;
    }

    private static bool IsCompatibleFakerDll(string path)
    {
        lock (DllLock)
        {
            try
            {
                if (!NativeLibrary.TryLoad(path, out var handle))
                {
                    LogIncompatibleFakerDll(path, "could not load");
                    return false;
                }

                try
                {
                    foreach (var export in RequiredFakerExports)
                    {
                        if (!NativeLibrary.TryGetExport(handle, export, out _))
                        {
                            LogIncompatibleFakerDll(path, $"missing export {export}");
                            return false;
                        }
                    }
                    return true;
                }
                finally
                {
                    try { NativeLibrary.Free(handle); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogIncompatibleFakerDll(path, ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }
    }

    private static void LogIncompatibleFakerDll(string path, string reason)
    {
        if (LoggedIncompatibleFakerDlls.Add(path))
            DebugTrace.Write("VirtualHID", $"Skipped incompatible FakerInput DLL '{path}': {reason}.");
    }

    private IntPtr TryOpenVmouse()
    {
        try
        {
            HidD_GetHidGuid(out var guid);
            const int present = 0;
            if (CM_Get_Device_Interface_List_Size(out var len, ref guid, null, present) != 0 || len <= 1)
                return IntPtr.Zero;
            var buffer = new char[len];
            if (CM_Get_Device_Interface_List(ref guid, null, buffer, len, present) != 0)
                return IntPtr.Zero;
            foreach (var path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!path.Contains(@"hid#mouse_device&col02", StringComparison.OrdinalIgnoreCase))
                    continue;
                var handle = CreateFile(path, GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                    return handle;
            }
        }
        catch (Exception ex)
        {
            DebugTrace.Write("VirtualHID", "vmouse open failed.", ex);
        }
        return IntPtr.Zero;
    }

    private void MoveWithFaker(int x, int y)
    {
        for (int i = 0; i < 220; i++)
        {
            if (!GetCursorPos(out var p)) break;
            var dx = Math.Clamp(x - p.X, -100, 100);
            var dy = Math.Clamp(y - p.Y, -100, 100);
            if (Math.Abs(x - p.X) <= 3 && Math.Abs(y - p.Y) <= 3) return;
            FakerRelativeMouse(_fakerHandle, 0, (short)dx, (short)dy, 0, 0);
            Thread.Sleep(1);
        }
    }

    private void MoveWithVmouse(int x, int y)
    {
        for (int i = 0; i < 260; i++)
        {
            if (!GetCursorPos(out var p)) break;
            var dx = Math.Clamp(x - p.X, -100, 100);
            var dy = Math.Clamp(y - p.Y, -100, 100);
            if (Math.Abs(x - p.X) <= 4 && Math.Abs(y - p.Y) <= 4) return;
            SendVmouseReport(0, (sbyte)dx, (sbyte)dy);
            Thread.Sleep(1);
        }
    }

    private bool SendVmouseReport(byte buttons, sbyte x, sbyte y)
    {
        if (_vmouseHandle == IntPtr.Zero) return false;
        var report = new[] { VmouseReportId, buttons, unchecked((byte)x), unchecked((byte)y) };
        return HidD_SetOutputReport(_vmouseHandle, report, report.Length);
    }

    private static byte KeyToHidUsage(string? key)
    {
        key = (key ?? "").Trim().ToUpperInvariant();
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z') return (byte)(0x04 + (c - 'A'));
            if (c is >= '1' and <= '9') return (byte)(0x1E + (c - '1'));
            if (c == '0') return 0x27;
        }
        if (key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key[1..], out var f) && f is >= 1 and <= 12)
            return (byte)(0x3A + (f - 1));
        return key switch
        {
            "ENTER" => 0x28,
            "ESC" or "ESCAPE" => 0x29,
            "BACK" or "BACKSPACE" => 0x2A,
            "TAB" => 0x2B,
            "SPACE" => 0x2C,
            "PAGEUP" => 0x4B,
            "PAGEDOWN" => 0x4E,
            "LEFT" => 0x50,
            "RIGHT" => 0x4F,
            "UP" => 0x52,
            "DOWN" => 0x51,
            _ => 0
        };
    }


    public void Dispose()
    {
        lock (_lock)
        {
            if (_fakerHandle != IntPtr.Zero)
            {
                try { if (_fakerConnected) FakerDisconnect(_fakerHandle); } catch { }
                try { FakerFree(_fakerHandle); } catch { }
                _fakerHandle = IntPtr.Zero;
                _fakerConnected = false;
            }
            if (_vmouseHandle != IntPtr.Zero)
            {
                try { CloseHandle(_vmouseHandle); } catch { }
                _vmouseHandle = IntPtr.Zero;
            }
        }
    }
}
