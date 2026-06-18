using System.Text.Json;

namespace FourRVivi.Core.Signatures;

/// <summary>Persists signature profiles to %AppData%/4rVivi/Profiles/signatures.json, merged with an
/// optional read-only seed shipped with the app. Export/Import allow sharing via the Marketplace.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string Path { get; }
    private Dictionary<string, SignatureProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public ProfileStore()
    {
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "Profiles");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "signatures.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(Path))
                _profiles = JsonSerializer.Deserialize<Dictionary<string, SignatureProfile>>(File.ReadAllText(Path))
                            ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _profiles = new(StringComparer.OrdinalIgnoreCase); }
        // merge shipped seed without overwriting user edits
        try
        {
            string seed = System.IO.Path.Combine(AppContext.BaseDirectory, "Signatures", "signatures.json");
            if (File.Exists(seed))
            {
                var s = JsonSerializer.Deserialize<Dictionary<string, SignatureProfile>>(File.ReadAllText(seed));
                if (s != null) foreach (var kv in s) _profiles.TryAdd(kv.Key, kv.Value);
            }
        }
        catch { }
    }

    public SignatureProfile? Find(string clientId)
        => !string.IsNullOrEmpty(clientId) && _profiles.TryGetValue(clientId, out var p) ? p : null;

    public void Save(SignatureProfile profile)
    {
        if (string.IsNullOrEmpty(profile.ClientId)) return;
        _profiles[profile.ClientId] = profile;
        try { File.WriteAllText(Path, JsonSerializer.Serialize(_profiles, Opt)); } catch { }
    }

    public string Export(SignatureProfile profile) => JsonSerializer.Serialize(profile, Opt);

    public SignatureProfile? Import(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<SignatureProfile>(json);
            if (p != null && !string.IsNullOrEmpty(p.ClientId)) { Save(p); return p; }
        }
        catch { }
        return null;
    }
}
