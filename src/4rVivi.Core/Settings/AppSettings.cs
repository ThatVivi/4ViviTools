using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using FourRVivi.Core.Trackers;

namespace FourRVivi.Core.Settings;

public sealed class PotConfig
{
    public bool Enabled { get; set; }
    public string Key { get; set; } = "F1";
    public int Percent { get; set; } = 50;
    public int Flat { get; set; }
    public bool UseSp { get; set; }
    public int ReactionMs { get; set; } = 150;
    public int UseDelayMs { get; set; } = 600;
}

public sealed class ProfileConfig
{
    public string Name { get; set; } = "Default";
    public MemoryAddressBook Addresses { get; set; } = new();
    public List<PotConfig> Pots { get; set; } = new();
    public List<string> PreferredProcessNames { get; set; } = new() { "ragexe", "ragexere", "4ragexe" };
}

public sealed class OcrTuningConfig
{
    public float DetBoxThresh { get; set; } = 0.15f;      // guide Stage 3 RO: det_db_thresh
    public float DetBoxScoreThresh { get; set; } = 0.20f; // guide Stage 3 RO: det_db_box_thresh
    public float DetUnclipRatio { get; set; } = 2.50f;    // guide Stage 3 RO: det_db_unclip_ratio
    public float TextScore { get; set; } = 0.20f;
    public int MaxSideLen { get; set; } = 960;
    public bool DoAngle { get; set; } = false;       // angle classifier off: HUD text never rotates; avoids 6/9 flips
    public int LimitSideLen { get; set; } = 960;      // detector short-side target (Det.limit_side_len)
    public int ImgResize { get; set; } = 0;           // 0 = adaptive short-side resize (upscales tiny text to LimitSideLen)
    // Tuned for Vivi's PC (i7-11700K, 8 physical cores). ONNX Runtime scales best at ~physical-core
    // count for the small HP/SP text crops; more threads than cores hurts. 0 = ORT auto.
    public int CpuThreads { get; set; } = 8;
}

public sealed class OcrTogglesConfig
{
    public bool DetectText { get; set; }
    public bool DetectMonsters { get; set; }
    public bool DetectSkills { get; set; }
    public bool DetectMovement { get; set; } = true;
    public bool ZoneScan { get; set; } = true;
    public int ZoneCols { get; set; } = 2;
    public int ZoneRows { get; set; } = 2;
    public bool MultiPass { get; set; }
    public bool WindowsForNumbers { get; set; }
    public bool Ensemble { get; set; }
    public bool SkipUnchanged { get; set; } = true;
    public bool GrfNamesAbove { get; set; }
    public int IconCellPx { get; set; } = 32;
    public double OverlayValuesSize { get; set; } = 1.0;
    public double EntityMinScore { get; set; } = 0.55;
    public double TextMinScore { get; set; } = 0.30;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "en";   // "en" | "ar"
    public string AccentHex { get; set; } = "#7C6CF7";
    public string Theme { get; set; } = "Dark";   // "Dark" | "Light"
    public int WindowOpacity { get; set; } = 100;   // 70..100
    public bool HumanizeTiming { get; set; } = true;
    public bool AcrylicBackdrop { get; set; } = true;
    public string ActiveProfile { get; set; } = "Default";
    public List<ProfileConfig> Profiles { get; set; } = new() { new ProfileConfig() };
    public Dictionary<string, string> ExternalToolPaths { get; set; } = new();
    public string DivinePrideImageUrl { get; set; } = "https://static.divine-pride.net/images/mobs/png/{id}.png";
    public string DivinePrideApiKey { get; set; } = "";
    public string GameFolder { get; set; } = "";
    public string GrfPath { get; set; } = "";
    public bool DiscordEnabled { get; set; } = true;   // on by default (uses built-in app id)
    public string DiscordAppId { get; set; } = "";
    public string DiscordWebsiteUrl { get; set; } = "";
    public string DiscordServerName { get; set; } = "Eldrynn RO";
    public List<FourRVivi.Core.Ocr.OcrMark> OcrMarks { get; set; } = new();
    public OcrTuningConfig OcrTuning { get; set; } = new();
    public OcrTogglesConfig OcrToggles { get; set; } = new();
    public List<ChainMacro> Macros { get; set; } = new();
    public List<BuffTimer> BuffTimers { get; set; } = new();

    public ProfileConfig GetActiveProfile() =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];
}
