using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

public sealed partial class SkillsViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    [ObservableProperty] private string _spamKey;
    [ObservableProperty] private int _spamDelay;
    [ObservableProperty] private string _rotationKeys = "";
    [ObservableProperty] private string _status = "Spammer hits one key on a delay; rotation cycles a list. Master ON to run.";

    public SkillsViewModel(EngineHub hub)
    {
        _hub = hub;
        _spamKey = hub.Spammer.Key;
        _spamDelay = hub.Spammer.DelayMs;
    }

    partial void OnSpamKeyChanged(string value) => _hub.Spammer.Key = value;
    partial void OnSpamDelayChanged(int value) => _hub.Spammer.DelayMs = Math.Max(20, value);

    [RelayCommand] private void ApplyRotation()
    {
        _hub.Spammer.Rotation.Clear();
        foreach (var k in RotationKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.Spammer.Rotation.Add(k);
        Status = _hub.Spammer.Rotation.Count > 0
            ? $"Rotation set: {_hub.Spammer.Rotation.Count} keys."
            : "Rotation cleared — single key spam.";
    }
}
