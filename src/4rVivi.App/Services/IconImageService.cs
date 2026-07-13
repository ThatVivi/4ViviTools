using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using Avalonia;
using Avalonia.Platform;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using FourRVivi.Core.Data;
using FourRVivi.Core.Trackers;

namespace FourRVivi.App.Services;

/// <summary>Icons load from the bundled pack first (works with NO GRF), then optionally from the
/// user's GRF or loose game folder. Cached as Avalonia bitmaps.</summary>
public sealed class IconImageService
{
    private readonly IconService _icons;
    private readonly Dictionary<string, AvBitmap?> _cache = new();
    private readonly Dictionary<string, byte[]> _pack = new(StringComparer.OrdinalIgnoreCase);
    public static IconImageService? Instance { get; set; }

    /// <summary>Optional network item-icon source (divine-pride). Set at startup.</summary>
    public ItemIconService? ItemNet { get; set; }
    /// <summary>Optional network skill-icon source (divine-pride, by skill id). Set at startup.</summary>
    public SkillIconService? SkillNet { get; set; }

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

    public AvBitmap? Get(int id)
    {
        var b = FromCache("i" + id, $"items/{id}.png", () => _icons.ItemIconData(id), () => _icons.ItemIconPath(id));
        return b ?? GetItemNetwork(id);
    }

    /// <summary>Divine-pride item image fallback: load from disk cache if present; otherwise kick off a
    /// background download so it shows on the next render. Returns null until cached.</summary>
    private AvBitmap? GetItemNetwork(int id)
    {
        if (ItemNet == null || id <= 0) return null;
        var key = "n" + id;
        if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var path = ItemNet.CachedPath(id);
        if (path != null)
        {
            try { var bmp = new AvBitmap(path); _cache[key] = bmp; return bmp; } catch { }
        }
        _ = ItemNet.EnsureIconAsync(id);   // fire-and-forget; appears next render
        return null;
    }

    public AvBitmap? GetSkill(string aegis) =>
        FromCache("s" + aegis, $"skills/{aegis.ToLowerInvariant()}.png", () => _icons.SkillIconData(aegis), () => _icons.SkillIconPath(aegis));

    /// <summary>Skill icon by aegis (pack/GRF) with a divine-pride network fallback by skill id.</summary>
    public AvBitmap? GetSkill(string aegis, int id)
        => GetSkill(aegis) ?? GetSkillNet(id);

    private AvBitmap? GetSkillNet(int id)
    {
        if (SkillNet == null || id <= 0) return null;
        var key = "sn" + id;
        if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var path = SkillNet.CachedPath(id);
        if (path != null)
        {
            try { var bmp = new AvBitmap(path); _cache[key] = bmp; return bmp; } catch { }
        }
        _ = SkillNet.EnsureIconAsync(id);
        return null;
    }

    /// <summary>Usable-item icon by lowercase name (e.g. "red_potion"), bundled from the 4rTools/ro-tools packs. No GRF needed.</summary>
    /// <summary>Resolves a display name to an item id (set at startup from GameDatabase) so icons
    /// can be looked up by name via the GRF / divine-pride pipeline.</summary>
    public Func<string, int>? NameToId { get; set; }
    /// <summary>Resolves a skill DISPLAY name to (aegis, id) so skill icons load by name.</summary>
    public Func<string, (string aegis, int id)?>? SkillByName { get; set; }

    public AvBitmap? GetSkillByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || SkillByName == null) return null;
        try { var r = SkillByName(name); return r is { } v ? GetSkill(v.aegis, v.id) : null; } catch { return null; }
    }

    public AvBitmap? GetItemByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("(no", StringComparison.OrdinalIgnoreCase)) return null;
        var key = "bn" + name;
        if (_cache.TryGetValue(key, out var c) && c != null) return c;
        AvBitmap? result = null;
        try
        {
            if (_pack.TryGetValue($"itemsbyname/{name.ToLowerInvariant()}.png", out var png))
            {
                using var ms = new MemoryStream(png);
                result = new AvBitmap(ms);
            }
        }
        catch { }
        // Fall back to name -> id -> GRF/divine-pride icon.
        if (result == null && NameToId != null)
        {
            try { var id = NameToId(name); if (id > 0) result = Get(id); } catch { }
        }
        if (result != null) _cache[key] = result;   // only cache hits, so async net icons retry
        return result;
    }

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
           try
        {
            if (src == null && (path == null || !File.Exists(path))) return null;
            using var bmp = src != null ? new Bitmap(src) : new Bitmap(path!);
            using var argb = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    argb.SetPixel(x, y, (c.R > 240 && c.G < 16 && c.B > 240) ? Color.Transparent : c);   // RO magenta key
                }
            using var outMs = new MemoryStream();
            argb.Save(outMs, ImageFormat.Png);
            outMs.Position = 0;
            return new AvBitmap(outMs);
        }
        catch { return null; }
    }
}
