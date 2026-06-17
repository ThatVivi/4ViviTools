using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FourRVivi.Core.Services;

public sealed record ReleaseInfo(string Version, string Url, string? Notes);

public interface IUpdateService
{
    Task<ReleaseInfo?> CheckAsync(string ownerRepo);
    bool IsNewer(string latest, string current);
}

/// <summary>Checks the latest GitHub release for a newer version. Pairs with the CI version.json/SHA256.</summary>
public sealed class UpdateService : IUpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<ReleaseInfo?> CheckAsync(string ownerRepo)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{ownerRepo}/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("4rVivi", "1.0"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            string url = root.TryGetProperty("html_url", out var u) ? (u.GetString() ?? "") : "";
            string? notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return string.IsNullOrEmpty(tag) ? null : new ReleaseInfo(tag.TrimStart('v', 'V'), url, notes);
        }
        catch { return null; }
    }

    public bool IsNewer(string latest, string current)
    {
        static Version Parse(string s) { Version.TryParse(new string(s.Where(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.'), out var v); return v ?? new Version(0, 0); }
        return Parse(latest) > Parse(current);
    }
}
