namespace FourRVivi.Core.Game;

/// <summary>A discovered address for one role, optionally expressed as module+offset for stability.</summary>
public sealed class SavedAddress
{
    public long Runtime { get; set; }
    public int? ModuleOffset { get; set; }
    public string Type { get; set; } = "Int32";
    public IntPtr Resolve(IntPtr moduleBase) =>
        ModuleOffset is int off && moduleBase != IntPtr.Zero ? (IntPtr)((long)moduleBase + off) : (IntPtr)Runtime;
}

/// <summary>Per-profile map of role -> saved address (HP, MaxHP, SP, MaxSP, custom...).</summary>
public sealed class MemoryAddressBook
{
    public Dictionary<string, SavedAddress> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Has(string role) => Entries.ContainsKey(role);
    public SavedAddress? Get(string role) => Entries.TryGetValue(role, out var a) ? a : null;
    public void Set(string role, SavedAddress a) => Entries[role] = a;
}
