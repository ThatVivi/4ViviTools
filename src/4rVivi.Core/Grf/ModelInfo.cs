using System.Text;

namespace FourRVivi.Core.Grf;

/// <summary>Lightweight header readers for .act (animation) and .rsm (3D model) — metadata only.</summary>
public static class ModelInfo
{
    public static string Act(byte[] data)
    {
        try
        {
            using var br = new BinaryReader(new MemoryStream(data));
            if (Encoding.ASCII.GetString(br.ReadBytes(2)) != "AC") return "Not an ACT file.";
            int minor = br.ReadByte(), major = br.ReadByte();
            int actions = br.ReadUInt16();
            return $"ACT v{major}.{minor} — {actions} actions (animations).";
        }
        catch (Exception e) { return "ACT read error: " + e.Message; }
    }

    public static string Rsm(byte[] data)
    {
        try
        {
            using var br = new BinaryReader(new MemoryStream(data));
            string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "GRSM") return "Not an RSM model.";
            int major = br.ReadByte(), minor = br.ReadByte();
            return $"RSM 3D model v{major}.{minor}. (3D preview not available; use the bundled editors to view.)";
        }
        catch (Exception e) { return "RSM read error: " + e.Message; }
    }
}
