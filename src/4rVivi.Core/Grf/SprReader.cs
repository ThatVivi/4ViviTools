using System.Text;

namespace FourRVivi.Core.Grf;

public sealed record SpriteFrame(int Width, int Height, byte[] Rgba);

/// <summary>Decodes .spr (indexed + RGBA frames) to RGBA images for previewing.</summary>
public static class SprReader
{
    public static List<SpriteFrame> Decode(byte[] data)
    {
        var frames = new List<SpriteFrame>();
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        if (Encoding.ASCII.GetString(br.ReadBytes(2)) != "SP") return frames;
        int minor = br.ReadByte(), major = br.ReadByte();
        double ver = major + minor / 10.0;
        int indexed = br.ReadUInt16();
        int rgbaCount = ver >= 2.1 ? br.ReadUInt16() : 0;

        // palette is 1024 bytes at end of file
        byte[] pal = new byte[1024];
        long save = ms.Position;
        if (indexed > 0 && ms.Length >= 1024) { ms.Seek(-1024, SeekOrigin.End); pal = br.ReadBytes(1024); ms.Seek(save, SeekOrigin.Begin); }

        for (int s = 0; s < indexed; s++)
        {
            int w = br.ReadUInt16(), h = br.ReadUInt16();
            byte[] idx = new byte[w * h];
            if (ver >= 2.1)
            {
                int encoded = br.ReadUInt16(); int read = 0, pos = 0;
                while (read < encoded && pos < idx.Length)
                {
                    byte c = br.ReadByte(); read++;
                    if (c == 0) { byte run = br.ReadByte(); read++; for (int k = 0; k < run && pos < idx.Length; k++) idx[pos++] = 0; }
                    else idx[pos++] = c;
                }
            }
            else { idx = br.ReadBytes(w * h); }
            frames.Add(new SpriteFrame(w, h, ToRgba(idx, w, h, pal)));
        }
        for (int s = 0; s < rgbaCount; s++)
        {
            int w = br.ReadUInt16(), h = br.ReadUInt16();
            byte[] rgba = br.ReadBytes(w * h * 4);
            frames.Add(new SpriteFrame(w, h, rgba));
        }
        return frames;
    }

    private static byte[] ToRgba(byte[] idx, int w, int h, byte[] pal)
    {
        var outp = new byte[w * h * 4];
        for (int i = 0; i < idx.Length; i++)
        {
            int c = idx[i] * 4;
            byte r = pal[c], g = pal[c + 1], b = pal[c + 2];
            int o = i * 4;
            outp[o] = r; outp[o + 1] = g; outp[o + 2] = b;
            outp[o + 3] = (byte)(idx[i] == 0 ? 0 : 255); // index 0 = transparent
        }
        return outp;
    }
}
