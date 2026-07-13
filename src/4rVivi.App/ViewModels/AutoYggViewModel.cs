using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

/// <summary>ro-tools "Auto Ygg" config (drives AutoYggEngine) — OCR HP/SP driven.</summary>
public sealed partial class AutoYggViewModel : ViewModelBase
{
    private readonly EngineHub _hub;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _key = "F10";
    [ObservableProperty] private int _hpPercent = 25;
    [ObservableProperty] private int _spPercent;       // 0 = ignore SP
    [ObservableProperty] private int _cooldownMs = 1500;

    public AutoYggViewModel(EngineHub hub)
    {
        _hub = hub;
        var e = _hub.AutoYgg;
        e.Key = Key; e.HpPercent = HpPercent; e.SpPercent = SpPercent; e.CooldownMs = CooldownMs;
    }

    partial void OnEnabledChanged(bool value) => _hub.AutoYgg.Enabled = value;
    partial void OnKeyChanged(string value) => _hub.AutoYgg.Key = value;
    partial void OnHpPercentChanged(int value) => _hub.AutoYgg.HpPercent = value;
    partial void OnSpPercentChanged(int value) => _hub.AutoYgg.SpPercent = value;
    partial void OnCooldownMsChanged(int value) => _hub.AutoYgg.CooldownMs = value;
}
