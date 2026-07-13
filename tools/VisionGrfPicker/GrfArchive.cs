using System.IO;
using System.IO.Compression;
using System.Text;

namespace VisionGrfPicker;

/// <summary>Minimal GRF 0x200 reader/writer. Reader accepts any magic; writer emits the
/// standard "Master of Magic" so the RO client loads it, writes header+body+table in one
/// forward pass (never half-finalized), and verifies by re-parsing the header.</summary>
public sealed class GrfArchive
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int off, int comp, int real, int flags)> _entries = new();
    private static Encoding Cp949 => Encoding.GetEncoding(949);

    private GrfArchive(byte[] data) { _data = data; }

    public static GrfArchive Open(string path)
    {
        var g = new GrfArchive(File.ReadAllBytes(path));
        g.ReadTable();
        return g;
    }

    public int Count => _entries.Count;
    public IReadOnlyCollection<string> Names => _entries.Keys;

    private void ReadTable()
    {
        if (_data.Length < 46) throw new InvalidDataException("file too small for a GRF");
        int tableOffset = U32(30), seed = U32(34), rawCount = U32(38);
        int count = Math.Max(0, rawCount - seed - 7);
        int pos = 46 + tableOffset;
        int compLen = U32(pos), realLen = U32(pos + 4);
        byte[] table = ZlibDecompress(_data, pos + 8, compLen);
        int o = 0;
        for (int i = 0; i < count; i++)
        {
            int end = Array.IndexOf(table, (byte)0, o);
            if (end < 0) break;
            string name = Cp949.GetString(table, o, end - o);
            o = end + 1;
            int comp = U32(table, o), real = U32(table, o + 8), flags = table[o + 12], off = U32(table, o + 13);
            o += 17;
            _entries[Norm(name)] = (off, comp, real, flags);
        }
    }

    public bool Has(string internalPath) => _entries.ContainsKey(Norm(internalPath));

    public byte[] Read(string internalPath)
    {
        if (!_entries.TryGetValue(Norm(internalPath), out var e)) throw new FileNotFoundException(internalPath);
        if ((e.flags & 0x02) != 0) throw new InvalidDataException($"{internalPath} is encrypted; extract with GRFEditor first");
        return ZlibDecompress(_data, 46 + e.off, e.comp);
    }

    // ---- writer ----
    public static void Write(IReadOnlyDictionary<string, byte[]> entries, string outPath)
    {
        var body = new MemoryStream();
        var rows = new List<(string name, int off, int comp, int real, int flags)>();
        foreach (var key in entries.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            byte[] comp = ZlibCompress(entries[key]);
            int off = (int)body.Length;
            body.Write(comp, 0, comp.Length);
            rows.Add((key.Replace('/', '\\'), off, comp.Length, entries[key].Length, 1));
        }
        var table = new MemoryStream();
        foreach (var r in rows)
        {
            byte[] nb = Cp949.GetBytes(r.name);
            table.Write(nb, 0, nb.Length); table.WriteByte(0);
            WU32(table, r.comp); WU32(table, r.comp); WU32(table, r.real);
            table.WriteByte((byte)r.flags); WU32(table, r.off);
        }
        byte[] tableRaw = table.ToArray();
        byte[] tableComp = ZlibCompress(tableRaw);
        int tableOffset = (int)body.Length;

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        // write to a temp file first so a locked target never corrupts/wastes the build
        string tmp = outPath + ".tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            byte[] magic = new byte[15];
            Encoding.ASCII.GetBytes("Master of Magic").CopyTo(magic, 0);
            f.Write(magic, 0, 15);
            f.Write(new byte[15], 0, 15);                       // key (zeros)
            WU32(f, tableOffset); WU32(f, 0); WU32(f, rows.Count + 7); WU32(f, 0x200);
            body.Position = 0; body.CopyTo(f);
            WU32(f, tableComp.Length); WU32(f, tableRaw.Length);
            f.Write(tableComp, 0, tableComp.Length);
        }
        Verify(tmp, rows.Count);
        try
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmp, outPath);
        }
        catch (IOException)
        {
            throw new IOException($"'{Path.GetFileName(outPath)}' is locked (close GRFEditor and the RO client). " +
                                  $"The finished GRF is saved here — rename it once the file is free:\n{tmp}");
        }
    }

    private static void Verify(string path, int expect)
    {
        var d = File.ReadAllBytes(path);
        if (Encoding.ASCII.GetString(d, 0, 15) != "Master of Magic") throw new InvalidDataException("verify: bad magic");
        int tableOffset = U32(d, 30), seed = U32(d, 34), rawCount = U32(d, 38), version = U32(d, 42);
        if (version != 0x200) throw new InvalidDataException($"verify: version 0x{version:X}");
        int count = Math.Max(0, rawCount - seed - 7);
        if (count != expect) throw new InvalidDataException($"verify: count {count} != {expect}");
        int pos = 46 + tableOffset;
        ZlibDecompress(d, pos + 8, U32(d, pos));               // throws if the table is corrupt
    }

    // ---- helpers ----
    private static string Norm(string p) => p.Replace('/', '\\').Trim().ToLowerInvariant();
    private int U32(int o) => U32(_data, o);
    private static int U32(byte[] d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
    private static void WU32(Stream s, int v)
    { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }

    private static byte[] ZlibDecompress(byte[] src, int offset, int len)
    {
        using var inMs = new MemoryStream(src, offset, len);
        using var z = new ZLibStream(inMs, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        z.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var outMs = new MemoryStream();
        using (var z = new ZLibStream(outMs, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        return outMs.ToArray();
    }
}
