using System.IO.Compression;
using System.Text;

namespace FourRVivi.Core.Grf;

public sealed record GrfEntry(string Name, int CompSize, int RealSize, byte Flags, int Offset)
{
    public bool IsFile => (Flags & 0x01) != 0;
    public bool Encrypted => (Flags & 0x06) != 0; // DES bits — not supported
}

/// <summary>Reads GRF v0x200 archives: list entries, extract zlib files. DES-encrypted entries unsupported.</summary>
public sealed class GrfArchive : IDisposable
{
    private readonly FileStream _fs;
    private readonly BinaryReader _br;
    public List<GrfEntry> Entries { get; } = new();
    public string Path { get; }

    public GrfArchive(string path)
    {
        Path = path;
        _fs = File.OpenRead(path);
        _br = new BinaryReader(_fs);
        ReadTable();
    }

    private void ReadTable()
    {
        var sig = Encoding.ASCII.GetString(_br.ReadBytes(15));
        if (!sig.StartsWith("Master of Magic")) throw new InvalidDataException("Not a GRF file.");
        _br.ReadBytes(15);                 // encryption key (unused)
        int tableOffset = _br.ReadInt32();
        int seed = _br.ReadInt32();
        int rawCount = _br.ReadInt32();
        int version = _br.ReadInt32();
        if (version != 0x200) throw new NotSupportedException($"GRF version 0x{version:X} not supported (need 0x200).");
        int count = rawCount - seed - 7;

        _fs.Seek(46 + tableOffset, SeekOrigin.Begin);
        int compLen = _br.ReadInt32();
        int realLen = _br.ReadInt32();
        byte[] comp = _br.ReadBytes(compLen);
        byte[] table = Inflate(comp, realLen);

        int p = 0;
        for (int i = 0; i < count && p < table.Length; i++)
        {
            int start = p;
            while (p < table.Length && table[p] != 0) p++;
            string name = Encoding.Latin1.GetString(table, start, p - start);
            p++; // null
            if (p + 17 > table.Length) break;
            int csize = BitConverter.ToInt32(table, p); p += 4;
            p += 4; // aligned size
            int rsize = BitConverter.ToInt32(table, p); p += 4;
            byte flags = table[p]; p += 1;
            int offset = BitConverter.ToInt32(table, p); p += 4;
            Entries.Add(new GrfEntry(name.Replace('\\', '/'), csize, rsize, flags, offset));
        }
    }

    public byte[]? Extract(GrfEntry e)
    {
        if (!e.IsFile || e.Encrypted) return null;
        _fs.Seek(46 + e.Offset, SeekOrigin.Begin);
        byte[] comp = _br.ReadBytes(e.CompSize);
        return Inflate(comp, e.RealSize);
    }

    private static byte[] Inflate(byte[] data, int expected)
    {
        using var ms = new MemoryStream(data);
        using var z = new ZLibStream(ms, CompressionMode.Decompress);
        var outp = new byte[expected];
        int read = 0;
        while (read < expected)
        {
            int n = z.Read(outp, read, expected - read);
            if (n <= 0) break;
            read += n;
        }
        return outp;
    }

    public void Dispose() { _br.Dispose(); _fs.Dispose(); }
}
