using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FourRVivi.Core.Ocr;

/// <summary>Guide §8 Stage 2/6 + Stage 15 — Region-Specific Pipelines. Each RO region (map name,
/// monster/target name, inventory, chat, stats) wants its own preprocessing + detector thresholds
/// instead of one global pipeline. Defaults below are the guide's exact recommended values; an optional
/// Data/RegionProfiles.json next to the binary overrides them without a rebuild.</summary>
public sealed class RegionProfile
{
    public double Scale { get; set; } = 2.0;            // upscale factor (guide: 3x for tiny text)
    public double Clahe { get; set; } = 2.0;            // CLAHE clip limit (guide: 4.0 for monster/map)
    public bool Threshold { get; set; }                 // hard Otsu threshold
    public bool AdaptiveThreshold { get; set; }         // local adaptive threshold (inventory)
    public bool Lab { get; set; }                       // convert to LAB before CLAHE (guide Stage 6)
    public bool Close { get; set; }                     // morphological close 2x2 (monster names)
    public double DetThresh { get; set; } = 0.15;       // det_db_thresh
    public double DetBoxThresh { get; set; } = 0.20;    // det_db_box_thresh
    public double Unclip { get; set; } = 2.50;          // det_db_unclip_ratio
}

public sealed class RegionProfiles
{
    private readonly Dictionary<string, RegionProfile> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public RegionProfile Default { get; private set; } = new();

    public RegionProfiles()
    {
        // guide-exact baked-in defaults (work even if the json isn't shipped)
        _map["MapName"]     = new RegionProfile { Scale = 3, Clahe = 4.0, Lab = true,  DetThresh = 0.15, DetBoxThresh = 0.20, Unclip = 2.50 };
        _map["MonsterName"] = new RegionProfile { Scale = 3, Clahe = 4.0, Lab = true,  Close = true, DetThresh = 0.10, DetBoxThresh = 0.15, Unclip = 3.00 };
        _map["TargetName"]  = new RegionProfile { Scale = 3, Clahe = 4.0, Lab = true,  Close = true, DetThresh = 0.10, DetBoxThresh = 0.15, Unclip = 3.00 };
        _map["Inventory"]   = new RegionProfile { Scale = 3, Clahe = 2.0, AdaptiveThreshold = true,    DetThresh = 0.15, DetBoxThresh = 0.20, Unclip = 2.50 };
        _map["Chat"]        = new RegionProfile { Scale = 3, Clahe = 2.0, DetThresh = 0.10, DetBoxThresh = 0.15, Unclip = 3.00 };
        _map["Stat"]        = new RegionProfile { Scale = 3, Clahe = 2.0, Threshold = true, DetThresh = 0.15, DetBoxThresh = 0.20, Unclip = 2.50 };
        Default             = new RegionProfile { Scale = 2, Clahe = 2.0, DetThresh = 0.15, DetBoxThresh = 0.20, Unclip = 2.50 };
        TryLoadOverride();
    }

    /// <summary>Profile for a region name; falls back to Default. Role aliases (HP/SP/Name/Monster) map
    /// onto the nearest guide region so callers can pass raw mark roles.</summary>
    public RegionProfile For(string region)
    {
        region = (region ?? "").Trim();
        if (_map.TryGetValue(region, out var p)) return p;
        // alias common mark roles onto guide regions
        string a = region.ToLowerInvariant();
        if (a.Contains("map")) return _map["MapName"];
        if (a.Contains("monster") || a.Contains("mob")) return _map["MonsterName"];
        if (a.Contains("target")) return _map["TargetName"];
        if (a.Contains("item") || a.Contains("inv")) return _map["Inventory"];
        if (a.Contains("chat")) return _map["Chat"];
        if (a.Contains("name") || a.Contains("class")) return _map["MapName"];
        return Default;
    }

    private void TryLoadOverride()
    {
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "RegionProfiles.json"),
            Path.Combine(AppContext.BaseDirectory, "RegionProfiles.json"),
        })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var rp = ReadProfile(prop.Value);
                    if (prop.NameEquals("Default")) Default = rp;
                    _map[prop.Name] = rp;
                }
                return;
            }
            catch { }
        }
    }

    private static RegionProfile ReadProfile(JsonElement e)
    {
        var p = new RegionProfile();
        double D(string k, double def) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
        bool B(string k) => e.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.False ? false : false));
        p.Scale = D("Scale", p.Scale);
        p.Clahe = D("Clahe", p.Clahe);
        p.Threshold = B("Threshold");
        p.AdaptiveThreshold = B("AdaptiveThreshold");
        p.Lab = B("Lab");
        p.Close = B("Close");
        p.DetThresh = D("DetThresh", p.DetThresh);
        p.DetBoxThresh = D("DetBoxThresh", p.DetBoxThresh);
        p.Unclip = D("Unclip", p.Unclip);
        return p;
    }
}
