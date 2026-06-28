using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using FourRVivi.Core.Game;

namespace FourRVivi.App.ViewModels;

/// <summary>Faithful in-app copy of RO-Tools v1.6.1 (dark layout). Drives the SAME engines as our
/// native tabs, but reads HP/SP/name from PaddleOCR (LiveStats) instead of memory addresses.</summary>
public sealed partial class RoToolsShellViewModel : ViewModelBase
{
    // Re-used engine view models (one source of truth).
    public AutopotViewModel HpSp { get; }
    public BuffsViewModel Buffs { get; }
    public SkillsViewModel SkillBuff { get; }
    public ClassSkillsViewModel Spammer { get; }
    public MacrosViewModel Macros { get; }
    public BotFarmViewModel Bot { get; }
    public SmartBotViewModel Smart { get; }
    public OverlayViewModel Overlay { get; }
    public StatsViewModel Stats { get; }
    public AutoYggViewModel Ygg { get; }
    public AutoStandViewModel AutoStand { get; }

    // Master ON switch for the whole window (fans out to every engine it hosts).
    [ObservableProperty] private bool _toolEnabled;

    // Live header read from OCR (no memory addresses).
    [ObservableProperty] private string _charName = "-";
    [ObservableProperty] private string _hpText = "-";
    [ObservableProperty] private string _spText = "-";
    [ObservableProperty] private string _ocrState = "OCR: waiting";

    // Auto-Element selector (cosmetic config the original exposes per-element)
    public string[] AttackElements { get; } =
        { "Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Shadow", "Ghost", "Undead" };
    [ObservableProperty] private string _selectedElement = "Neutral";
    [ObservableProperty] private string _elementKey = "";

    private readonly DispatcherTimer _timer;

    public RoToolsShellViewModel(AutopotViewModel hpSp, BuffsViewModel buffs, SkillsViewModel skillBuff,
                                 ClassSkillsViewModel spammer, MacrosViewModel macros, BotFarmViewModel bot,
                                 SmartBotViewModel smart, OverlayViewModel overlay, StatsViewModel stats,
                                 AutoYggViewModel ygg, AutoStandViewModel autoStand)
    {
        HpSp = hpSp; Buffs = buffs; SkillBuff = skillBuff; Spammer = spammer; Macros = macros;
        Bot = bot; Smart = smart; Overlay = overlay; Stats = stats; Ygg = ygg; AutoStand = autoStand;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => RefreshHeader();
        _timer.Start();
    }

    private void RefreshHeader()
    {
        var ls = LiveStats.Instance;
        OcrState = ls.IsFresh ? "OCR: live (PaddleOCR)" : "OCR: waiting — start the OCR Reader";
        var name = ls.GetText("CharName");
        CharName = string.IsNullOrWhiteSpace(name) ? "-" : name;
        HpText = (ls.TryGetNumber("HP", out var hp) & ls.TryGetNumber("MaxHP", out var mhp)) && mhp > 0
            ? $"{hp} / {mhp}" : (ls.TryGetNumber("HP", out var hp2) ? hp2.ToString() : "-");
        SpText = (ls.TryGetNumber("SP", out var sp) & ls.TryGetNumber("MaxSP", out var msp)) && msp > 0
            ? $"{sp} / {msp}" : (ls.TryGetNumber("SP", out var sp2) ? sp2.ToString() : "-");
    }

    partial void OnToolEnabledChanged(bool value)
    {
        HpSp.Enabled = value;
        Buffs.Enabled = value;
        SkillBuff.Enabled = value;
        Spammer.Enabled = value;
        Macros.Enabled = value;
        Bot.Enabled = value;
        Smart.Enabled = value;
        Overlay.Enabled = value;
        Ygg.Enabled = value;
        AutoStand.Enabled = value;
    }
}
