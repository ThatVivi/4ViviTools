using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Avalonia.Media.Imaging;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using FourRVivi.Core.Data;

namespace FourRVivi.App.Services;

/// <summary>Loads item icons from the user's game folder (BMP, magenta keyed) as Avalonia bitmaps, cached.</summary>
public sealed class IconImageService
{
    private readonly IconService _icons;
    private readonly Dictionary<int, AvBitmap?> _cache = new();
    public static IconImageService? Instance { get; set; }

    public IconImageService(IconService icons) { _icons = icons; }
    public void SetGameFolder(string folder) => _icons.GameFolder = folder;

    public AvBitmap? Get(int id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        AvBitmap? result = null;
        var path = _icons.ItemIconPath(id);
        if (path != null)
        {
            try
            {
                using var sys = new System.Drawing.Bitmap(path);
                sys.MakeTransparent(System.Drawing.Color.Magenta);
                using var ms = new MemoryStream();
                sys.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                result = new AvBitmap(ms);
            }
            catch { }
        }
        _cache[id] = result;
        return result;
    }
}
