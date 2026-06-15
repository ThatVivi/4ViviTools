using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using Avalonia;
using Avalonia.Platform;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using FourRVivi.Core.Data;

namespace FourRVivi.App.Services;

/// <summary>Icons load from the bundled pack first (works with NO GRF), then optionally from the
/// user's GRF or loose game folder. Cached as Avalonia bitmaps.</summary>
public sealed class IconImageService
{
    private readonly IconService _icons;
    private readonly Dictionary<string, AvBitmap?> _cache = new();
    private readonly Dictionary<string, byte[]> _pack = new(StringComparer.OrdinalIgnoreCase);
    public static IconImageService? Instance { get; set; }

    public IconImageService(IconService icons) { _icons = icons; LoadPack(); }
    public void SetGameFolder(string folder) => _icons.GameFolder = folder;
    public void SetGrf(string grfPath) => _icons.GrfPath = grfPath;

    private void LoadPack()
    {
        try
        {
            using var src = AssetLoader.Open(new Uri("avares://4rVivi/Assets/iconpack.zip"));
            using var mem = new MemoryStream();
            src.CopyTo(mem); mem.Position = 0;
            using var zip = new ZipArchive(mem, ZipArchiveMode.Read);
            foreach (var e in zip.Entries)
            {
                if (e.Name.Length == 0) continue;
                using var es = e.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                _pack[e.FullName.Replace('\\', '/')] = ms.ToArray();
            }
        }
        catch { }
    }

    public AvBitmap? Get(int id) =>
        FromCache("i" + id, $"items/{id}.png", () => _icons.ItemIconData(id), () => _icons.ItemIconPath(id));

    public AvBitmap? GetSkill(string aegis) =>
        FromCache("s" + aegis, $"skills/{aegis.ToLowerInvariant()}.png", () => _icons.SkillIconData(aegis), () => _icons.SkillIconPath(aegis));

    private AvBitmap? FromCache(string key, string packKey, Func<byte[]?> grf, Func<string?> file)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;
        AvBitmap? result = null;
        try
        {
            if (_pack.TryGetValue(packKey, out var png))          // bundled PNG (already transparent)
            {
                using var ms = new MemoryStream(png);
                result = new AvBitmap(ms);
            }
            else result = FromBmp(grf(), file());                  // GRF / loose BMP fallback
        }
        catch { }
        _cache[key] = result;
        return result;
    }

    private static AvBitmap? FromBmp(byte[]? data, string? path)
    {
        using var src = data is not null ? new MemoryStream(data) : null;
        using System.Drawing.Bitmap? sys = src is not null ? new System.Drawing.Bitmap(src)
                                          : path is not null ? new System.Drawing.Bitmap(path) : null;
        if (sys is null) return null;
        sys.MakeTransparent(System.Drawing.Color.Magenta);
        using var ms = new MemoryStream();
        sys.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return new AvBitmap(ms);
    }
}
