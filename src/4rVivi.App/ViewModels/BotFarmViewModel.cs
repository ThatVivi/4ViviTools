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

    partial void OnAttackKeyChanged(string v) => _hub.BotFarm.AttackKey = v;
    partial void OnLootKeyChanged(string v) => _hub.BotFarm.LootKey = v;
    partial void OnFleeAtHpPercentChanged(int v) => _hub.BotFarm.FleeAtHpPercent = v;
    partial void OnRotationMsChanged(int v) => _hub.BotFarm.RotationMs = Math.Max(50, v);
}
