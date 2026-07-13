using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

/// <summary>Faithful 4RTools "ATK x DEF" tab — drives AtkDefEngine (OCR/attach-gated).</summary>
public sealed partial class AtkDefViewModel : ViewModelBase
{
    private readonly EngineHub _hub;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _atkSwitchKey = "F5";
    [ObservableProperty] private string _defSwitchKey = "F6";
    [ObservableProperty] private int _switchDelay = 500;
    [ObservableProperty] private string _spammerKey = "F1";
    [ObservableProperty] private int _spammerDelay = 200;
    [ObservableProperty] private bool _withMouseClick;
    [ObservableProperty] private bool _atkMode = true;   // true = ATK stance, false = DEF stance

    public AtkDefViewModel(EngineHub hub)
    {
        _hub = hub;
        Push();
    }

    private void Push()
    {
        var e = _hub.AtkDef;
        e.AtkSwitchKey = AtkSwitchKey; e.DefSwitchKey = DefSwitchKey; e.SwitchDelayMs = System.Math.Max(0, SwitchDelay);
        e.SpammerKey = SpammerKey; e.SpammerDelayMs = System.Math.Max(50, SpammerDelay);
        e.WithMouseClick = WithMouseClick; e.AtkMode = AtkMode;
    }

    partial void OnEnabledChanged(bool value) => _hub.AtkDef.Enabled = value;
    partial void OnAtkSwitchKeyChanged(string value) => _hub.AtkDef.AtkSwitchKey = value;
    partial void OnDefSwitchKeyChanged(string value) => _hub.AtkDef.DefSwitchKey = value;
    partial void OnSwitchDelayChanged(int value) => _hub.AtkDef.SwitchDelayMs = System.Math.Max(0, value);
    partial void OnSpammerKeyChanged(string value) => _hub.AtkDef.SpammerKey = value;
    partial void OnSpammerDelayChanged(int value) => _hub.AtkDef.SpammerDelayMs = System.Math.Max(50, value);
    partial void OnWithMouseClickChanged(bool value) => _hub.AtkDef.WithMouseClick = value;
    partial void OnAtkModeChanged(bool value) => _hub.AtkDef.AtkMode = value;
}
