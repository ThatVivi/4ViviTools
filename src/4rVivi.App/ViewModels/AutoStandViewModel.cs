using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

/// <summary>Auto-Stand config (drives AutoStandEngine). OCR-driven: reads a "Posture"/"State" text box
/// (stands when it reads "sit"), or optionally on motion-dwell from the character-motion box.</summary>
public sealed partial class AutoStandViewModel : ViewModelBase
{
    private readonly EngineHub _hub;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _standKey = "Insert";
    [ObservableProperty] private bool _useMotion;
    [ObservableProperty] private int _motionThreshold = 2;
    [ObservableProperty] private int _dwellMs = 2000;
    [ObservableProperty] private int _cooldownMs = 3000;

    public AutoStandViewModel(EngineHub hub)
    {
        _hub = hub;
        var e = _hub.AutoStand;
        e.StandKey = StandKey; e.UseMotion = UseMotion; e.MotionThreshold = MotionThreshold;
        e.DwellMs = DwellMs; e.CooldownMs = CooldownMs;
    }

    partial void OnEnabledChanged(bool value) => _hub.AutoStand.Enabled = value;
    partial void OnStandKeyChanged(string value) => _hub.AutoStand.StandKey = value;
    partial void OnUseMotionChanged(bool value) => _hub.AutoStand.UseMotion = value;
    partial void OnMotionThresholdChanged(int value) => _hub.AutoStand.MotionThreshold = value;
    partial void OnDwellMsChanged(int value) => _hub.AutoStand.DwellMs = value;
    partial void OnCooldownMsChanged(int value) => _hub.AutoStand.CooldownMs = value;
}
