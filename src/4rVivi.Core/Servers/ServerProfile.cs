using System.Globalization;
using System.Text.Json.Serialization;

namespace FourRVivi.Core.Servers;

/// <summary>One supported client: matched by process (exe) name, with fixed absolute HP + name
/// addresses. Layout (from 4RTools): HP=[hp], MaxHP=[hp+4], SP=[hp+8], MaxSP=[hp+12]; name string at nameAddress.</summary>
public sealed class ServerProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";          // process name (no .exe)
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("hpAddress")] public string HpAddress { get; set; } = "";
    [JsonPropertyName("nameAddress")] public string NameAddress { get; set; } = "";

    public long HpAddr => ParseHex(HpAddress);
    public long NameAddr => ParseHex(NameAddress);
    public string Label => string.IsNullOrWhiteSpace(Description) ? Name : $"{Description}  ({Name})";

    public static long ParseHex(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
