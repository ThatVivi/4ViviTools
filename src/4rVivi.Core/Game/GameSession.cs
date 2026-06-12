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
        var a = AddressBook.Get(role);
        if (a is null || !Reader.Attached) return null;
        return Reader.ReadInt32(a.Resolve(Reader.ModuleBase));
    }
    public bool HasRole(string role) => AddressBook.Has(role);

    public IntPtr WindowHandle => Process?.WindowHandle ?? IntPtr.Zero;
    public void Dispose() => Reader.Dispose();
}
