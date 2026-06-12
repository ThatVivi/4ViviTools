using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;

namespace FourRVivi.App.ViewModels;

public sealed partial class SmartBotViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly GameSession _session;

    [ObservableProperty] private string _attackKey;
    [ObservableProperty] private string _lootKey;
    [ObservableProperty] private string _teleportKey;
    [ObservableProperty] private string _returnKey;
    [ObservableProperty] private string _rotation = "";
    [ObservableProperty] private int _fleeAtHpPercent;
    [ObservableProperty] private int _stuckSeconds;
    [ObservableProperty] private int _returnAtWeightPercent;
    [ObservableProperty] private int _rotationMs;
    [ObservableProperty] private string _addressStatus = "";

    public string[] Keys { get; } = KeyList.Common;

    public SmartBotViewModel(EngineHub hub, GameSession session)
    {
        _hub = hub; _session = session;
        var b = hub.SmartBot;
        _attackKey = b.AttackKey; _lootKey = b.LootKey; _teleportKey = b.TeleportKey; _returnKey = b.ReturnKey;
        _fleeAtHpPercent = b.FleeAtHpPercent; _stuckSeconds = b.StuckSeconds;
        _returnAtWeightPercent = b.ReturnAtWeightPercent; _rotationMs = b.RotationMs;
        RefreshAddresses();
    }

    partial void OnAttackKeyChanged(string value) => _hub.SmartBot.AttackKey = value;
    partial void OnLootKeyChanged(string value) => _hub.SmartBot.LootKey = value;
    partial void OnTeleportKeyChanged(string value) => _hub.SmartBot.TeleportKey = value;
    partial void OnReturnKeyChanged(string value) => _hub.SmartBot.ReturnKey = value;
    partial void OnFleeAtHpPercentChanged(int value) => _hub.SmartBot.FleeAtHpPercent = value;
    partial void OnStuckSecondsChanged(int value) => _hub.SmartBot.StuckSeconds = Math.Max(2, value);
    partial void OnReturnAtWeightPercentChanged(int value) => _hub.SmartBot.ReturnAtWeightPercent = value;
    partial void OnRotationMsChanged(int value) => _hub.SmartBot.RotationMs = Math.Max(80, value);

    [RelayCommand] private void ApplyRotation()
    {
        _hub.SmartBot.SkillRotation.Clear();
        foreach (var k in Rotation.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.SmartBot.SkillRotation.Add(k);
    }

    [RelayCommand] private void RefreshAddresses()
    {
        string Mark(string role) => _session.HasRole(role) ? "✓" : "✗";
        AddressStatus =
            $"HP {Mark(Roles.Hp)}   EXP {Mark(Roles.Exp)}   Weight {Mark(Roles.Weight)}/{Mark(Roles.MaxWeight)}   Pos {Mark(Roles.PosX)}/{Mark(Roles.PosY)}   " +
            "— set missing ones in the Scanner (bot still works on a best-effort basis without them).";
    }
}
