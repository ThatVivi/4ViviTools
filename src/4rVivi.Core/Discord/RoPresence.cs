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
    public string Activity { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";

    public string DetailsLine
    {
        get
        {
            string lv = (BaseLevel > 0 || JobLevel > 0) ? $"Lv {BaseLevel}/{JobLevel}" : "";
            return string.Join(" ", new[] { lv, ClassName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public string StateLine
    {
        get
        {
            string map = string.IsNullOrWhiteSpace(MapDisplay) ? MapName : MapDisplay;
            string place = string.IsNullOrWhiteSpace(map) ? "" : ((X > 0 || Y > 0) ? $"{map} ({X},{Y})" : map);
            string head = string.IsNullOrWhiteSpace(Activity) ? place : (string.IsNullOrWhiteSpace(place) ? Activity : $"{Activity} - {place}");
            return head;
        }
    }
}
