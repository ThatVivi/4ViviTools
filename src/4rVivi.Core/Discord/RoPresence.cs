using System.Collections.Generic;
namespace FourRVivi.Core.Discord;

/// <summary>Live character snapshot used to build a Discord Rich Presence. All values come
/// from the running client via bound memory roles. The strings below show the FORMAT only:
///   DetailsLine -> "Lv {base}/{job} {class}"   e.g. "Lv 99/70 Lord Knight"
///   StateLine   -> "{map} ({x},{y})"           e.g. "Prontera (155,180)"
///   large text  -> "{char} - {server}"         e.g. "MyChar - Eldrynn RO"
/// Nothing here is hardcoded text; empty fields are simply omitted.</summary>
public sealed class RoPresence
{
    public string CharName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int BaseLevel { get; set; }
    public int JobLevel { get; set; }
    public string MapName { get; set; } = "";
    public string MapDisplay { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string ServerName { get; set; } = "";
    public int PartySize { get; set; }
    public int PartyMax { get; set; }
    public string LargeImageKey { get; set; } = "logo";
    public string SmallImageKey { get; set; } = "";
    public int HpPct { get; set; }
    public int SpPct { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Sp { get; set; }
    public int MaxSp { get; set; }
    public int BaseExpPct { get; set; }
    public int JobExpPct { get; set; }
    public string Activity { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";

    public string DetailsLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(CharName)) parts.Add(CharName);
            if (BaseLevel > 0 || JobLevel > 0) parts.Add($"Lv {BaseLevel}/{JobLevel}");
            if (!string.IsNullOrWhiteSpace(ClassName)) parts.Add(ClassName);
            return parts.Count > 0 ? string.Join(" \u2022 ", parts) : "Playing Ragnarok Online";
        }
    }

    public string StateLine
    {
        get
        {
            string map = string.IsNullOrWhiteSpace(MapDisplay) ? MapName : MapDisplay;
            string place = string.IsNullOrWhiteSpace(map) ? "" : ((X > 0 || Y > 0) ? $"{map} ({X},{Y})" : map);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Activity)) parts.Add(Activity);
            if (MaxHp > 0) parts.Add($"HP {Hp}/{MaxHp}");
            if (MaxSp > 0) parts.Add($"SP {Sp}/{MaxSp}");
            if (BaseExpPct > 0 || JobExpPct > 0) parts.Add($"EXP {BaseExpPct}%/{JobExpPct}%");
            if (place.Length > 0) parts.Add(place);
            return string.Join(" | ", parts);
        }
    }
}
