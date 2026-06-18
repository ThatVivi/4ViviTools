using System.Diagnostics;
using FourRVivi.Core.Memory;

namespace FourRVivi.Core.Signatures;

public sealed class RoleBinding
{
    public PointerPath Path { get; set; } = new();
    public string Type { get; set; } = "Int32";
}

/// <summary>A per-client recipe: for each role, how to resolve its address. Keyed by client identity.</summary>
public sealed class SignatureProfile
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Dictionary<string, RoleBinding> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>exeName|fileSize|fileVersion — stable across launches, distinguishes client builds.</summary>
    public static string Identify(MemoryReader r)
    {
        try
        {
            var m = r.Target?.MainModule;
            if (m is null) return "";
            string name = m.ModuleName ?? "game.exe";
            long size = 0; string ver = "";
            try { size = new FileInfo(m.FileName!).Length; } catch { }
            try { ver = FileVersionInfo.GetVersionInfo(m.FileName!).FileVersion ?? ""; } catch { }
            return $"{name}|{size}|{ver}";
        }
        catch { return ""; }
    }
}
