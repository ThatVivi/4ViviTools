using System.IO;
using System.Text.Json;

namespace FourRVivi.Core.Configuration;

public interface IConfigService
{
    T Load<T>(string file) where T : new();
    void Save<T>(string file, T data);
}

/// <summary>Typed JSON config under %AppData%/4rVivi/Config. Never throws.</summary>
public sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private readonly string _dir;

    public ConfigService()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "Config");
        try { Directory.CreateDirectory(_dir); } catch { }
    }

    public T Load<T>(string file) where T : new()
    {
        try
        {
            string p = Path.Combine(_dir, file);
            return File.Exists(p) ? JsonSerializer.Deserialize<T>(File.ReadAllText(p)) ?? new() : new();
        }
        catch { return new(); }
    }

    public void Save<T>(string file, T data)
    {
        try { File.WriteAllText(Path.Combine(_dir, file), JsonSerializer.Serialize(data, Opt)); } catch { }
    }
}
