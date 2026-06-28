using System.Net.Http;

namespace FourRVivi.Core.Trackers;

/// <summary>Downloads + caches skill icons from divine-pride
/// (https://static.divine-pride.net/images/skill/{id}.png), keyed by skill id.</summary>
public sealed class SkillIconService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public string CacheDir { get; }
    public string UrlTemplate { get; set; } = "https://static.divine-pride.net/images/skill/{id}.png";

    public SkillIconService()
    {
        CacheDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "skill_icons");
        Directory.CreateDirectory(CacheDir);
    }

    public string LocalPath(int id) => System.IO.Path.Combine(CacheDir, id + ".png");

    public string? CachedPath(int id)
    {
        if (id <= 0) return null;
        var p = LocalPath(id);
        return File.Exists(p) && new FileInfo(p).Length > 0 ? p : null;
    }

    public async Task<string?> EnsureIconAsync(int id)
    {
        if (id <= 0) return null;
        string path = LocalPath(id);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        try
        {
            using var resp = await Http.GetAsync(UrlTemplate.Replace("{id}", id.ToString()));
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch { return null; }
    }
}
