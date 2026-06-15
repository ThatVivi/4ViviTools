using System.Reflection;
using System.Text;
using FourRVivi.Core.Grf;

namespace FourRVivi.Core.Data;

/// <summary>Maps item id -> client resource name (from idnum2itemresnametable, CP949) and resolves
/// the on-disk icon path inside the user's game folder. Icons load at runtime from the user's data.</summary>
public sealed class IconService
{
    private readonly Dictionary<int, string> _res = new();
    public string GameFolder { get; set; } = "";
    public string GrfPath { get; set; } = "";
    private Grf.GrfArchive? _grf; private bool _grfTried;

    private Grf.GrfArchive? Grf()
    {
        if (_grfTried) return _grf;
        _grfTried = true;
        try { if (!string.IsNullOrEmpty(GrfPath) && File.Exists(GrfPath)) _grf = new Grf.GrfArchive(GrfPath); } catch { }
        return _grf;
    }
    private static string Cp949To1252(string s)
    {
        try { return Encoding.GetEncoding(1252).GetString(Encoding.GetEncoding(949).GetBytes(s)); } catch { return s; }
    }
    private string UiFolder => Cp949To1252("유저인터페이스"); // 유저인터페이스

    /// <summary>Item icon bytes straight from the GRF (no extraction), or null.</summary>
    public byte[]? ItemIconData(int id)
    {
        var g = Grf(); var rn = ResName(id);
        if (g is null || rn is null) return null;
        foreach (var sub in new[] { "item", "collection" })
        {
            var key = $"data/texture/{UiFolder}/{sub}/{Cp949To1252(rn)}.bmp";
            var d = g.Extract(key);
            if (d is not null) return d;
        }
        return null;
    }
    /// <summary>Skill icon bytes from the GRF, named by AEGIS id, or null.</summary>
    public byte[]? SkillIconData(string aegis)
    {
        var g = Grf();
        if (g is null || string.IsNullOrEmpty(aegis)) return null;
        return g.Extract($"data/texture/{UiFolder}/item/{aegis.ToLowerInvariant()}.bmp");
    }

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

    /// <summary>Path to a skill icon BMP (named by the skill's AEGIS id, lowercased), or null.</summary>
    public string? SkillIconPath(string aegis)
    {
        if (string.IsNullOrEmpty(GameFolder) || string.IsNullOrEmpty(aegis)) return null;
        string p = Path.Combine(GameFolder, "data", "texture", "유저인터페이스", "item", aegis.ToLowerInvariant() + ".bmp");
        return File.Exists(p) ? p : null;
    }

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
