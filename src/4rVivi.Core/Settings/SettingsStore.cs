using System.Text.Json;

namespace FourRVivi.Core.Settings;

/// <summary>Loads/saves AppSettings as JSON under %AppData%/4rVivi/settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string Path { get; }
    public AppSettings Current { get; private set; } = new();

    public SettingsStore()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        try { if (File.Exists(Path)) Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new(); }
        catch { Current = new(); }
        if (Current.Profiles.Count == 0) Current.Profiles.Add(new ProfileConfig());
    }

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(Current, Opt)); } catch { }
    }
}
