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
    public float DetBoxThresh { get; set; } = 0.30f;
    public float DetBoxScoreThresh { get; set; } = 0.60f;
    public float DetUnclipRatio { get; set; } = 1.50f;
    public float TextScore { get; set; } = 0.50f;
    public int MaxSideLen { get; set; } = 960;
    public int CpuThreads { get; set; } = 0;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "en";   // "en" | "ar"
    public string AccentHex { get; set; } = "#7C6CF7";
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
    public List<ChainMacro> Macros { get; set; } = new();
    public List<BuffTimer> BuffTimers { get; set; } = new();

    public ProfileConfig GetActiveProfile() =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];
}
