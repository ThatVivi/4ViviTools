using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Trackers;

namespace FourRVivi.Core.Settings;

public sealed class PotConfig
{
    public bool Enabled { get; set; }
    public string Key { get; set; } = "F1";
    public string Name { get; set; } = "";
    public int Percent { get; set; } = 50;
    public int Flat { get; set; }
    public bool UseSp { get; set; }
    public int ReactionMs { get; set; } = -1;
    public int UseDelayMs { get; set; } = -1;
}

public sealed class SmartSkillButtonConfig
{
    public bool Enabled { get; set; }
    public string Key { get; set; } = "";
    public string SkillName { get; set; } = "";
    public int SpRequired { get; set; }
    public int SkillLevel { get; set; } = 1;
    public bool IsSkill { get; set; }
    public bool IsBuff { get; set; }
    public bool IsTeleport { get; set; }
    public bool IsYgg { get; set; }
    public bool IsHpPot { get; set; }
    public bool IsSpPot { get; set; }
    public bool IsAmmo { get; set; }
    public bool IsAmmoBag { get; set; }
    public bool IsLoot { get; set; }
    public bool IsReturn { get; set; }
    public bool IsWeapon { get; set; }
    public bool IsReconnect { get; set; }
    public string ItemName { get; set; } = "";
    public int SkillDelayMs { get; set; } = -1;
    public int ReactionMs { get; set; } = -1;
    public int UseDelayMs { get; set; } = -1;
    public int StopAtAmmo { get; set; }
    public int AmmoCount { get; set; }
    public int AmmoBags { get; set; }
    public int AmmoPerBag { get; set; } = 500;
    public int BuffIntervalSec { get; set; } = 120;
    public int PotPercent { get; set; } = 50;
}

public sealed class ControllerKeyMapConfig
{
    public string Key { get; set; } = "";
    public string Button { get; set; } = "";
}

public sealed class SmartBotConfig
{
    public bool Enabled { get; set; }
    public string AttackKey { get; set; } = "A";
    public string LootKey { get; set; } = "X";
    public string TeleportKey { get; set; } = "Back";
    public string ReturnKey { get; set; } = "Start";
    public List<SmartSkillButtonConfig> SkillButtons { get; set; } = new();
    public List<SmartSkillButtonConfig> BuffButtons { get; set; } = new();
    public List<MonsterRule> Monsters { get; set; } = new();
    public int FleeAtHpPercent { get; set; } = 25;
    public int StuckMs { get; set; } = 0;          // 0 = migrate from legacy StuckSeconds
    public int FocusKillMs { get; set; } = 0;      // 0 = migrate from legacy FocusKillSeconds
    public int StuckSeconds { get; set; } = 8;
    public int FocusKillSeconds { get; set; } = -1;
    public int NextMonsterDelayMs { get; set; } = -1;
    public int ReturnAtWeightPercent { get; set; } = 90;
    public int RotationMs { get; set; } = -1;
    public bool SkillSpamEnabled { get; set; }
    public string SkillClickMode { get; set; } = "No mouse click";
    public string AhkMode { get; set; } = "Compatibility";
    public bool MouseFlick { get; set; }
    public bool NoShift { get; set; }
    public int BuffIntervalSec { get; set; } = 120;
    public bool ClickToMove { get; set; } = true;
    public bool ClickAttack { get; set; } = true;
    public int MoveRadius { get; set; } = 180;
    public int MoveWaitMs { get; set; } = -1;
    public int MoveStableMs { get; set; } = -1;
    public bool UseVision { get; set; } = true;
    public bool HardwareClick { get; set; } = true;
    public bool UseControllerButtons { get; set; } = true;
    public bool UseControllerCombos { get; set; } = true;
    public bool ShowControllerAssignments { get; set; }
    public bool ShowAdvancedTiming { get; set; }
    public List<ControllerKeyMapConfig> ControllerKeyMap { get; set; } = new();
    public bool AutopotEnabled { get; set; }
    public InputMethod InputMethod { get; set; } = InputMethod.Viiper;
    public string VirtualClickButton { get; set; } = "A";
    public int VirtualClickHoldMs { get; set; } = 100;
    public bool VirtualClickFallback { get; set; } = true;
    public string ToggleHotkey { get; set; } = "";
    public string StartHotkey { get; set; } = "";
    public string StopHotkey { get; set; } = "";
    public string WeaponKey { get; set; } = "LeftShoulder";
    public string AmmoKey { get; set; } = "RightShoulder";
    public bool EquipOnStart { get; set; }
    public int StopAtAmmo { get; set; }
    public bool UseWalkBox { get; set; }
    public bool ShowWalkBoxOverlay { get; set; } = true;
    public int BoxX { get; set; }
    public int BoxY { get; set; }
    public int BoxW { get; set; }
    public int BoxH { get; set; }
    public bool AutoReconnect { get; set; }
    public List<string> ReconnectKeys { get; set; } = new();
    public string TargetMap { get; set; } = "";
    public string AmmoName { get; set; } = "";
    public int AmmoCount { get; set; } = 0;
    public int AmmoBags { get; set; } = 0;
    public int AmmoPerBag { get; set; } = 500;
    public string AmmoBagKey { get; set; } = "";
    public string AttackSkill { get; set; } = "";
}

public sealed class ProfileConfig
{
    public string Name { get; set; } = "Default";
    public MemoryAddressBook Addresses { get; set; } = new();
    public List<PotConfig> Pots { get; set; } = new();
    public SmartBotConfig SmartBot { get; set; } = new();
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
    public string OnnxExecutionProvider { get; set; } = "auto"; // auto | cpu | cuda | directml
    // Tuned for Vivi's PC (i7-11700K, 8 physical cores). ONNX Runtime scales best at ~physical-core
    // count for the small HP/SP text crops; more threads than cores hurts. 0 = ORT auto.
    public int CpuThreads { get; set; } = 8;
}

public sealed class OcrTogglesConfig
{
    public bool DetectText { get; set; } = true;
    public bool DetectMonsters { get; set; } = true;
    public bool DetectSkills { get; set; } = true;
    public bool DetectMovement { get; set; } = true;
    public bool ZoneScan { get; set; } = false;
    public int ZoneCols { get; set; } = 2;
    public int ZoneRows { get; set; } = 2;
    public bool MultiPass { get; set; } = true;
    public bool WindowsForNumbers { get; set; } = true;
    public bool Ensemble { get; set; }
    public bool SkipUnchanged { get; set; } = true;
    public bool GrfNamesAbove { get; set; }
    public bool VisionAssistGrf { get; set; }
    // Legacy setting kept for profile compatibility. Runtime uses the built-in mob-id marker table.
    public string VisionAssistManifestPath { get; set; } = "";
    public int IconCellPx { get; set; } = 32;
    public double OverlayValuesSize { get; set; } = 1.0;
    public double EntityMinScore { get; set; } = FourRVivi.Core.Ocr.VisionConfig.DefaultTrackConfidence;
    public double TextMinScore { get; set; } = 0.30;
    public bool AutoEntityMinScore { get; set; } = true;
    public bool AutoTextMinScore { get; set; } = true;
    public bool FastMonsterTracking { get; set; } = true;
    public int TextScanEvery { get; set; } = -1;
    public int OverlayFrameMs { get; set; } = -1;
    public int MaxOverlayDetections { get; set; } = 40;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "en";   // "en" | "ar"
    public string AccentHex { get; set; } = "#7C6CF7";
    public string Theme { get; set; } = "Red";   // "Red" | "Black" | "Dark" | "Light"
    public int WindowOpacity { get; set; } = 100;   // 15..100
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
