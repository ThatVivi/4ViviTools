using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VisionGrfPicker;

// ---------------------------------------------------------------------------
// Color-code: EXACT port of build_vision_grf.color_code (base-5, dominant channel).
// The manifest stores these; the runtime detector matches against them.
// ---------------------------------------------------------------------------
public static class Marker
{
    public const int BoxPx = 2;
    public const int CodeCell = 5;
    public const int CodeCells = 3;
    public static readonly int[] CodeLevels = { 48, 96, 144, 192, 240 };
    public static readonly Color BoxColor = Color.FromArgb(255, 255, 0, 0);

    public static int[][] ColorCode(int mobId)
    {
        int baseN = CodeLevels.Length;                 // 5
        int width = CodeCells * 2;                      // 6 digits
        var digits = new int[width];
        int n = mobId;
        for (int i = width - 1; i >= 0; i--) { digits[i] = n % baseN; n /= baseN; }

        var cells = new int[CodeCells][];
        for (int i = 0; i < CodeCells; i++)
        {
            int a = CodeLevels[digits[i * 2]];
            int b = CodeLevels[digits[i * 2 + 1]];
            cells[i] = i switch
            {
                0 => new[] { 255, a, b },
                1 => new[] { a, 255, b },
                _ => new[] { a, b, 255 },
            };
        }
        return cells;
    }
}

// ---------------------------------------------------------------------------
// Two folders inside one GRF:
//   data\sprite\몬스터\...         = LIVE path the game reads
//   data\sprite\visionassistant\  = baked library (beside 몬스터), game never reads it
// "Promote" copies a baked entry from the library folder to the live folder.
// ---------------------------------------------------------------------------
public static class MarkerPaths
{
    public const string LiveSeg = "\\몬스터\\";
    public const string LibSeg = "\\visionassistant\\";
    public static string ToLib(string live) => live.Replace(LiveSeg, LibSeg);
    public static string ToLive(string lib) => lib.Replace(LibSeg, LiveSeg);
    public static bool IsLib(string p) => p.Contains(LibSeg, StringComparison.OrdinalIgnoreCase);
}

// ---------------------------------------------------------------------------
// Catalog: mobId -> display name (gamedata.json) and mobId -> sprite path.
// ---------------------------------------------------------------------------
public sealed class Catalog
{
    public Dictionary<int, string> Names { get; } = new();
    public Dictionary<int, string> Sprites { get; } = new();

    public static Catalog Load(string baseDir)
    {
        var c = new Catalog();
        using (var doc = JsonDocument.Parse(ReadData(baseDir, "gamedata.json")))
        {
            if (doc.RootElement.TryGetProperty("mobs", out var mobs))
                foreach (var m in mobs.EnumerateArray())
                {
                    if (!m.TryGetProperty("id", out var idEl)) continue;
                    if (!int.TryParse(idEl.ToString(), out int id)) continue;
                    string name = m.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                    if (string.IsNullOrEmpty(name) && m.TryGetProperty("aegis", out var aEl)) name = aEl.GetString() ?? "";
                    if (string.IsNullOrEmpty(name)) name = $"mob_{id}";
                    c.Names[id] = name;
                }
        }
        using (var doc = JsonDocument.Parse(ReadData(baseDir, "mobid_sprite_map.json")))
        {
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, out int id))
                    c.Sprites[id] = (p.Value.GetString() ?? "").Replace('/', '\\');
        }
        return c;
    }

    // Prefer an external file next to the exe / in the working dir (lets a server swap data),
    // otherwise fall back to the copy embedded inside the exe.
    private static string ReadData(string baseDir, string name)
    {
        foreach (var cand in new[] { Path.Combine(baseDir, name), Path.Combine(Environment.CurrentDirectory, name), name })
            if (File.Exists(cand)) return File.ReadAllText(cand);
        var asm = typeof(Catalog).Assembly;
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"{name} not found (no external file and not embedded).");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

// ---------------------------------------------------------------------------
// SPR codec. Truecolor frames are stored BGRA + bottom-up; indexed frames use
// an RGB palette (index 0 transparent) + bottom-up. We decode to top-up ARGB
// and re-encode as truecolor v2.1 (the exact inverse), matching build_vision_grf.
// ---------------------------------------------------------------------------
public static class Spr
{
    public static List<Bitmap> Decode(byte[] d)
    {
        var frames = new List<Bitmap>();
        if (d.Length < 6 || d[0] != (byte)'S' || d[1] != (byte)'P') throw new InvalidDataException("not an SPR");
        int minor = d[2], major = d[3];
        double version = major + minor / 10.0;
        int indexedCount = U16(d, 4);
        int off = 6, rgbaCount = 0;
        if (version >= 2.0) { rgbaCount = U16(d, off); off += 2; }
        byte[] palette = indexedCount > 0 ? d[^1024..] : Array.Empty<byte>();

        for (int f = 0; f < indexedCount; f++)
        {
            int w = U16(d, off), h = U16(d, off + 2); off += 4;
            int pc = w * h;
            var idx = new List<int>(pc);
            if (version >= 2.1)
            {
                int size = U16(d, off); off += 2; int end = off + size;
                while (off < end && idx.Count < pc)
                {
                    byte c = d[off++];
                    if (c == 0 && off < end) { int run = d[off++]; for (int r = 0; r < Math.Max(1, run); r++) idx.Add(0); }
                    else idx.Add(c);
                }
                off = end;
            }
            else { for (int i = 0; i < pc; i++) idx.Add(d[off + i]); off += pc; }
            while (idx.Count < pc) idx.Add(0);

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            for (int i = 0; i < pc; i++)
            {
                int p = idx[i] * 4;
                int r = p + 2 < palette.Length ? palette[p] : 0;
                int g = p + 2 < palette.Length ? palette[p + 1] : 0;
                int b = p + 2 < palette.Length ? palette[p + 2] : 0;
                int a = idx[i] == 0 ? 0 : 255;
                bmp.SetPixel(i % w, i / w, Color.FromArgb(a, r, g, b));   // indexed frames are top-down (no flip, RGB palette)
            }
            frames.Add(bmp);
        }

        for (int f = 0; f < rgbaCount; f++)
        {
            int w = U16(d, off), h = U16(d, off + 2); off += 4;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = off + (y * w + x) * 4;
                    byte b = d[p], g = d[p + 1], r = d[p + 2], a = d[p + 3];   // stored BGRA -> swap
                    bmp.SetPixel(x, h - 1 - y, Color.FromArgb(a, r, g, b));    // flip vertical
                }
            off += w * h * 4;
            frames.Add(bmp);
        }
        return frames;
    }

    public static byte[] Encode(List<Bitmap> frames)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)'S'); ms.WriteByte((byte)'P');
        ms.WriteByte(1); ms.WriteByte(2);                 // version 2.1
        WriteU16(ms, 0); WriteU16(ms, frames.Count);      // 0 indexed + N truecolor
        foreach (var bmp in frames)
        {
            int w = bmp.Width, h = bmp.Height;
            WriteU16(ms, w); WriteU16(ms, h);
            for (int y = 0; y < h; y++)                    // inverse: bottom-up + BGRA
            {
                int sy = h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, sy);
                    ms.WriteByte(c.B); ms.WriteByte(c.G); ms.WriteByte(c.R); ms.WriteByte(c.A);
                }
            }
        }
        return ms.ToArray();
    }

    // ---- indexed path: keeps the original palette so colors are exact (what GRFEditor/client render) ----
    public static (List<(int W, int H, byte[] Idx)> frames, byte[] palette)? DecodeIndexed(byte[] d)
    {
        if (d.Length < 6 || d[0] != (byte)'S' || d[1] != (byte)'P') throw new InvalidDataException("not SPR");
        int minor = d[2], major = d[3]; double ver = major + minor / 10.0;
        int idxC = U16(d, 4); int off = 6, rgbaC = 0;
        if (ver >= 2.0) { rgbaC = U16(d, off); off += 2; }
        if (rgbaC > 0) return null;                       // truecolor original -> caller falls back
        byte[] pal = d[^1024..];
        var frames = new List<(int, int, byte[])>();
        for (int f = 0; f < idxC; f++)
        {
            int w = U16(d, off), h = U16(d, off + 2); off += 4; int pc = w * h;
            var idx = new byte[pc]; int p = 0;
            if (ver >= 2.1)
            {
                int size = U16(d, off); off += 2; int end = off + size;
                while (off < end && p < pc)
                {
                    byte c = d[off++];
                    if (c == 0 && off < end) { int run = d[off++]; for (int r = 0; r < Math.Max(1, run) && p < pc; r++) idx[p++] = 0; }
                    else idx[p++] = c;
                }
                off = end;
            }
            else { Array.Copy(d, off, idx, 0, pc); off += pc; }
            frames.Add((w, h, idx));
        }
        return (frames, pal);
    }

    public static byte[] EncodeIndexed(List<(int W, int H, byte[] Idx)> frames, byte[] palette)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)'S'); ms.WriteByte((byte)'P'); ms.WriteByte(1); ms.WriteByte(2);   // v2.1
        WriteU16(ms, frames.Count); WriteU16(ms, 0);
        foreach (var (w, h, idx) in frames)
        {
            WriteU16(ms, w); WriteU16(ms, h);
            var rle = new List<byte>();
            int i = 0, n = idx.Length;
            while (i < n)
            {
                byte v = idx[i];
                if (v == 0) { int run = 1; while (i + run < n && idx[i + run] == 0 && run < 255) run++; rle.Add(0); rle.Add((byte)run); i += run; }
                else { rle.Add(v); i++; }
            }
            WriteU16(ms, rle.Count); ms.Write(rle.ToArray(), 0, rle.Count);
        }
        ms.Write(palette, 0, 1024);
        return ms.ToArray();
    }

    private static int U16(byte[] d, int o) => d[o] | (d[o + 1] << 8);
    private static void WriteU16(Stream s, int v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)((v >> 8) & 0xFF)); }
}

// ---------------------------------------------------------------------------
// Baker: red box + color-code + TRUE name, symmetric padding (keeps sprite
// center -> .act unchanged). Exact port of build_vision_grf.bake_frame.
// ---------------------------------------------------------------------------
public static class Baker
{
    private static readonly Font NameFont = new("Arial", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    private const int NameOutlinePx = 2;

    public static Bitmap Bake(Bitmap img, int mobId, string name)
    {
        int w = img.Width, h = img.Height;
        if (w < 1 || h < 1) return img;

        int tw, th;
        using (var probe = new Bitmap(1, 1))
        using (var pg = Graphics.FromImage(probe))
        {
            var sz = string.IsNullOrEmpty(name) ? new SizeF(0, 0) : pg.MeasureString(name, NameFont);
            tw = (int)Math.Ceiling(sz.Width); th = (int)Math.Ceiling(sz.Height);
        }
        int strip = string.IsNullOrEmpty(name) ? 0 : th + 3;
        int nw = Math.Max(w, tw + 4), nh = h + 2 * strip;
        int px = (nw - w) / 2, py = strip;

        var canvas = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            g.CompositingMode = CompositingMode.SourceOver;
            g.DrawImageUnscaled(img, px, py);

            using var red = new Pen(Marker.BoxColor, 1);
            if (w > 2 * Marker.BoxPx && h > 2 * Marker.BoxPx)
                for (int i = 0; i < Marker.BoxPx; i++)
                    g.DrawRectangle(red, px + i, py + i, w - 1 - 2 * i, h - 1 - 2 * i);
            else
                g.DrawRectangle(red, px, py, w - 1, h - 1);

            if (w >= Marker.CodeCells * Marker.CodeCell + 2 * Marker.BoxPx && h >= Marker.CodeCell + 2 * Marker.BoxPx)
            {
                var code = Marker.ColorCode(mobId);
                for (int i = 0; i < Marker.CodeCells; i++)
                {
                    using var br = new SolidBrush(Color.FromArgb(255, code[i][0], code[i][1], code[i][2]));
                    g.FillRectangle(br, px + Marker.BoxPx + i * Marker.CodeCell, py + Marker.BoxPx, Marker.CodeCell, Marker.CodeCell);
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float nx = (nw - tw) / 2f;
                using var black = new SolidBrush(Color.Black);
                using var white = new SolidBrush(Color.White);
                for (int dx = -NameOutlinePx; dx <= NameOutlinePx; dx++)
                    for (int dy = -NameOutlinePx; dy <= NameOutlinePx; dy++)
                        if (dx != 0 || dy != 0) g.DrawString(name, NameFont, black, nx + dx, 1 + dy);
                g.DrawString(name, NameFont, white, nx, 1);
            }
        }
        return canvas;
    }

    // Whole-animation indexed bake: ONE fixed box/name/code sized to the biggest frame, kept in the
    // original palette so colors are exact. Frames are centered -> .act stays valid.
    public static (List<(int W, int H, byte[] Idx)> frames, byte[] palette) BakeIndexed(
        List<(int W, int H, byte[] Idx)> frames, byte[] palette, int mobId, string name)
    {
        int wb = 0, hb = 0;
        foreach (var f in frames) { if (f.W > wb) wb = f.W; if (f.H > hb) hb = f.H; }

        int tw, th;
        using (var b = new Bitmap(1, 1)) using (var g = Graphics.FromImage(b))
        {
            var sz = string.IsNullOrEmpty(name) ? new SizeF(0, 0) : g.MeasureString(name, NameFont);
            tw = (int)Math.Ceiling(sz.Width); th = (int)Math.Ceiling(sz.Height);
        }
        int strip = string.IsNullOrEmpty(name) ? 0 : th + 3;
        int wo = Math.Max(wb, tw + 4), ho = hb + 2 * strip;

        var used = new bool[256];
        foreach (var f in frames) foreach (var v in f.Idx) used[v] = true;
        var free = new Stack<int>();
        for (int i = 1; i < 256; i++) if (!used[i]) free.Push(i);
        int Take() => free.Count > 0 ? free.Pop() : 255;

        var pal = (byte[])palette.Clone();
        void SetPal(int idx, int r, int gg, int bb) { int p = idx * 4; pal[p] = (byte)r; pal[p + 1] = (byte)gg; pal[p + 2] = (byte)bb; pal[p + 3] = 255; }
        int redIdx = Take(), whiteIdx = Take(), blackIdx = Take();
        SetPal(redIdx, 255, 0, 0); SetPal(whiteIdx, 255, 255, 255); SetPal(blackIdx, 0, 0, 0);
        var codeIdx = new List<int>();
        foreach (var c in Marker.ColorCode(mobId)) { int ci = Take(); SetPal(ci, c[0], c[1], c[2]); codeIdx.Add(ci); }

        int bx0 = (wo - wb) / 2, by0 = strip, bx1 = bx0 + wb - 1, by1 = by0 + hb - 1;
        var nameMap = string.IsNullOrEmpty(name) ? new Dictionary<(int, int), int>() : NameIndices(name, wo, strip, whiteIdx, blackIdx);

        var outFrames = new List<(int, int, byte[])>();
        foreach (var (w, h, idx) in frames)
        {
            var canvas = new byte[wo * ho];               // 0 = transparent
            int ox = (wo - w) / 2, oy = (ho - h) / 2;
            for (int y = 0; y < h; y++) Array.Copy(idx, y * w, canvas, (oy + y) * wo + ox, w);
            for (int i = 0; i < Marker.BoxPx; i++)
            {
                for (int x = bx0 + i; x <= bx1 - i; x++) { canvas[(by0 + i) * wo + x] = (byte)redIdx; canvas[(by1 - i) * wo + x] = (byte)redIdx; }
                for (int y = by0 + i; y <= by1 - i; y++) { canvas[y * wo + bx0 + i] = (byte)redIdx; canvas[y * wo + bx1 - i] = (byte)redIdx; }
            }
            if (codeIdx.Count > 0 && wb >= Marker.CodeCells * Marker.CodeCell + 2 * Marker.BoxPx && hb >= Marker.CodeCell + 2 * Marker.BoxPx)
                for (int k = 0; k < codeIdx.Count; k++)
                {
                    int cx = bx0 + Marker.BoxPx + k * Marker.CodeCell, cy = by0 + Marker.BoxPx;
                    for (int yy = cy; yy < cy + Marker.CodeCell; yy++) for (int xx = cx; xx < cx + Marker.CodeCell; xx++) canvas[yy * wo + xx] = (byte)codeIdx[k];
                }
            foreach (var kv in nameMap) { var (x, y) = kv.Key; if (x >= 0 && x < wo && y >= 0 && y < ho) canvas[y * wo + x] = (byte)kv.Value; }
            outFrames.Add((wo, ho, canvas));
        }
        return (outFrames, pal);
    }

    private static Dictionary<(int, int), int> NameIndices(string name, int wo, int strip, int whiteIdx, int blackIdx)
    {
        var map = new Dictionary<(int, int), int>();
        int h = Math.Max(1, strip);
        using var bmp = new Bitmap(wo, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float tw = g.MeasureString(name, NameFont).Width; float nx = (wo - tw) / 2f;
            using var black = new SolidBrush(Color.Black); using var white = new SolidBrush(Color.White);
            for (int dx = -NameOutlinePx; dx <= NameOutlinePx; dx++) for (int dy = -NameOutlinePx; dy <= NameOutlinePx; dy++) if (dx != 0 || dy != 0) g.DrawString(name, NameFont, black, nx + dx, 1 + dy);
            g.DrawString(name, NameFont, white, nx, 1);
        }
        for (int y = 0; y < h; y++) for (int x = 0; x < wo; x++) { var c = bmp.GetPixel(x, y); if (c.A < 96) continue; map[(x, y)] = (c.R + c.G + c.B) > 300 ? whiteIdx : blackIdx; }
        return map;
    }
}
