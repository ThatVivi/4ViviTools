using System.Reflection;
using System.Text;

namespace FourRVivi.Core.Data;

/// <summary>Maps item id -> client resource name (from idnum2itemresnametable, CP949) and resolves
/// the on-disk icon path inside the user's game folder. Icons load at runtime from the user's data.</summary>
public sealed class IconService
{
    private readonly Dictionary<int, string> _res = new();
    public string GameFolder { get; set; } = "";

    static IconService()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
    }

    public IconService() => Load();

    private void Load()
    {
        try
        {
            var enc = Encoding.GetEncoding(949);
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("idnum2itemresnametable.txt", StringComparison.OrdinalIgnoreCase));
            if (name == null) return;
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s, enc);
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.StartsWith("//") || !line.Contains('#')) continue;
                var p = line.Split('#');
                if (p.Length > 1 && int.TryParse(p[0].Trim(), out int id) && p[1].Length > 0)
                    _res[id] = p[1];
            }
        }
        catch { }
    }

    public string? ResName(int id) => _res.TryGetValue(id, out var v) ? v : null;

    /// <summary>Path to the small inventory icon BMP for an item, or null if not found.</summary>
    public string? ItemIconPath(int id)
    {
        if (string.IsNullOrEmpty(GameFolder)) return null;
        var rn = ResName(id);
        if (rn == null) return null;
        // RO UI folder = "유저인터페이스" (user interface)
        foreach (var sub in new[] { "item", "collection" })
        {
            string p = Path.Combine(GameFolder, "data", "texture", "유저인터페이스", sub, rn + ".bmp");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
