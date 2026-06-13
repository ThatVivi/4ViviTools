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
    public List<ChainMacro> Macros { get; set; } = new();
    public List<BuffTimer> BuffTimers { get; set; } = new();

    public ProfileConfig GetActiveProfile() =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];
}
