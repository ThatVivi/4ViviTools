using System.Net.Http;

namespace FourRVivi.Core.Trackers;

/// <summary>Downloads + caches MVP/monster icons from divine-pride (runs on the user's machine).</summary>
public sealed class MvpIconService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public string CacheDir { get; }
    public string UrlTemplate { get; set; } = "https://static.divine-pride.net/images/mobs/png/{id}.png";
    public string? ApiKey { get; set; }

    public MvpIconService()
    {
        CacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "mvp_icons");
        Directory.CreateDirectory(CacheDir);
    }

    public string LocalPath(int id) => System.IO.Path.Combine(CacheDir, id + ".png");

    /// <summary>Returns the local file path if cached or downloaded, else null.</summary>
    public async Task<string?> EnsureIconAsync(int id)
    {
        if (id <= 0) return null;
        string path = LocalPath(id);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        try
        {
            string url = UrlTemplate.Replace("{id}", id.ToString());
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(ApiKey)) req.Headers.TryAddWithoutValidation("apikey", ApiKey);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch { return null; }
    }
}
