using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using FourRVivi.Core.Game;

namespace FourRVivi.App.ViewModels;

/// <summary>Faithful in-app copy of 4RTools (light WinForms-style shell). Drives the SAME engines as
/// our native tabs, but reads HP/SP/levels from PaddleOCR (LiveStats) instead of memory addresses.</summary>
public sealed partial class FourRToolsShellViewModel : ViewModelBase
{
    // Re-used engine view models (one source of truth).
    public AutopotViewModel Autopot { get; }
    public BuffsViewModel Buffs { get; }
    public SkillsViewModel AtkSkills { get; }
    public ClassSkillsViewModel Spammer { get; }
    public MacrosViewModel Macros { get; }
    public AtkDefViewModel AtkDef { get; }
    public AutoStandViewModel AutoStand { get; }
    public SpammerGridViewModel SpamGrid { get; }

    // Master ON switch for the whole 4rTools window (fans out to every engine it hosts).
    [ObservableProperty] private bool _toolEnabled;

    // Live header read from OCR (no memory addresses).
    [ObservableProperty] private string _charName = "-";
    [ObservableProperty] private string _hpText = "-";
    [ObservableProperty] private string _spText = "-";
    [ObservableProperty] private string _ocrState = "OCR: waiting";

    private readonly DispatcherTimer _timer;

    public FourRToolsShellViewModel(AutopotViewModel autopot, BuffsViewModel buffs, SkillsViewModel atk,
                                    ClassSkillsViewModel spammer, MacrosViewModel macros, AtkDefViewModel atkDef,
                                    AutoStandViewModel autoStand, SpammerGridViewModel spamGrid)
    {
        Autopot = autopot; Buffs = buffs; AtkSkills = atk; Spammer = spammer; Macros = macros; AtkDef = atkDef; AutoStand = autoStand; SpamGrid = spamGrid;

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
        Autopot.Enabled = value;
        Buffs.Enabled = value;
        AtkSkills.Enabled = value;
        Spammer.Enabled = value;
        Macros.Enabled = value;
        AtkDef.Enabled = value;
        AutoStand.Enabled = value;
        SpamGrid.Enabled = value;
    }
}
