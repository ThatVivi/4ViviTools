using System.Net.Http;

namespace FourRVivi.Core.Trackers;

/// <summary>Downloads + caches item / equipment / non-monster icons from divine-pride
/// (https://www.divine-pride.net/img/items/item/jRO/{id}). Runs on the user's machine.</summary>
public sealed class ItemIconService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public string CacheDir { get; }
    public string UrlTemplate { get; set; } = "https://www.divine-pride.net/img/items/item/jRO/{id}";

    public ItemIconService()
    {
        CacheDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "item_icons");
        Directory.CreateDirectory(CacheDir);
    }

    public string LocalPath(int id) => System.IO.Path.Combine(CacheDir, id + ".png");

    /// <summary>Local cached file path if present (non-empty), else null. Synchronous, safe for UI.</summary>
    public string? CachedPath(int id)
    {
        if (id <= 0) return null;
        var p = LocalPath(id);
        return File.Exists(p) && new FileInfo(p).Length > 0 ? p : null;
    }

    /// <summary>Returns the local file path if cached or downloaded, else null.</summary>
    public async Task<string?> EnsureIconAsync(int id)
    {
        if (id <= 0) return null;
        string path = LocalPath(id);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        try
        {
            string url = UrlTemplate.Replace("{id}", id.ToString());
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch { return null; }
    }
}
