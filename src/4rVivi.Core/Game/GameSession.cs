using System.Diagnostics;
using FourRVivi.Core.Common;
using FourRVivi.Core.Memory;

namespace FourRVivi.Core.Game;

/// <summary>Shared, observable state: the attached process, active profile, and address book.
/// Every automation engine reads from here so a single ON/OFF and process pick drives all of them.</summary>
public sealed class GameSession : IDisposable
{
    public MemoryReader Reader { get; } = new();
    public MemoryAddressBook AddressBook { get; private set; } = new();
    public HealthReader Health { get; private set; }

    public string ProfileName { get; private set; } = "Default";
    public bool MasterEnabled { get; private set; }
    public GameProcess? Process { get; private set; }

    public event Action? Changed;
    public event Action<bool>? MasterToggled;

    public GameSession() => Health = new HealthReader(Reader, AddressBook);

    public OpResult Attach(Process p)
    {
        var r = Reader.Attach(p);
        if (r) { Process = GameProcess.From(p); Changed?.Invoke(); }
        return r;
    }

    public OpResult Reattach()
    {
        if (Process is null) return OpResult.Fail("No process selected — pick it in the top bar.");
        try { return Attach(System.Diagnostics.Process.GetProcessById(Process.Pid)); }
        catch (Exception e) { return OpResult.Fail("Re-attach failed: " + e.Message); }
    }

    public void UseProfile(string name, MemoryAddressBook book)
    {
        ProfileName = name;
        AddressBook = book;
        Health = new HealthReader(Reader, AddressBook);
        Changed?.Invoke();
    }

    public void SetMaster(bool on)
    {
        if (MasterEnabled == on) return;
        MasterEnabled = on;
        MasterToggled?.Invoke(on);
    }

    /// <summary>Read an Int32 from a bound role address, or null if unknown/not attached.</summary>
    public int? ReadRole(string role)
    {
        if (LiveStats.Instance.TryGetNumber(role, out int live)) return live;   // OCR mode wins when fresh
        var a = AddressBook.Get(role);
        if (a is null || !Reader.Attached) return null;
        return Reader.ReadInt32(a.Resolve(Reader.ModuleBase));
    }

    /// <summary>Read a fixed-length string from a bound role address (e.g. CharName, MapName).</summary>
    public string ReadRoleString(string role, int len = 24)
    {
        var live = LiveStats.Instance.GetText(role);
        if (!string.IsNullOrEmpty(live)) return live;   // OCR mode
        var a = AddressBook.Get(role);
        if (a is null || !Reader.Attached) return "";
        return Reader.ReadString(a.Resolve(Reader.ModuleBase), len);
    }

    public bool HasRole(string role) => AddressBook.Has(role);

    public IntPtr WindowHandle => RefreshWindowHandle();

    public IntPtr RefreshWindowHandle()
    {
        if (Process is null)
            return IntPtr.Zero;
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(Process.Pid);
            var hwnd = p.MainWindowHandle;
            if (hwnd != IntPtr.Zero && hwnd != Process.WindowHandle)
            {
                Process = GameProcess.From(p);
                Changed?.Invoke();
            }
        }
        catch { }

        return Process?.WindowHandle ?? IntPtr.Zero;
    }

    public void Dispose() => Reader.Dispose();
}
