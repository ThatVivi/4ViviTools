using System.IO;
using System.Text.Json;

namespace FourRVivi.Core.Services;

public sealed class Profile
{
    public string Name { get; set; } = "Default";
    public Dictionary<string, string> Values { get; set; } = new();
}

public interface IProfileService
{
    IReadOnlyList<string> List();
    Profile Load(string name);
    void Save(Profile profile);
    void Delete(string name);
    string Export(string name);
    void Import(string json);
}

/// <summary>Named profiles as JSON under %AppData%/4rVivi/Profiles, with export/import.</summary>
public sealed class ProfileService : IProfileService
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private readonly string _dir;

    public ProfileService()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "Profiles");
        try { Directory.CreateDirectory(_dir); } catch { }
    }

    public IReadOnlyList<string> List()
    {
        try { return Directory.GetFiles(_dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Cast<string>().ToList(); }
        catch { return new List<string>(); }
    }

    public Profile Load(string name)
    {
        try
        {
            string p = Path.Combine(_dir, name + ".json");
            return File.Exists(p) ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(p)) ?? new Profile { Name = name } : new Profile { Name = name };
        }
        catch { return new Profile { Name = name }; }
    }

    public void Save(Profile profile)
    {
        try { File.WriteAllText(Path.Combine(_dir, profile.Name + ".json"), JsonSerializer.Serialize(profile, Opt)); } catch { }
    }

    public void Delete(string name)
    {
        try { var p = Path.Combine(_dir, name + ".json"); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    public string Export(string name) => JsonSerializer.Serialize(Load(name), Opt);

    public void Import(string json)
    {
        try { var p = JsonSerializer.Deserialize<Profile>(json); if (p != null) Save(p); } catch { }
    }
}
