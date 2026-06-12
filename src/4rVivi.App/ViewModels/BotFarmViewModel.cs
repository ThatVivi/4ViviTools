using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

public sealed partial class BotFarmViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    [ObservableProperty] private string _attackKey;
    [ObservableProperty] private string _lootKey;
    [ObservableProperty] private int _fleeAtHpPercent;
    [ObservableProperty] private int _rotationMs;

    public BotFarmViewModel(EngineHub hub)
    {
        _hub = hub;
        var b = hub.BotFarm;
        _attackKey = b.AttackKey; _lootKey = b.LootKey;
        _fleeAtHpPercent = b.FleeAtHpPercent; _rotationMs = b.RotationMs;
    }

    partial void OnAttackKeyChanged(string value) => _hub.BotFarm.AttackKey = value;
    partial void OnLootKeyChanged(string value) => _hub.BotFarm.LootKey = value;
    partial void OnFleeAtHpPercentChanged(int value) => _hub.BotFarm.FleeAtHpPercent = value;
    partial void OnRotationMsChanged(int value) => _hub.BotFarm.RotationMs = Math.Max(50, value);
}
