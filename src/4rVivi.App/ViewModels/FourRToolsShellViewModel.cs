using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Game;

namespace FourRVivi.App.ViewModels;

public sealed partial class FourRToolsShellViewModel : ViewModelBase
{
    public AutopotViewModel Autopot { get; }
    public BuffsViewModel Buffs { get; }
    public SkillsViewModel AtkSkills { get; }
    public ClassSkillsViewModel Spammer { get; }
    public MacrosViewModel Macros { get; }
    public AtkDefViewModel AtkDef { get; }
    public AutoStandViewModel AutoStand { get; }
    public SpammerGridViewModel SpamGrid { get; }

    [ObservableProperty] private bool _toolEnabled;
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
        OcrState = ls.IsFresh ? "OCR: live (PaddleOCR)" : "OCR: waiting - start the OCR Reader";
        var name = ls.GetText("CharName");
        CharName = string.IsNullOrWhiteSpace(name) ? "-" : name;
        HpText = ls.TryGetTrustedNumber(Roles.HpPercent, out var hpPct) ? $"{hpPct}%" : "-";
        SpText = ls.TryGetTrustedNumber(Roles.SpPercent, out var spPct) ? $"{spPct}%" : "-";
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
