using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
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
    [ObservableProperty] private bool _clickToMove;
    [ObservableProperty] private bool _clickAttack;
    [ObservableProperty] private int _moveRadius;
    [ObservableProperty] private bool _useVision;
    [ObservableProperty] private bool _hardwareClick;
    public string[] InputMethods { get; } = { "SendInput  (AHK Send/Click)", "mouse/keybd_event  (AHK SendEvent)", "PostMessage  (AHK ControlSend)" };
    [ObservableProperty] private int _inputMethodIndex;
    partial void OnInputMethodIndexChanged(int value) => _hub.InputMethod = (FourRVivi.Core.Input.InputMethod)value;
    [ObservableProperty] private string _addressStatus = "";

    // gear / ammo
    [ObservableProperty] private string _weaponKey = "";
    [ObservableProperty] private string _ammoKey = "";
    [ObservableProperty] private bool _equipOnStart;
    [ObservableProperty] private int _stopAtAmmo;

    // roam box
    [ObservableProperty] private bool _useWalkBox;
    [ObservableProperty] private int _boxX, _boxY, _boxW, _boxH;

    // auto-reconnect
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private string _reconnectKeys = "";
    [ObservableProperty] private string _targetMap = "";
    [ObservableProperty] private string _ammoName = "";
    [ObservableProperty] private string _attackSkill = "";

    public string[] Keys { get; } = KeyList.Common;
    public ObservableCollection<MonsterRule> Monsters { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    /// <summary>Trained monster names (from the icon model labels) for the auto-kill picker's search.</summary>
    public System.Collections.Generic.IReadOnlyList<string> MonsterNames { get; } = LoadLabelCategory("mob__");
    public System.Collections.Generic.IReadOnlyList<string> SkillNames { get; } = LoadLabelCategory("skills__");
    public System.Collections.Generic.IReadOnlyList<string> MapNames { get; } = LoadMapNames();
    public System.Collections.Generic.IReadOnlyList<string> AmmoNames { get; } = LoadLabelCategory("itemsbyname__");

    private static System.Collections.Generic.IReadOnlyList<string> LoadMapNames()
    {
        try
        {
            string bd = System.AppContext.BaseDirectory;
            foreach (var d in new[]
            {
                System.IO.Path.Combine(bd, "OcrServer", "models", "icons"),
                System.IO.Path.Combine(bd, "models", "icons"),
            })
            {
                var jf = System.IO.Path.Combine(d, "map_names.json");
                if (!System.IO.File.Exists(jf)) continue;
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(jf));
                var set = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject()) set.Add(prop.Name);
                if (set.Count > 0) return new System.Collections.Generic.List<string>(set);
            }
        }
        catch { }
        return LoadLabelCategory("map__");
    }

    private static System.Collections.Generic.IReadOnlyList<string> LoadLabelCategory(string prefix)
    {
        try
        {
            string bd = System.AppContext.BaseDirectory;
            foreach (var d in new[]
            {
                System.IO.Path.Combine(bd, "OcrServer", "models", "icons"),
                System.IO.Path.Combine(bd, "models", "icons"),
            })
            {
                var lab = System.IO.Path.Combine(d, "labels.txt");
                if (!System.IO.File.Exists(lab)) continue;
                var set = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var line in System.IO.File.ReadLines(lab))
                {
                    int t = line.IndexOf('\t');
                    if (t < 0) continue;
                    var name = line.Substring(t + 1).Trim();
                    if (name.StartsWith(prefix, System.StringComparison.Ordinal)) set.Add(name.Substring(prefix.Length));
                }
                if (set.Count > 0) return new System.Collections.Generic.List<string>(set);
            }
        }
        catch { }
        return System.Array.Empty<string>();
    }

    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) { _hub.SmartBot.Enabled = value; }

    public SmartBotViewModel(EngineHub hub, GameSession session)
    {
        _hub = hub; _session = session;
        var b = hub.SmartBot;
        _attackKey = b.AttackKey; _lootKey = b.LootKey; _teleportKey = b.TeleportKey; _returnKey = b.ReturnKey;
        _fleeAtHpPercent = b.FleeAtHpPercent; _stuckSeconds = b.StuckSeconds;
        _returnAtWeightPercent = b.ReturnAtWeightPercent; _rotationMs = b.RotationMs;
        _clickToMove = b.ClickToMove; _clickAttack = b.ClickAttack; _moveRadius = b.MoveRadius;
        _useVision = b.UseVision; _hardwareClick = b.HardwareClick;
        _weaponKey = b.WeaponKey; _ammoKey = b.AmmoKey; _equipOnStart = b.EquipOnStart; _stopAtAmmo = b.StopAtAmmo;
        _useWalkBox = b.UseWalkBox; _boxX = b.BoxX; _boxY = b.BoxY; _boxW = b.BoxW; _boxH = b.BoxH;
        _autoReconnect = b.AutoReconnect; _reconnectKeys = string.Join(", ", b.ReconnectKeys);
        _targetMap = b.TargetMap; _ammoName = b.AmmoName; _attackSkill = b.AttackSkill;
        foreach (var m in b.Monsters) Monsters.Add(m);
        BotLog.Instance.Added += OnBotLog;
        RefreshAddresses();
    }

    private void OnBotLog(BotLogEntry e) => Dispatcher.UIThread.Post(() =>
    {
        Logs.Insert(0, $"{e.Stamp}  [{e.Kind}]  {e.Text}");
        while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    });

    partial void OnAttackKeyChanged(string value) => _hub.SmartBot.AttackKey = value;
    partial void OnLootKeyChanged(string value) => _hub.SmartBot.LootKey = value;
    partial void OnTeleportKeyChanged(string value) => _hub.SmartBot.TeleportKey = value;
    partial void OnReturnKeyChanged(string value) => _hub.SmartBot.ReturnKey = value;
    partial void OnFleeAtHpPercentChanged(int value) => _hub.SmartBot.FleeAtHpPercent = value;
    partial void OnStuckSecondsChanged(int value) => _hub.SmartBot.StuckSeconds = Math.Max(2, value);
    partial void OnReturnAtWeightPercentChanged(int value) => _hub.SmartBot.ReturnAtWeightPercent = value;
    partial void OnRotationMsChanged(int value) => _hub.SmartBot.RotationMs = Math.Max(80, value);
    partial void OnClickToMoveChanged(bool value) => _hub.SmartBot.ClickToMove = value;
    partial void OnClickAttackChanged(bool value) => _hub.SmartBot.ClickAttack = value;
    partial void OnMoveRadiusChanged(int value) => _hub.SmartBot.MoveRadius = Math.Max(20, value);
    partial void OnUseVisionChanged(bool value) => _hub.SmartBot.UseVision = value;
    partial void OnHardwareClickChanged(bool value) => _hub.SmartBot.HardwareClick = value;
    partial void OnWeaponKeyChanged(string value) => _hub.SmartBot.WeaponKey = value;
    partial void OnAmmoKeyChanged(string value) => _hub.SmartBot.AmmoKey = value;
    partial void OnEquipOnStartChanged(bool value) => _hub.SmartBot.EquipOnStart = value;
    partial void OnStopAtAmmoChanged(int value) => _hub.SmartBot.StopAtAmmo = Math.Max(0, value);
    partial void OnUseWalkBoxChanged(bool value) => _hub.SmartBot.UseWalkBox = value;
    partial void OnBoxXChanged(int value) => _hub.SmartBot.BoxX = value;
    partial void OnBoxYChanged(int value) => _hub.SmartBot.BoxY = value;
    partial void OnBoxWChanged(int value) => _hub.SmartBot.BoxW = value;
    partial void OnBoxHChanged(int value) => _hub.SmartBot.BoxH = value;
    partial void OnAutoReconnectChanged(bool value) => _hub.SmartBot.AutoReconnect = value;
    partial void OnTargetMapChanged(string value) => _hub.SmartBot.TargetMap = value ?? "";
    partial void OnAmmoNameChanged(string value) => _hub.SmartBot.AmmoName = value ?? "";
    partial void OnAttackSkillChanged(string value) => _hub.SmartBot.AttackSkill = value ?? "";

    partial void OnReconnectKeysChanged(string value)
    {
        _hub.SmartBot.ReconnectKeys.Clear();
        foreach (var k in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.SmartBot.ReconnectKeys.Add(k);
    }

    [RelayCommand] private void ClearAllKeys()
    {
        _hub.ClearAllKeys();                                   // every feature: bot, pot, spammer, buffs, macros...
        AttackKey = ""; LootKey = ""; TeleportKey = ""; ReturnKey = "";
        WeaponKey = ""; AmmoKey = ""; ReconnectKeys = ""; Rotation = "";
        foreach (var m in Monsters) m.SkillKey = "";
        AddressStatus = "Cleared all hotkeys across every feature.";
    }

    [RelayCommand] private void ApplyRotation()
    {
        _hub.SmartBot.SkillRotation.Clear();
        foreach (var k in Rotation.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.SmartBot.SkillRotation.Add(k);
    }

    [RelayCommand] private void AddMonster() => Monsters.Add(new MonsterRule { Name = "monster", Attack = true });
    [RelayCommand] private void RemoveMonster(MonsterRule? m) { if (m != null) Monsters.Remove(m); }

    [RelayCommand] private void ApplyMonsters()
    {
        _hub.SmartBot.Monsters.Clear();
        foreach (var m in Monsters) _hub.SmartBot.Monsters.Add(m);
        AddressStatus = $"Applied {Monsters.Count} monster rule(s).";
    }

    [RelayCommand] private void ClearLog() { Logs.Clear(); BotLog.Instance.Clear(); }

    [RelayCommand] private void RefreshAddresses()
    {
        string Mark(string role) => _session.HasRole(role) ? "✓" : "✗";
        AddressStatus =
            $"HP {Mark(Roles.Hp)}   EXP {Mark(Roles.Exp)}   Weight {Mark(Roles.Weight)}/{Mark(Roles.MaxWeight)}   Pos {Mark(Roles.PosX)}/{Mark(Roles.PosY)}   Ammo {Mark(Roles.Ammo)}   " +
            "— set missing ones in the Scanner (bot still works best-effort without them).";
    }
}
