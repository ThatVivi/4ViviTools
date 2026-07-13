using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Win32;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Common;
using FourRVivi.Core.Data;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed class SmartSkillButton : ObservableObject
{
    private readonly Action _changed;
    private readonly Func<string, int, (string hint, int delayMs, int maxLevel)> _describe;
    private bool _enabled;
    private string _key;
    private string _skillName = "";
    private string _skillHint = "";
    private int _suggestedDelayMs;
    private int _spRequired;
    private int _skillLevel = 1;
    private int _skillMaxLevel = 1;
    private bool _isSkill;
    private bool _isBuff;
    private bool _isTeleport;
    private bool _isYgg;
    private bool _isHpPot;
    private bool _isSpPot;
    private bool _isAmmo;
    private bool _isAmmoBag;
    private bool _isLoot;
    private bool _isReturn;
    private bool _isWeapon;
    private bool _isReconnect;
    private string _itemName = "";
    private string _itemHint = "";
    private int _skillDelayMs = -1;
    private int _reactionMs = -1;
    private int _useDelayMs = -1;
    private int _stopAtAmmo;
    private int _ammoCount;
    private int _ammoBags;
    private int _ammoPerBag = 500;
    private int _buffIntervalSec = 120;
    private int _potPercent = 50;
    private string _controllerButton = "";

    public SmartSkillButton(string key, Action changed, Func<string, int, (string hint, int delayMs, int maxLevel)>? describe = null)
    {
        _key = key;
        _changed = changed;
        _describe = describe ?? ((_, _) => ("", 0, 1));
    }

    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) RefreshActionState(); _changed(); } }
    public string Key { get => _key; set { SetProperty(ref _key, value ?? ""); _changed(); } }
    public string SkillName { get => _skillName; set { if (SetProperty(ref _skillName, value ?? "")) RefreshSkillInfo(); _changed(); } }
    public string SkillHint { get => _skillHint; private set => SetProperty(ref _skillHint, value); }
    public int SuggestedDelayMs { get => _suggestedDelayMs; private set { if (SetProperty(ref _suggestedDelayMs, value)) OnPropertyChanged(nameof(HasSuggestedDelay)); } }
    public int SpRequired { get => _spRequired; set { SetProperty(ref _spRequired, Math.Max(0, value)); _changed(); } }
    public int SkillLevel { get => _skillLevel; set { if (SetProperty(ref _skillLevel, Math.Clamp(value, 1, Math.Max(1, SkillMaxLevel)))) RefreshSkillInfo(); _changed(); } }
    public int SkillMaxLevel { get => _skillMaxLevel; private set { if (SetProperty(ref _skillMaxLevel, Math.Max(1, value))) OnPropertyChanged(nameof(SkillLevelSummary)); } }
    public bool IsSkill { get => _isSkill; set => SetActionKind(nameof(IsSkill), value); }
    public bool IsBuff { get => _isBuff; set => SetActionKind(nameof(IsBuff), value); }
    public bool IsTeleport { get => _isTeleport; set => SetActionKind(nameof(IsTeleport), value); }
    public bool IsYgg { get => _isYgg; set => SetActionKind(nameof(IsYgg), value); }
    public bool IsHpPot { get => _isHpPot; set => SetActionKind(nameof(IsHpPot), value); }
    public bool IsSpPot { get => _isSpPot; set => SetActionKind(nameof(IsSpPot), value); }
    public bool IsAmmo { get => _isAmmo; set => SetActionKind(nameof(IsAmmo), value); }
    public bool IsAmmoBag { get => _isAmmoBag; set => SetActionKind(nameof(IsAmmoBag), value); }
    public bool IsLoot { get => _isLoot; set => SetActionKind(nameof(IsLoot), value); }
    public bool IsReturn { get => _isReturn; set => SetActionKind(nameof(IsReturn), value); }
    public bool IsWeapon { get => _isWeapon; set => SetActionKind(nameof(IsWeapon), value); }
    public bool IsReconnect { get => _isReconnect; set => SetActionKind(nameof(IsReconnect), value); }
    public string ItemName { get => _itemName; set { if (SetProperty(ref _itemName, value ?? "")) RefreshItemInfo(); _changed(); } }
    public string ItemHint { get => _itemHint; private set => SetProperty(ref _itemHint, value); }
    public int SkillDelayMs { get => _skillDelayMs; set { if (SetProperty(ref _skillDelayMs, value < 0 ? -1 : Math.Clamp(value, 10, 5000))) OnPropertyChanged(nameof(TimingSummary)); _changed(); } }
    public int ReactionMs { get => _reactionMs; set { if (SetProperty(ref _reactionMs, value < 0 ? -1 : Math.Clamp(value, 0, 5000))) OnPropertyChanged(nameof(TimingSummary)); _changed(); } }
    public int UseDelayMs { get => _useDelayMs; set { if (SetProperty(ref _useDelayMs, value < 0 ? -1 : Math.Clamp(value, 50, 60000))) OnPropertyChanged(nameof(TimingSummary)); _changed(); } }
    public int StopAtAmmo { get => _stopAtAmmo; set { if (SetProperty(ref _stopAtAmmo, Math.Max(0, value))) OnPropertyChanged(nameof(TimingSummary)); _changed(); } }
    public int AmmoCount { get => _ammoCount; set { SetProperty(ref _ammoCount, Math.Max(0, value)); _changed(); } }
    public int AmmoBags { get => _ammoBags; set { SetProperty(ref _ammoBags, Math.Max(0, value)); _changed(); } }
    public int AmmoPerBag { get => _ammoPerBag; set { if (SetProperty(ref _ammoPerBag, Math.Max(1, value))) OnPropertyChanged(nameof(TimingSummary)); _changed(); } }
    public int BuffIntervalSec { get => _buffIntervalSec; set { if (SetProperty(ref _buffIntervalSec, Math.Clamp(value, 5, 3600))) RefreshActionState(); _changed(); } }
    public int PotPercent { get => _potPercent; set { if (SetProperty(ref _potPercent, Math.Clamp(value, 1, 100))) RefreshActionState(); _changed(); } }
    public string ControllerButton { get => _controllerButton; set { if (SetProperty(ref _controllerButton, value ?? "")) OnPropertyChanged(nameof(ControllerSummary)); } }
    public bool HasSuggestedDelay => SuggestedDelayMs > 0;
    public bool ShowActionOptions => Enabled;
    public bool ShowSkillPicker => Enabled && IsSkill;
    public bool ShowBuffPicker => Enabled && IsBuff;
    public bool ShowBuffTimer => Enabled && IsBuff;
    public bool ShowSkillDelay => Enabled && IsSkill;
    public bool ShowPotSettings => Enabled && (IsHpPot || IsSpPot || IsYgg);
    public bool ShowTeleportSettings => Enabled && IsTeleport;
    public bool ShowAmmoSettings => Enabled && IsAmmo;
    public bool ShowAmmoBagSettings => Enabled && IsAmmoBag;
    public bool ShowSimpleDelay => Enabled && (IsLoot || IsReturn || IsWeapon || IsReconnect);
    public bool HasConfiguredAction => IsSkill || IsBuff || IsTeleport || IsYgg || IsHpPot || IsSpPot || IsAmmo || IsAmmoBag || IsLoot || IsReturn || IsWeapon || IsReconnect;
    public string SkillLevelSummary => $"Lv {SkillLevel}/{SkillMaxLevel}";
    public string ActionSummary
    {
        get
        {
            if (IsSkill) return string.IsNullOrWhiteSpace(SkillName) ? "Skill" : $"Skill: {SkillName}";
            if (IsBuff) return string.IsNullOrWhiteSpace(SkillName) ? "Buff" : $"Buff: {SkillName}";
            if (IsTeleport) return "Teleport / fly wing";
            if (IsYgg) return $"Ygg at {PotPercent}%";
            if (IsHpPot) return $"HP pot at {PotPercent}%";
            if (IsSpPot) return $"SP pot at {PotPercent}%";
            if (IsAmmo) return string.IsNullOrWhiteSpace(ItemName) ? "Ammo equip/refill key" : $"Ammo: {ItemName}";
            if (IsAmmoBag) return string.IsNullOrWhiteSpace(ItemName) ? "Ammo bag key" : $"Ammo bag: {ItemName}";
            if (IsLoot) return "Loot / pickup";
            if (IsReturn) return "Return to town";
            if (IsWeapon) return "Weapon swap";
            if (IsReconnect) return "Reconnect step";
            return "Choose an action";
        }
    }
    public string ControllerSummary => string.IsNullOrWhiteSpace(ControllerButton) ? "Controller: auto" : $"Controller: {ControllerButton}";
    public string TimingSummary
    {
        get
        {
            if (!Enabled) return "";
            if (IsSkill) return $"Order: press {Key} -> move cursor by distance -> click monster -> wait {DelayText(SkillDelayMs)}";
            if (IsBuff) return $"Order: press {Key} -> refresh every {BuffIntervalSec}s";
            if (IsHpPot || IsSpPot || IsYgg) return $"Order: when threshold is met -> wait {DelayText(ReactionMs)} -> press {Key} -> lockout {DelayText(UseDelayMs)}";
            if (IsTeleport) return $"Order: press {Key} -> wait {DelayText(UseDelayMs)} before next action";
            if (IsAmmo) return $"Order: equip ammo with {Key} -> stop attacking at <= {StopAtAmmo}";
            if (IsAmmoBag) return $"Order: press {Key} -> add {AmmoPerBag} ammo -> wait {DelayText(UseDelayMs)}";
            if (IsLoot || IsReturn || IsWeapon || IsReconnect) return $"Order: press {Key} -> wait {DelayText(UseDelayMs)}";
            return "";
        }
    }

    private static string DelayText(int value) => value < 0 ? "auto" : $"{value} ms";

    private void RefreshSkillInfo()
    {
        var (hint, delayMs, maxLevel) = _describe(SkillName, SkillLevel);
        SkillMaxLevel = maxLevel;
        if (SkillLevel > SkillMaxLevel) _skillLevel = SkillMaxLevel;
        SkillHint = hint;
        SuggestedDelayMs = delayMs;
        if (delayMs > 0 && SkillDelayMs == 0) SkillDelayMs = delayMs;
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(SkillLevelSummary));
    }

    private void RefreshItemInfo()
    {
        ItemHint = EstimateItemHint(ItemName, IsHpPot, IsSpPot, IsYgg, IsAmmo, IsAmmoBag);
        OnPropertyChanged(nameof(ActionSummary));
    }

    private void SetActionKind(string propertyName, bool value)
    {
        bool changed = propertyName switch
        {
            nameof(IsSkill) when _isSkill != value => SetAndClear(ref _isSkill, value, propertyName),
            nameof(IsBuff) when _isBuff != value => SetAndClear(ref _isBuff, value, propertyName),
            nameof(IsTeleport) when _isTeleport != value => SetAndClear(ref _isTeleport, value, propertyName),
            nameof(IsYgg) when _isYgg != value => SetAndClear(ref _isYgg, value, propertyName),
            nameof(IsHpPot) when _isHpPot != value => SetAndClear(ref _isHpPot, value, propertyName),
            nameof(IsSpPot) when _isSpPot != value => SetAndClear(ref _isSpPot, value, propertyName),
            nameof(IsAmmo) when _isAmmo != value => SetAndClear(ref _isAmmo, value, propertyName),
            nameof(IsAmmoBag) when _isAmmoBag != value => SetAndClear(ref _isAmmoBag, value, propertyName),
            nameof(IsLoot) when _isLoot != value => SetAndClear(ref _isLoot, value, propertyName),
            nameof(IsReturn) when _isReturn != value => SetAndClear(ref _isReturn, value, propertyName),
            nameof(IsWeapon) when _isWeapon != value => SetAndClear(ref _isWeapon, value, propertyName),
            nameof(IsReconnect) when _isReconnect != value => SetAndClear(ref _isReconnect, value, propertyName),
            _ => false
        };
        if (!changed) return;
        RefreshActionState();
        _changed();
    }

    private bool SetAndClear(ref bool field, bool value, string propertyName)
    {
        field = value;
        OnPropertyChanged(propertyName);
        if (value) ClearOtherActionKinds(propertyName);
        return true;
    }

    private void ClearOtherActionKinds(string keep)
    {
        ClearIfNeeded(ref _isSkill, nameof(IsSkill), keep);
        ClearIfNeeded(ref _isBuff, nameof(IsBuff), keep);
        ClearIfNeeded(ref _isTeleport, nameof(IsTeleport), keep);
        ClearIfNeeded(ref _isYgg, nameof(IsYgg), keep);
        ClearIfNeeded(ref _isHpPot, nameof(IsHpPot), keep);
        ClearIfNeeded(ref _isSpPot, nameof(IsSpPot), keep);
        ClearIfNeeded(ref _isAmmo, nameof(IsAmmo), keep);
        ClearIfNeeded(ref _isAmmoBag, nameof(IsAmmoBag), keep);
        ClearIfNeeded(ref _isLoot, nameof(IsLoot), keep);
        ClearIfNeeded(ref _isReturn, nameof(IsReturn), keep);
        ClearIfNeeded(ref _isWeapon, nameof(IsWeapon), keep);
        ClearIfNeeded(ref _isReconnect, nameof(IsReconnect), keep);
    }

    private void ClearIfNeeded(ref bool field, string propertyName, string keep)
    {
        if (propertyName == keep || !field) return;
        field = false;
        OnPropertyChanged(propertyName);
    }

    private void RefreshActionState()
    {
        OnPropertyChanged(nameof(ShowActionOptions));
        OnPropertyChanged(nameof(ShowSkillPicker));
        OnPropertyChanged(nameof(ShowBuffPicker));
        OnPropertyChanged(nameof(ShowBuffTimer));
        OnPropertyChanged(nameof(ShowSkillDelay));
        OnPropertyChanged(nameof(ShowPotSettings));
        OnPropertyChanged(nameof(ShowTeleportSettings));
        OnPropertyChanged(nameof(ShowAmmoSettings));
        OnPropertyChanged(nameof(ShowAmmoBagSettings));
        OnPropertyChanged(nameof(ShowSimpleDelay));
        OnPropertyChanged(nameof(HasConfiguredAction));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(TimingSummary));
        RefreshItemInfo();
    }

    private static string EstimateItemHint(string itemName, bool hp, bool sp, bool ygg, bool ammo, bool bag)
    {
        var n = (itemName ?? "").Trim().ToLowerInvariant();
        if (n.Length == 0)
        {
            if (hp) return "Pick the HP item used by this hotkey.";
            if (sp) return "Pick the SP item used by this hotkey.";
            if (ygg) return "Yggdrasil Seed is usually 50%; Berry is usually 100%.";
            if (ammo) return "Pick the arrow/bullet equipped by this key.";
            if (bag) return "Pick the quiver/ammo box opened by this key.";
            return "";
        }
        if (n.Contains("yggdrasil berry")) return "Expected effect: restores HP and SP to 100%.";
        if (n.Contains("yggdrasil seed")) return "Expected effect: restores about 50% HP and 50% SP.";
        if (n.Contains("white potion")) return "Expected HP recovery: medium-high; exact value depends on server/rates.";
        if (n.Contains("yellow potion")) return "Expected HP recovery: medium; exact value depends on server/rates.";
        if (n.Contains("orange potion")) return "Expected HP recovery: low-medium; exact value depends on server/rates.";
        if (n.Contains("red potion")) return "Expected HP recovery: low; exact value depends on server/rates.";
        if (n.Contains("blue potion")) return "Expected SP recovery: medium-high; exact value depends on server/rates.";
        if (n.Contains("green potion")) return "Status cure item.";
        if (bag || n.Contains("quiver") || n.Contains("box")) return "Ammo container; set how many ammo it adds below.";
        return "Item recognized from the database; exact recovery can vary by server.";
    }
}

public sealed class ControllerKeyMapRow : ObservableObject
{
    private readonly Action _changed;
    private string _button;

    public ControllerKeyMapRow(string key, string button, Action changed)
    {
        Key = key;
        _button = button;
        _changed = changed;
    }

    public string Key { get; }
    public string Button { get => _button; set { SetProperty(ref _button, value ?? ""); _changed(); } }
}

public sealed partial class SmartBotViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly GameSession _session;
    private readonly SettingsStore _settings;
    private readonly Lazy<GameDatabase> _db;
    private readonly CalculatorViewModel _calculator;
    private readonly SmartBotTrainingRecorder _training;
    private const string ViGEmInstallerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    private const string ReWasdDownloadUrl = "https://www.rewasd.com/download";
    private const string FakerInputInstallerUrl = "https://github.com/Ryochan7/FakerInput/releases/download/v0.1.1/FakerInput_Setup_0.1.1_x64.msi";
    private const string MouseDriverSourceZipUrl = "https://github.com/wadrych/vmouse/archive/refs/heads/main.zip";
    private static readonly string DriverInstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "4rVivi",
        "Drivers");
    private static readonly string ViGEmInstallerPath = Path.Combine(DriverInstallDir, "ViGEmBus_1.22.0_x64_x86_arm64.exe");
    private static readonly string FakerInputInstallerPath = Path.Combine(DriverInstallDir, "FakerInput_Setup_0.1.1_x64.msi");
    private static readonly string MouseDriverPackageDir = Path.Combine(DriverInstallDir, "vmouse");
    private static readonly string MouseDriverSourceZipPath = Path.Combine(DriverInstallDir, "vmouse-source.zip");
    private bool _hydrating;
    private bool _syncingControllerMap;
    private bool _syncingUnifiedActions;

    [ObservableProperty] private string _attackKey;
    [ObservableProperty] private string _lootKey;
    [ObservableProperty] private string _teleportKey;
    [ObservableProperty] private string _returnKey;
    [ObservableProperty] private string _rotation = "";
    [ObservableProperty] private string _buffKeys = "";
    [ObservableProperty] private string _reconnectKey1 = "";
    [ObservableProperty] private string _reconnectKey2 = "";
    [ObservableProperty] private string _reconnectKey3 = "";
    [ObservableProperty] private int _fleeAtHpPercent;
    [ObservableProperty] private int _stuckMs;
    [ObservableProperty] private int _focusKillMs = -1;
    [ObservableProperty] private int _nextMonsterDelayMs = -1;
    [ObservableProperty] private int _returnAtWeightPercent;
    [ObservableProperty] private int _rotationMs;
    [ObservableProperty] private bool _skillSpamEnabled;
    public string[] SkillClickModes { get; } = { "No mouse click", "With mouse click", "Deactivated" };
    [ObservableProperty] private string _skillClickMode = "No mouse click";
    public string[] AhkModes { get; } = { "Compatibility", "Speed boost" };
    [ObservableProperty] private string _ahkMode = "Compatibility";
    [ObservableProperty] private bool _mouseFlick;
    [ObservableProperty] private bool _noShift;
    [ObservableProperty] private int _buffIntervalSec;
    [ObservableProperty] private bool _clickToMove;
    [ObservableProperty] private bool _clickAttack;
    [ObservableProperty] private int _moveRadius;
    [ObservableProperty] private int _moveWaitMs;
    [ObservableProperty] private int _moveStableMs;
    [ObservableProperty] private bool _useVision;
    [ObservableProperty] private bool _hardwareClick;
    [ObservableProperty] private bool _useControllerButtons = true;
    [ObservableProperty] private bool _useControllerCombos;
    [ObservableProperty] private bool _showControllerAssignments;
    [ObservableProperty] private bool _showAdvancedTiming;
    [ObservableProperty] private bool _autopotEnabled;
    public string[] InputMethods { get; } =
    {
        "SendInput  (AHK Send/Click)",
        "mouse/keybd_event  (AHK SendEvent)",
        "PostMessage  (AHK ControlSend)",
        "ViGEm virtual click  (controller button)",
        "Virtual HID  (FakerInput/vmouse)",
        "VIIPER virtual USB  (keyboard + mouse)"
    };
    [ObservableProperty] private int _inputMethodIndex = (int)InputMethod.Viiper;
    partial void OnInputMethodIndexChanged(int value)
    {
        _hub.InputMethod = (InputMethod)value;
        RefreshVirtualDriverStatus();
        if (!_hydrating) SaveBotProfile();
    }
    public string[] VirtualClickButtons { get; } = ReWasdMouseMap.ButtonNames.ToArray();
    public string[] ControllerButtonChoices => ReWasdMouseMap.ButtonChordNames(UseControllerCombos).ToArray();
    [ObservableProperty] private string _virtualClickButton = "A";
    [ObservableProperty] private int _virtualClickHoldMs = 100;
    [ObservableProperty] private bool _virtualClickFallback;
    [ObservableProperty] private string _toggleHotkey = "";
    [ObservableProperty] private string _startHotkey = "";
    [ObservableProperty] private string _stopHotkey = "";
    [ObservableProperty] private string _addressStatus = "";
    [ObservableProperty] private string _virtualDriverStatus = "Virtual driver: checking...";
    [ObservableProperty] private IBrush _virtualDriverStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _virtualDriverInstallText = "Install ViGEm";
    [ObservableProperty] private string _reWasdStatus = "reWASD: checking...";
    [ObservableProperty] private IBrush _reWasdStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _reWasdActionText = "Get reWASD";
    [ObservableProperty] private string _mouseDriverStatus = "Virtual mouse driver: checking...";
    [ObservableProperty] private IBrush _mouseDriverStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _mouseDriverInstallText = "Install mouse driver";
    [ObservableProperty] private string _viiperStatus = "VIIPER: checking...";
    [ObservableProperty] private IBrush _viiperStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _viiperActionText = "Open VIIPER";
    [ObservableProperty] private string _inputStackStatus = "Input stack: checking...";
    [ObservableProperty] private bool _installingVirtualDriver;
    [ObservableProperty] private string _skillSuggestionStatus = "Skill metadata appears after you pick a skill.";
    [ObservableProperty] private bool _smartBotTrainingActive;
    [ObservableProperty] private string _smartBotTrainingStatus = "Smart Bot Training is idle.";

    // gear / ammo
    [ObservableProperty] private string _weaponKey = "";
    [ObservableProperty] private string _ammoKey = "";
    [ObservableProperty] private string _ammoBagKey = "";
    [ObservableProperty] private bool _equipOnStart;
    [ObservableProperty] private int _stopAtAmmo;
    [ObservableProperty] private int _ammoCount;
    [ObservableProperty] private int _ammoBags;
    [ObservableProperty] private int _ammoPerBag = 500;

    // roam box
    [ObservableProperty] private bool _useWalkBox;
    [ObservableProperty] private bool _showWalkBoxOverlay = true;
    [ObservableProperty] private int _boxX, _boxY, _boxW, _boxH;

    // auto-reconnect
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private string _reconnectKeys = "";
    [ObservableProperty] private string _targetMap = "";
    [ObservableProperty] private string _mapMonsterFocusText = "No farm map selected. Vision accepts every monster.";
    [ObservableProperty] private string _ammoName = "";
    [ObservableProperty] private string _attackSkill = "";
    [ObservableProperty] private string _attackSkillHint = "";
    [ObservableProperty] private int _attackSkillSuggestedDelayMs;
    public bool HasAttackSkillSuggestedDelay => AttackSkillSuggestedDelayMs > 0;

    public string[] Keys { get; } = KeyList.Common;
    public string[] ActionButtonChoices => Keys;
    public ObservableCollection<SmartSkillButton> SkillButtons { get; } = new();
    public ObservableCollection<ObservableCollection<SmartSkillButton>> SkillColumns { get; } = new();
    public ObservableCollection<SmartSkillButton> BuffButtons { get; } = new();
    public ObservableCollection<ControllerKeyMapRow> ControllerKeyMapRows { get; } = new();
    public ObservableCollection<PotRowViewModel> Pots { get; } = new();
    public ObservableCollection<MonsterRule> Monsters { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    public string DebugLogPath => DebugTrace.LogPath;
    /// <summary>Trained monster names (from the icon model labels) for the auto-kill picker's search.</summary>
    public System.Collections.Generic.IReadOnlyList<string> MonsterNames { get; private set; } = System.Array.Empty<string>();
    public System.Collections.Generic.IReadOnlyList<string> SkillNames { get; private set; } = System.Array.Empty<string>();
    public System.Collections.Generic.IReadOnlyList<string> MapNames { get; } = LoadMapNames();
    public System.Collections.Generic.IReadOnlyList<string> AmmoNames { get; private set; } = System.Array.Empty<string>();
    public System.Collections.Generic.IReadOnlyList<string> ConsumableNames { get; private set; } = System.Array.Empty<string>();

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

    private System.Collections.Generic.IReadOnlyList<string> LoadSkillDisplayNames()
    {
        try
        {
            var fromDb = _db.Value.SkillDisplayNames();
            if (fromDb.Count > 0) return fromDb;
        }
        catch { }

        return LoadLabelCategory("skills__")
            .Select(SkillDisplayName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
    }

    private System.Collections.Generic.IReadOnlyList<string> LoadMonsterDisplayNames()
    {
        var set = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            var fromDb = _db.Value.MobDisplayNames();
            foreach (var name in fromDb)
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
        }
        catch { }

        foreach (var label in LoadLabelCategory("mob__"))
        {
            var name = MonsterDisplayName(label);
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name);
        }

        return set.ToList();
    }

    private System.Collections.Generic.IReadOnlyList<string> LoadAmmoDisplayNames()
    {
        try
        {
            var fromDb = _db.Value.ItemNamesByType("Ammo");
            if (fromDb.Count > 0) return fromDb;
        }
        catch { }

        return LoadLabelCategory("itemsbyname__")
            .Select(ItemDisplayName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
    }

    private System.Collections.Generic.IReadOnlyList<string> LoadConsumableDisplayNames()
    {
        try
        {
            var fromDb = _db.Value.ItemNamesByType("Usable", "DelayConsume", "Healing");
            if (fromDb.Count > 0) return fromDb;
        }
        catch { }

        return LoadLabelCategory("itemsbyname__")
            .Select(ItemDisplayName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
    }

    private string SkillDisplayName(string? value)
    {
        try { return _db.Value.SkillDisplayName(value); }
        catch { return value ?? ""; }
    }

    private string ItemDisplayName(string? value)
    {
        try { return _db.Value.ItemDisplayName(value); }
        catch { return value ?? ""; }
    }

    private string MonsterDisplayName(string? value)
    {
        try { return _db.Value.MonsterDisplayNameFromTrainingLabel(value); }
        catch { return value ?? ""; }
    }

    private string EstimateMonsterKillText(string? monsterName)
    {
        try
        {
            var mob = _db.Value.MobByName(monsterName ?? "");
            if (mob == null) return "";

            var skillRow = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsSkill && !string.IsNullOrWhiteSpace(b.SkillName));
            var skillName = skillRow?.SkillName;
            var level = skillRow?.SkillLevel ?? 1;
            var delay = skillRow?.SkillDelayMs ?? RotationMs;
            if (string.IsNullOrWhiteSpace(skillName))
            {
                skillName = AttackSkill;
                delay = RotationMs;
            }
            var skill = string.IsNullOrWhiteSpace(skillName) ? null : _db.Value.SkillByName(skillName);
            return _calculator.EstimateKillTime(mob, skill, level, Math.Max(80, delay));
        }
        catch
        {
            return "";
        }
    }

    private void RefreshMonsterEstimates()
    {
        foreach (var m in Monsters)
            m.Estimate = EstimateMonsterKillText(m.Name);
    }

    private void RefreshMapMonsterFocus(bool save = true)
    {
        var map = (TargetMap ?? "").Trim();
        var names = new List<string>();
        try
        {
            if (map.Length > 0)
            {
                names = _db.Value.MapMonsterSpawns(map)
                    .Select(s => MonsterDisplayName(s.Name))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s)
                    .ToList();
            }
        }
        catch { }

        _hub.SmartBot.FocusedMonsterNames.Clear();
        foreach (var n in names) _hub.SmartBot.FocusedMonsterNames.Add(n);
        LiveScene.Instance.SetMonsterFocus(names);

        MapMonsterFocusText = names.Count == 0
            ? (map.Length == 0
                ? "No farm map selected. Vision accepts every monster."
                : $"No spawn list found for {map}. Vision accepts every monster.")
            : $"Map focus: {map} -> {string.Join(", ", names.Take(12))}{(names.Count > 12 ? $" +{names.Count - 12} more" : "")}. Out-of-map lookalike labels are ignored.";

        if (!_hydrating && names.Count > 0)
            SkillSuggestionStatus = $"Loaded {names.Count} map monster(s) for {map}. YOLO/track labels now prefer this map.";
        if (save && !_hydrating) SaveBotProfile();
    }

    private static string RuleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _botStateText = "Stopped";
    partial void OnEnabledChanged(bool value)
    {
        _hub.SmartBot.Enabled = value;
        BotStateText = value ? "Running" : "Stopped";
        SaveBotProfile();
    }

    public SmartBotViewModel(EngineHub hub, GameSession session, SettingsStore settings, Lazy<GameDatabase> db, CalculatorViewModel calculator, SmartBotTrainingRecorder training)
    {
        _hub = hub; _session = session; _settings = settings; _db = db; _calculator = calculator; _training = training;
        _training.StatusChanged += s => Dispatcher.UIThread.Post(() =>
        {
            SmartBotTrainingStatus = s;
            SmartBotTrainingActive = _training.Running;
        });
        MonsterNames = LoadMonsterDisplayNames();
        SkillNames = LoadSkillDisplayNames();
        AmmoNames = LoadAmmoDisplayNames();
        ConsumableNames = LoadConsumableDisplayNames();
        _hydrating = true;
        var profile = settings.Current.GetActiveProfile();
        profile.SmartBot ??= new SmartBotConfig();
        ApplyPersistedConfig(profile.SmartBot);
        var b = hub.SmartBot;
        _attackKey = b.AttackKey; _lootKey = b.LootKey; _teleportKey = b.TeleportKey; _returnKey = b.ReturnKey;
        _fleeAtHpPercent = b.FleeAtHpPercent; _stuckMs = b.StuckMs; _focusKillMs = b.FocusKillMs; _nextMonsterDelayMs = b.NextMonsterDelayMs;
        _returnAtWeightPercent = b.ReturnAtWeightPercent; _rotationMs = b.RotationMs;
        _enabled = b.Enabled;
        _botStateText = b.Enabled ? "Running" : "Stopped";
        _skillSpamEnabled = profile.SmartBot.SkillSpamEnabled || b.SkillRotation.Count > 0;
        _skillClickMode = string.IsNullOrWhiteSpace(profile.SmartBot.SkillClickMode) ? "No mouse click" : profile.SmartBot.SkillClickMode;
        _ahkMode = string.IsNullOrWhiteSpace(profile.SmartBot.AhkMode) ? "Compatibility" : profile.SmartBot.AhkMode;
        _mouseFlick = profile.SmartBot.MouseFlick;
        _noShift = profile.SmartBot.NoShift;
        _buffIntervalSec = Math.Max(5, b.BuffIntervalMs / 1000);
        _buffKeys = string.Join(", ", b.BuffKeys);
        _clickToMove = b.ClickToMove; _clickAttack = b.ClickAttack; _moveRadius = b.MoveRadius;
        _moveWaitMs = b.MoveWaitMs; _moveStableMs = b.MoveStableMs;
        _useVision = b.UseVision; _hardwareClick = b.HardwareClick; _useControllerButtons = b.UseControllerButtons;
        _useControllerCombos = profile.SmartBot.ControllerKeyMap.Count == 0 || profile.SmartBot.UseControllerCombos;
        _showControllerAssignments = profile.SmartBot.ShowControllerAssignments;
        _showAdvancedTiming = profile.SmartBot.ShowAdvancedTiming;
        _autopotEnabled = hub.Autopot.Enabled;
        _weaponKey = b.WeaponKey; _ammoKey = b.AmmoKey; _ammoBagKey = b.AmmoBagKey; _equipOnStart = b.EquipOnStart; _stopAtAmmo = b.StopAtAmmo;
        _ammoCount = b.ManualAmmoCount; _ammoBags = b.AmmoBags; _ammoPerBag = b.AmmoPerBag;
        _useWalkBox = b.UseWalkBox; _showWalkBoxOverlay = profile.SmartBot.ShowWalkBoxOverlay; _boxX = b.BoxX; _boxY = b.BoxY; _boxW = b.BoxW; _boxH = b.BoxH;
        _autoReconnect = b.AutoReconnect; _reconnectKeys = string.Join(", ", b.ReconnectKeys);
        _reconnectKey1 = b.ReconnectKeys.Count > 0 ? b.ReconnectKeys[0] : "";
        _reconnectKey2 = b.ReconnectKeys.Count > 1 ? b.ReconnectKeys[1] : "";
        _reconnectKey3 = b.ReconnectKeys.Count > 2 ? b.ReconnectKeys[2] : "";
        _targetMap = b.TargetMap; _ammoName = b.AmmoName; _attackSkill = b.AttackSkill;
        RefreshMapMonsterFocus(save: false);
        RefreshAttackSkillInfo();
        _inputMethodIndex = (int)hub.InputMethod;
        _virtualClickButton = _hub.VirtualClickButton;
        _virtualClickHoldMs = _hub.VirtualClickHoldMs;
        _virtualClickFallback = _hub.VirtualClickFallback;
        _toggleHotkey = profile.SmartBot.ToggleHotkey ?? "";
        _startHotkey = profile.SmartBot.StartHotkey ?? "";
        _stopHotkey = profile.SmartBot.StopHotkey ?? "";
        BuildSkillColumns();
        foreach (var key in new[] { "F5", "F6", "F7" })
            BuffButtons.Add(new SmartSkillButton(key, SyncBuffButtons, DescribeSkill) { IsBuff = true, BuffIntervalSec = _buffIntervalSec });
        LoadSmartSkillButtonsFromConfig(profile.SmartBot);
        LoadBuffButtonsFromConfig(profile.SmartBot);
        LoadPotRules();
        LoadUnifiedActionsFromLegacy(profile.SmartBot);
        LoadControllerKeyMap(profile.SmartBot);
        EnsureControllerMappingsForActiveActions();
        BotLog.Instance.Added += OnBotLog;
        _hydrating = false;
        DebugTrace.Write("SmartBotVM", $"Loaded profile controllerMode={UseControllerButtons} mapRows={ControllerKeyMapRows.Count} validMap={_hub.SmartBot.ControllerKeyMap.Count} inputMethod={_hub.InputMethod} virtualButton={_hub.VirtualClickButton} fallback={_hub.VirtualClickFallback}.");
        RefreshVirtualDriverStatus();
        RefreshAddresses();
    }

    [RelayCommand] private void StartBot() => StartBotFromHotkey("button");

    [RelayCommand] private void StopBot() => StopBotFromHotkey("button");

    [RelayCommand]
    private void StartSmartBotTraining()
    {
        SyncSkillButtons();
        _training.Start();
        SmartBotTrainingActive = true;
        SmartBotTrainingStatus = _training.Summary;
        SkillSuggestionStatus = "Smart Bot Training is recording Ragnarok hotbar keys, clicks, OCR scene, HP/SP, map, and kill timing.";
    }

    [RelayCommand]
    private void StopSmartBotTraining()
    {
        _training.Stop();
        SmartBotTrainingActive = false;
        SmartBotTrainingStatus = $"{_training.Summary} Log: {_training.LogPath}";
        SkillSuggestionStatus = "Smart Bot Training stopped. Learned timings are now feeding automatic timing.";
    }

    [RelayCommand]
    private void OpenSmartBotTrainingLog()
    {
        var path = _training.LogPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SkillSuggestionStatus = "No Smart Bot Training log has been created yet.";
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public void StartBotFromHotkey(string source)
    {
        Enabled = true;
        SkillSuggestionStatus = string.Equals(source, "button", StringComparison.OrdinalIgnoreCase)
            ? "Smart Bot started. OCR detections now drive monster clicks, skills, buffs, potions, and walking."
            : $"Smart Bot started by {source}.";
    }

    public void StopBotFromHotkey(string source)
    {
        Enabled = false;
        SkillSuggestionStatus = string.Equals(source, "button", StringComparison.OrdinalIgnoreCase)
            ? "Smart Bot stopped. OCR and other tools can stay running."
            : $"Smart Bot stopped by {source}.";
    }

    public void ToggleBotFromHotkey()
    {
        Enabled = !Enabled;
        SkillSuggestionStatus = Enabled
            ? $"Smart Bot started by {ToggleHotkey}."
            : $"Smart Bot stopped by {ToggleHotkey}.";
    }

    private void OnBotLog(BotLogEntry e) => Dispatcher.UIThread.Post(() =>
    {
        Logs.Insert(0, $"{e.Stamp}  [{e.Kind}]  {e.Text}");
        while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    });

    partial void OnAttackKeyChanged(string value) { _hub.SmartBot.AttackKey = value; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnLootKeyChanged(string value) { _hub.SmartBot.LootKey = value; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnTeleportKeyChanged(string value) { _hub.SmartBot.TeleportKey = value; if (_syncingUnifiedActions) return; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnReturnKeyChanged(string value) { _hub.SmartBot.ReturnKey = value; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnFleeAtHpPercentChanged(int value) { _hub.SmartBot.FleeAtHpPercent = value; SaveBotProfile(); }
    partial void OnStuckMsChanged(int value) { _hub.SmartBot.StuckMs = Math.Clamp(value, 2000, 120000); SaveBotProfile(); }
    partial void OnFocusKillMsChanged(int value) { _hub.SmartBot.FocusKillMs = value < 0 ? -1 : Math.Clamp(value, 1000, 600000); SaveBotProfile(); }
    partial void OnNextMonsterDelayMsChanged(int value) { _hub.SmartBot.NextMonsterDelayMs = value < 0 ? -1 : Math.Clamp(value, 0, 5000); SaveBotProfile(); }
    partial void OnReturnAtWeightPercentChanged(int value) { _hub.SmartBot.ReturnAtWeightPercent = value; SaveBotProfile(); }
    partial void OnRotationMsChanged(int value) { _hub.SmartBot.RotationMs = value < 0 ? -1 : Math.Max(10, value); RefreshMonsterEstimates(); SaveBotProfile(); }
    partial void OnSkillSpamEnabledChanged(bool value) { if (!_syncingUnifiedActions) SyncSkillButtons(); }
    partial void OnSkillClickModeChanged(string value)
    {
        if (string.Equals(value, "With mouse click", StringComparison.OrdinalIgnoreCase))
            ClickAttack = true;
        else if (string.Equals(value, "No mouse click", StringComparison.OrdinalIgnoreCase))
            ClickAttack = false;
        SyncSkillButtons();
    }
    partial void OnAhkModeChanged(string value) => SaveBotProfile();
    partial void OnMouseFlickChanged(bool value) => SaveBotProfile();
    partial void OnNoShiftChanged(bool value) => SaveBotProfile();
    partial void OnBuffIntervalSecChanged(int value) { _hub.SmartBot.BuffIntervalMs = Math.Max(5, value) * 1000; SaveBotProfile(); }
    partial void OnClickToMoveChanged(bool value) { _hub.SmartBot.ClickToMove = value; SaveBotProfile(); }
    partial void OnClickAttackChanged(bool value) { _hub.SmartBot.ClickAttack = value; SaveBotProfile(); }
    partial void OnMoveRadiusChanged(int value) { _hub.SmartBot.MoveRadius = Math.Max(20, value); SaveBotProfile(); }
    partial void OnMoveWaitMsChanged(int value) { _hub.SmartBot.MoveWaitMs = value < 0 ? -1 : Math.Clamp(value, 400, 5000); SaveBotProfile(); }
    partial void OnMoveStableMsChanged(int value) { _hub.SmartBot.MoveStableMs = value < 0 ? -1 : Math.Clamp(value, 150, 3000); SaveBotProfile(); }
    partial void OnUseVisionChanged(bool value) { _hub.SmartBot.UseVision = value; SaveBotProfile(); }
    partial void OnHardwareClickChanged(bool value) { _hub.SmartBot.HardwareClick = value; SaveBotProfile(); }
    partial void OnUseControllerButtonsChanged(bool value)
    {
        _hub.SmartBot.UseControllerButtons = value;
        _hub.Autopot.UseControllerButtons = value;
        DebugTrace.Write("SmartBotVM", $"UseControllerButtons changed -> {value}.");
        BuildSkillColumns();
        LoadSmartSkillButtonsFromRotation();
        OnPropertyChanged(nameof(ActionButtonChoices));
        EnsureControllerMappingsForActiveActions();
        SaveBotProfile();
    }
    partial void OnUseControllerCombosChanged(bool value)
    {
        OnPropertyChanged(nameof(ControllerButtonChoices));
        EnsureControllerMappingsForActiveActions(forceUnique: true);
        SaveBotProfile();
    }
    partial void OnShowControllerAssignmentsChanged(bool value) => SaveBotProfile();
    partial void OnShowAdvancedTimingChanged(bool value) => SaveBotProfile();
    partial void OnAutopotEnabledChanged(bool value) { _hub.Autopot.Enabled = value; if (!_syncingUnifiedActions) SaveBotProfile(); }
    partial void OnVirtualClickButtonChanged(string value)
    {
        _hub.VirtualClickButton = value;
        SaveBotProfile();
    }
    partial void OnVirtualClickHoldMsChanged(int value)
    {
        _hub.VirtualClickHoldMs = Math.Clamp(value, 30, 500);
        SaveBotProfile();
    }
    partial void OnVirtualClickFallbackChanged(bool value)
    {
        _hub.VirtualClickFallback = value;
        RefreshVirtualDriverStatus();
        SaveBotProfile();
    }
    partial void OnToggleHotkeyChanged(string value) => SaveBotProfile();
    partial void OnStartHotkeyChanged(string value) => SaveBotProfile();
    partial void OnStopHotkeyChanged(string value) => SaveBotProfile();
    partial void OnWeaponKeyChanged(string value) { _hub.SmartBot.WeaponKey = value; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnAmmoKeyChanged(string value) { _hub.SmartBot.AmmoKey = value; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnAmmoBagKeyChanged(string value) { _hub.SmartBot.AmmoBagKey = value; EnsureControllerMappingsForActiveActions(); SaveBotProfile(); }
    partial void OnEquipOnStartChanged(bool value) { _hub.SmartBot.EquipOnStart = value; SaveBotProfile(); }
    partial void OnStopAtAmmoChanged(int value) { _hub.SmartBot.StopAtAmmo = Math.Max(0, value); SaveBotProfile(); }
    partial void OnAmmoCountChanged(int value) { _hub.SmartBot.ManualAmmoCount = Math.Max(0, value); SaveBotProfile(); }
    partial void OnAmmoBagsChanged(int value) { _hub.SmartBot.AmmoBags = Math.Max(0, value); SaveBotProfile(); }
    partial void OnAmmoPerBagChanged(int value) { _hub.SmartBot.AmmoPerBag = Math.Max(1, value); SaveBotProfile(); }
    partial void OnUseWalkBoxChanged(bool value) { _hub.SmartBot.UseWalkBox = value; SaveBotProfile(); }
    partial void OnShowWalkBoxOverlayChanged(bool value) => SaveBotProfile();
    partial void OnBoxXChanged(int value) { _hub.SmartBot.BoxX = value; SaveBotProfile(); }
    partial void OnBoxYChanged(int value) { _hub.SmartBot.BoxY = value; SaveBotProfile(); }
    partial void OnBoxWChanged(int value) { _hub.SmartBot.BoxW = value; SaveBotProfile(); }
    partial void OnBoxHChanged(int value) { _hub.SmartBot.BoxH = value; SaveBotProfile(); }
    partial void OnAutoReconnectChanged(bool value) { _hub.SmartBot.AutoReconnect = value; SaveBotProfile(); }
    partial void OnTargetMapChanged(string value)
    {
        _hub.SmartBot.TargetMap = value ?? "";
        RefreshMapMonsterFocus();
        SaveBotProfile();
    }
    partial void OnAmmoNameChanged(string value) { _hub.SmartBot.AmmoName = value ?? ""; SaveBotProfile(); }
    partial void OnAttackSkillChanged(string value) { _hub.SmartBot.AttackSkill = value ?? ""; RefreshAttackSkillInfo(); RefreshMonsterEstimates(); SaveBotProfile(); }
    partial void OnAttackSkillSuggestedDelayMsChanged(int value) => OnPropertyChanged(nameof(HasAttackSkillSuggestedDelay));
    partial void OnReconnectKey1Changed(string value) => SyncReconnectKeys();
    partial void OnReconnectKey2Changed(string value) => SyncReconnectKeys();
    partial void OnReconnectKey3Changed(string value) => SyncReconnectKeys();

    partial void OnReconnectKeysChanged(string value)
    {
        _hub.SmartBot.ReconnectKeys.Clear();
        foreach (var k in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.SmartBot.ReconnectKeys.Add(k);
        SaveBotProfile();
    }

    partial void OnBuffKeysChanged(string value)
    {
        _hub.SmartBot.BuffKeys.Clear();
        foreach (var k in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.SmartBot.BuffKeys.Add(k);
        LoadBuffButtonsFromKeys();
        SaveBotProfile();
    }

    [RelayCommand] private void ClearAllKeys()
    {
        _hub.ClearAllKeys();                                   // every feature: bot, pot, spammer, buffs, macros...
        AttackKey = ""; LootKey = ""; TeleportKey = ""; ReturnKey = "";
        WeaponKey = ""; AmmoKey = ""; ReconnectKeys = ""; ReconnectKey1 = ""; ReconnectKey2 = ""; ReconnectKey3 = "";
        Rotation = ""; BuffKeys = "";
        foreach (var b in SkillButtons)
        {
            b.Enabled = false; b.SkillName = ""; b.SpRequired = 0;
            b.IsSkill = false; b.IsBuff = false; b.IsTeleport = false; b.IsYgg = false; b.IsHpPot = false; b.IsSpPot = false;
            b.IsAmmo = false; b.IsAmmoBag = false; b.IsLoot = false; b.IsReturn = false; b.IsWeapon = false; b.IsReconnect = false;
            b.ItemName = ""; b.SkillDelayMs = -1; b.ReactionMs = -1; b.UseDelayMs = -1;
            b.StopAtAmmo = 0; b.AmmoCount = 0; b.AmmoBags = 0; b.AmmoPerBag = 500;
        }
        foreach (var b in BuffButtons) { b.Enabled = false; b.SkillName = ""; }
        foreach (var m in Monsters) m.SkillKey = "";
        AddressStatus = "Cleared all hotkeys across every feature.";
        SaveBotProfile();
    }

    [RelayCommand] private void ClearToggleHotkey()
    {
        ToggleHotkey = "";
        SkillSuggestionStatus = "Smart Bot start/stop key cleared.";
    }

    [RelayCommand] private void ClearStartHotkey()
    {
        StartHotkey = "";
        SkillSuggestionStatus = "Smart Bot start key cleared.";
    }

    [RelayCommand] private void ClearStopHotkey()
    {
        StopHotkey = "";
        SkillSuggestionStatus = "Smart Bot stop key cleared.";
    }

    [RelayCommand] private void ApplyRotation()
    {
        _hub.SmartBot.SkillRotation.Clear();
        foreach (var k in Rotation.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _hub.SmartBot.SkillRotation.Add(k);
        LoadSmartSkillButtonsFromRotation();
        SaveBotProfile();
    }

    [RelayCommand] private void AddPot()
    {
        var c = new PotConfig { Enabled = true, Key = "F2", Percent = 40, ReactionMs = -1, UseDelayMs = -1 };
        _settings.Current.GetActiveProfile().Pots.Add(c);
        _hub.Autopot.Rules.Add(c);
        Pots.Add(new PotRowViewModel(c, OnPotChanged));
        SaveSettings();
        SaveBotProfile();
    }

    [RelayCommand] private void AddBuffButton()
    {
        BuffButtons.Add(new SmartSkillButton("", SyncBuffButtons, DescribeSkill) { IsBuff = true, BuffIntervalSec = BuffIntervalSec });
        SyncBuffButtons();
    }

    [RelayCommand]
    private void ApplySuggestedSkillDelay(SmartSkillButton? row)
    {
        if (row == null || row.SuggestedDelayMs <= 0)
        {
            SkillSuggestionStatus = "No database delay is available for that skill.";
            return;
        }
        row.SkillDelayMs = Math.Clamp(row.SuggestedDelayMs, 10, 5000);
        SkillSuggestionStatus = $"Manual delay set to {row.SkillDelayMs} ms for {row.SkillName}. Set the delay back to -1 to return to automatic timing.";
    }

    [RelayCommand]
    private void ResetSkillDelayAuto(SmartSkillButton? row)
    {
        if (row == null) return;
        row.SkillDelayMs = -1;
        SkillSuggestionStatus = string.IsNullOrWhiteSpace(row.SkillName)
            ? $"{row.Key} is back on automatic timing (-1)."
            : $"{row.Key} / {row.SkillName} is back on automatic timing (-1).";
    }

    [RelayCommand]
    private void ApplyAttackSkillDelay()
    {
        if (AttackSkillSuggestedDelayMs <= 0)
        {
            SkillSuggestionStatus = "No database delay is available for the attack skill.";
            return;
        }
        RotationMs = Math.Clamp(AttackSkillSuggestedDelayMs, 10, 5000);
        SkillSuggestionStatus = $"Manual main attack delay set to {RotationMs} ms for {AttackSkill}. Set it back to -1 to return to automatic timing.";
    }

    [RelayCommand] private void RemoveBuffButton(SmartSkillButton? row)
    {
        if (row == null) return;
        BuffButtons.Remove(row);
        SyncBuffButtons();
    }

    [RelayCommand] private void RemovePot(PotRowViewModel? row)
    {
        if (row == null) return;
        _settings.Current.GetActiveProfile().Pots.Remove(row.Model);
        _hub.Autopot.Rules.Remove(row.Model);
        Pots.Remove(row);
        SaveSettings();
        SaveBotProfile();
    }

    [RelayCommand] private void ClearLog() { Logs.Clear(); BotLog.Instance.Clear(); }

    [RelayCommand]
    private void ClearDebugLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebugTrace.LogPath)!);
            File.WriteAllText(DebugTrace.LogPath, "");
            SkillSuggestionStatus = "Debug log cleared.";
        }
        catch (Exception ex)
        {
            SkillSuggestionStatus = "Could not clear debug log: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenDebugLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebugTrace.LogPath)!);
            if (!File.Exists(DebugTrace.LogPath)) File.WriteAllText(DebugTrace.LogPath, "");
            Process.Start(new ProcessStartInfo(DebugTrace.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SkillSuggestionStatus = "Could not open debug log: " + ex.Message;
        }
    }

    [RelayCommand]
    private void RefreshVirtualDriverStatus()
    {
        bool driverInstalled = false;
        bool reWasdRunning = false;
        bool reWasdInstalled = false;
        bool vmouseDriverInstalled = IsVmouseDriverInstalled();
        bool fakerInputInstalled = IsFakerInputInstalled();
        bool mouseDriverInstalled = vmouseDriverInstalled || fakerInputInstalled;
        bool viiperInstalled = false;
        bool viiperReady = false;
        bool mouseDriverReady = false;
        bool mouseDriverPackaged = TryFindPackagedMouseDriverInf() != null;

        try { driverInstalled = _hub.IsVirtualDriverInstalled(); } catch { }
        try { mouseDriverReady = _hub.IsVirtualHidReady(); } catch { }
        try { viiperInstalled = _hub.IsViiperInstalled(); } catch { viiperInstalled = FindViiperExe() != null; }
        try { viiperReady = _hub.IsViiperReady(); } catch { }
        try { reWasdRunning = _hub.IsReWasdRunning(); } catch { }
        try { reWasdInstalled = reWasdRunning || FindReWasdExe() != null; } catch { }

        if (driverInstalled && reWasdRunning)
        {
            VirtualDriverStatus = "ViGEm installed - virtual Xbox available";
            VirtualDriverStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            VirtualDriverInstallText = "Repair ViGEm";
        }
        else if (driverInstalled)
        {
            VirtualDriverStatus = "ViGEm installed - ready when Smart Bot needs it";
            VirtualDriverStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            VirtualDriverInstallText = "Repair ViGEm";
        }
        else
        {
            VirtualDriverStatus = "ViGEm driver missing - press Install ViGEm";
            VirtualDriverStatusBrush = new SolidColorBrush(Color.Parse("#FF7A7A"));
            VirtualDriverInstallText = "Install ViGEm";
        }

        if (reWasdRunning)
        {
            ReWasdStatus = "reWASD running - profile bridge ready";
            ReWasdStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            ReWasdActionText = "Open reWASD";
        }
        else if (reWasdInstalled)
        {
            ReWasdStatus = "reWASD installed - open it to use profiles";
            ReWasdStatusBrush = new SolidColorBrush(Color.Parse("#FFC857"));
            ReWasdActionText = "Open reWASD";
        }
        else
        {
            ReWasdStatus = "reWASD not installed - optional profile bridge";
            ReWasdStatusBrush = new SolidColorBrush(Color.Parse("#8A94A6"));
            ReWasdActionText = "Get reWASD";
        }

        if (mouseDriverReady && fakerInputInstalled)
        {
            MouseDriverStatus = "FakerInput virtual HID connected";
            MouseDriverStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            MouseDriverInstallText = "Repair FakerInput";
        }
        else if (mouseDriverReady && vmouseDriverInstalled)
        {
            MouseDriverStatus = "vmouse virtual mouse connected";
            MouseDriverStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            MouseDriverInstallText = "Repair mouse driver";
        }
        else if (fakerInputInstalled)
        {
            MouseDriverStatus = "FakerInput installed - press Test virtual HID";
            MouseDriverStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            MouseDriverInstallText = "Repair FakerInput";
        }
        else if (vmouseDriverInstalled)
        {
            MouseDriverStatus = "vmouse virtual mouse driver installed";
            MouseDriverStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            MouseDriverInstallText = "Repair mouse driver";
        }
        else if (mouseDriverPackaged)
        {
            MouseDriverStatus = "Virtual mouse driver packaged - ready to install";
            MouseDriverStatusBrush = new SolidColorBrush(Color.Parse("#FFC857"));
            MouseDriverInstallText = "Install mouse driver";
        }
        else
        {
            MouseDriverStatus = "FakerInput virtual mouse not installed";
            MouseDriverStatusBrush = new SolidColorBrush(Color.Parse("#FFC857"));
            MouseDriverInstallText = "Install FakerInput";
        }

        if (viiperReady)
        {
            ViiperStatus = "VIIPER connected - virtual USB keyboard/mouse ready";
            ViiperStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            ViiperActionText = "Open VIIPER";
        }
        else if (viiperInstalled)
        {
            ViiperStatus = "VIIPER installed - press Enable VIIPER";
            ViiperStatusBrush = new SolidColorBrush(Color.Parse("#5EC26A"));
            ViiperActionText = "Open VIIPER";
        }
        else
        {
            ViiperStatus = "VIIPER not installed - optional virtual USB backend";
            ViiperStatusBrush = new SolidColorBrush(Color.Parse("#FFC857"));
            ViiperActionText = "Get VIIPER";
        }

        var method = Enum.IsDefined(typeof(InputMethod), InputMethodIndex) ? (InputMethod)InputMethodIndex : InputMethod.VirtualHid;
        var fallbackText = VirtualClickFallback ? "normal fallback enabled" : "normal fallback off";
        InputStackStatus = method switch
        {
            InputMethod.Viiper when viiperInstalled => $"Input stack: VIIPER virtual USB -> FakerInput/vmouse -> ViGEm virtual Xbox -> {fallbackText}.",
            InputMethod.Viiper => $"Input stack: install VIIPER for first path; FakerInput/vmouse and ViGEm are fallbacks; {fallbackText}.",
            InputMethod.VirtualHid when mouseDriverInstalled => $"Input stack: FakerInput/vmouse -> ViGEm virtual Xbox -> {fallbackText}. reWASD is optional.",
            InputMethod.VirtualHid => $"Input stack: install FakerInput/vmouse for first path; ViGEm is second path; {fallbackText}. reWASD is optional.",
            InputMethod.ReWasdClick when driverInstalled => $"Input stack: ViGEm virtual Xbox -> {fallbackText}. reWASD profiles are optional.",
            InputMethod.ReWasdClick => "Input stack: install ViGEm for virtual-controller clicks. reWASD is optional.",
            _ => "Input stack: standard Windows input backend selected."
        };
    }

    [RelayCommand]
    private async Task InstallVirtualDriver()
    {
        if (InstallingVirtualDriver)
            return;

        RefreshVirtualDriverStatus();
        bool alreadyReady = _hub.IsVirtualDriverInstalled();

        InstallingVirtualDriver = true;
        VirtualDriverInstallText = "Downloading...";
        try
        {
            Directory.CreateDirectory(DriverInstallDir);
            if (!File.Exists(ViGEmInstallerPath) || new FileInfo(ViGEmInstallerPath).Length < 1024 * 1024)
            {
                SkillSuggestionStatus = "Downloading the official ViGEm installer...";
                DebugTrace.Write("DriverInstall", $"Downloading ViGEm installer from {ViGEmInstallerUrl} to {ViGEmInstallerPath}.");
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("4ViviTools/1.0");
                await using var webStream = await http.GetStreamAsync(ViGEmInstallerUrl);
                await using var fileStream = File.Create(ViGEmInstallerPath);
                await webStream.CopyToAsync(fileStream);
            }

            SkillSuggestionStatus = alreadyReady
                ? "Opening the ViGEm setup for repair. Accept the Windows prompt, finish setup, then press Check driver."
                : "Opening the ViGEm installer. Accept the Windows prompt, finish setup, then press Check driver.";
            DebugTrace.Write("DriverInstall", $"Launching ViGEm installer: {ViGEmInstallerPath}");
            Process.Start(new ProcessStartInfo(ViGEmInstallerPath)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            DebugTrace.Write("DriverInstall", "ViGEm install/download failed.", ex);
            SkillSuggestionStatus = "Could not open the ViGEm installer: " + ex.Message;
        }
        finally
        {
            InstallingVirtualDriver = false;
            RefreshVirtualDriverStatus();
        }
    }

    [RelayCommand]
    private void OpenVirtualDriverFolder()
    {
        try
        {
            Directory.CreateDirectory(DriverInstallDir);
            Process.Start(new ProcessStartInfo(DriverInstallDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SkillSuggestionStatus = "Could not open driver folder: " + ex.Message;
        }
    }

    [RelayCommand]
    private void InstallOrOpenReWasd()
    {
        try
        {
            var exe = FindReWasdExe();
            if (exe != null)
            {
                DebugTrace.Write("DriverInstall", $"Opening reWASD: {exe}");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                SkillSuggestionStatus = "Opening reWASD. Apply the profile that maps your selected Xbox button to left mouse.";
            }
            else
            {
                DebugTrace.Write("DriverInstall", $"Opening reWASD official download page: {ReWasdDownloadUrl}");
                Process.Start(new ProcessStartInfo(ReWasdDownloadUrl) { UseShellExecute = true });
                SkillSuggestionStatus = "Opening the official reWASD download page. Install it only if you want profile import/mapping.";
            }
        }
        catch (Exception ex)
        {
            DebugTrace.Write("DriverInstall", "Could not open reWASD or its download page.", ex);
            SkillSuggestionStatus = "Could not open reWASD: " + ex.Message;
        }
        finally
        {
            RefreshVirtualDriverStatus();
        }
    }

    [RelayCommand]
    private void InstallOrOpenViiper()
    {
        try
        {
            var exe = FindViiperExe();
            if (exe != null)
            {
                DebugTrace.Write("DriverInstall", $"Opening VIIPER: {exe}");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                SkillSuggestionStatus = "Opening VIIPER. For bot input, press Enable VIIPER so 4ViviTools creates the virtual USB keyboard and mouse.";
            }
            else
            {
                DebugTrace.Write("DriverInstall", "Opening VIIPER release page.");
                Process.Start(new ProcessStartInfo("https://github.com/Alia5/VIIPER/releases") { UseShellExecute = true });
                SkillSuggestionStatus = "Opening the official VIIPER releases page. Install VIIPER, restart 4ViviTools, then press Check driver.";
            }
        }
        catch (Exception ex)
        {
            DebugTrace.Write("DriverInstall", "Could not open VIIPER or its release page.", ex);
            SkillSuggestionStatus = "Could not open VIIPER: " + ex.Message;
        }
        finally
        {
            RefreshVirtualDriverStatus();
        }
    }

    [RelayCommand]
    private void EnableViiper()
    {
        if (_hub.EnableViiper())
        {
            SkillSuggestionStatus = "VIIPER enabled for this 4ViviTools session. It created a virtual USB keyboard and mouse and will release them when the tool closes.";
            RefreshVirtualDriverStatus();
            return;
        }

        SkillSuggestionStatus = "VIIPER is not ready. Start or reinstall VIIPER, then press Enable VIIPER again.";
        RefreshVirtualDriverStatus();
    }

    [RelayCommand]
    private void TestViiper()
    {
        if (_hub.TestViiperInput())
        {
            SkillSuggestionStatus = "VIIPER test sent a virtual USB mouse click and F2 key.";
            InputMethodIndex = (int)InputMethod.Viiper;
        }
        else
        {
            SkillSuggestionStatus = "VIIPER test failed. Open the debug log and VIIPER.log from AppData\\Roaming\\4rVivi\\Logs.";
        }
        RefreshVirtualDriverStatus();
    }

    [RelayCommand]
    private async Task InstallMouseDriver()
    {
        try
        {
            var inf = TryFindPackagedMouseDriverInf();
            if (inf == null)
            {
                DebugTrace.Write("DriverInstall", "No packaged vmouse.inf + vmouse.sys found. Using FakerInput MSI installer.");
                SkillSuggestionStatus = "Downloading the official FakerInput virtual mouse installer...";
                await DownloadFakerInputInstaller();
                SkillSuggestionStatus = "Opening the FakerInput installer. Accept the Windows prompt, finish setup, then press Check driver.";
                DebugTrace.Write("DriverInstall", $"Launching FakerInput MSI: {FakerInputInstallerPath}");
                Process.Start(new ProcessStartInfo("msiexec.exe")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = $"/i \"{FakerInputInstallerPath}\""
                });
                RefreshVirtualDriverStatus();
                return;
            }

            DebugTrace.Write("DriverInstall", $"Launching pnputil for virtual mouse driver: {inf}");
            SkillSuggestionStatus = "Opening Windows driver installer. Accept the administrator prompt, then press Check driver.";
            Process.Start(new ProcessStartInfo("pnputil.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"/add-driver \"{inf}\" /install"
            });
        }
        catch (Exception ex)
        {
            DebugTrace.Write("DriverInstall", "Virtual mouse driver install failed.", ex);
            SkillSuggestionStatus = "Could not install the virtual mouse driver: " + ex.Message;
        }
        finally
        {
            RefreshVirtualDriverStatus();
        }
    }

    private static async Task DownloadMouseDriverSourceZip()
    {
        Directory.CreateDirectory(DriverInstallDir);
        if (File.Exists(MouseDriverSourceZipPath) && new FileInfo(MouseDriverSourceZipPath).Length > 32 * 1024)
            return;

        DebugTrace.Write("DriverInstall", $"Downloading vmouse source from {MouseDriverSourceZipUrl} to {MouseDriverSourceZipPath}.");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("4ViviTools/1.0");
        await using var webStream = await http.GetStreamAsync(MouseDriverSourceZipUrl);
        await using var fileStream = File.Create(MouseDriverSourceZipPath);
        await webStream.CopyToAsync(fileStream);
    }

    private static async Task DownloadFakerInputInstaller()
    {
        Directory.CreateDirectory(DriverInstallDir);
        if (File.Exists(FakerInputInstallerPath) && new FileInfo(FakerInputInstallerPath).Length > 128 * 1024)
            return;

        DebugTrace.Write("DriverInstall", $"Downloading FakerInput installer from {FakerInputInstallerUrl} to {FakerInputInstallerPath}.");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("4ViviTools/1.0");
        await using var webStream = await http.GetStreamAsync(FakerInputInstallerUrl);
        await using var fileStream = File.Create(FakerInputInstallerPath);
        await webStream.CopyToAsync(fileStream);
    }

    [RelayCommand]
    private void OpenMouseDriverSource()
    {
        try
        {
            var candidates = new[]
            {
                MouseDriverPackageDir,
                Path.Combine(AppContext.BaseDirectory, "Drivers", "vmouse"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "drivers", "vmouse")),
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tools", "drivers", "vmouse")),
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "vmouse"))
            };

            var dir = candidates.FirstOrDefault(Directory.Exists) ?? DriverInstallDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SkillSuggestionStatus = "Could not open mouse driver folder: " + ex.Message;
        }
    }

    private static bool IsVmouseDriverInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vmouse");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFakerInputInstalled()
    {
        try
        {
            using var rootDevice = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\FakerInput");
            if (rootDevice != null)
                return true;

            var umdfDriver = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "drivers",
                "UMDF",
                "FakerInput.dll");
            if (File.Exists(umdfDriver))
                return true;

            return HasFakerInputUninstallEntry(RegistryView.Registry64) || HasFakerInputUninstallEntry(RegistryView.Registry32);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasFakerInputUninstallEntry(RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null)
                return false;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                if (displayName?.Contains("FakerInput", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? TryFindPackagedMouseDriverInf()
    {
        foreach (var dir in MouseDriverSearchDirs())
        {
            try
            {
                var inf = Path.Combine(dir, "vmouse.inf");
                var sys = Path.Combine(dir, "vmouse.sys");
                if (File.Exists(inf) && File.Exists(sys))
                    return inf;
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> MouseDriverSearchDirs()
    {
        yield return MouseDriverPackageDir;
        yield return Path.Combine(AppContext.BaseDirectory, "Drivers", "vmouse");
        yield return Path.Combine(AppContext.BaseDirectory, "vmouse");
        yield return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Drivers", "vmouse"));
        yield return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tools", "drivers", "vmouse", "vmouse", "vmouse"));
    }

    private static string? FindReWasdExe()
    {
        foreach (var path in ReWasdExeCandidates())
        {
            try
            {
                if (File.Exists(path))
                    return path;
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? FindViiperExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIIPER", "viiper.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VIIPER", "viiper.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VIIPER", "viiper.exe"),
            Path.Combine(AppContext.BaseDirectory, "viiper.exe"),
            Path.Combine(AppContext.BaseDirectory, "Drivers", "VIIPER", "viiper.exe")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> ReWasdExeCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "reWASD", "reWASD.exe");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return Path.Combine(programFilesX86, "reWASD", "reWASD.exe");
        if (!string.IsNullOrWhiteSpace(localAppData))
            yield return Path.Combine(localAppData, "Programs", "reWASD", "reWASD.exe");
    }

    [RelayCommand]
    private void EnableVirtualController()
    {
        if (_hub.EnableVirtualController())
        {
            SkillSuggestionStatus = "Virtual Xbox controller enabled for this 4ViviTools session. It will be released when the tool closes.";
            RefreshVirtualDriverStatus();
            return;
        }

        SkillSuggestionStatus = "ViGEm is not ready. Install ViGEm, restart 4ViviTools, then press Enable controller.";
        RefreshVirtualDriverStatus();
    }

    [RelayCommand]
    private void EnableVirtualHid()
    {
        if (_hub.EnableVirtualHid())
        {
            SkillSuggestionStatus = "Virtual HID enabled. FakerInput/vmouse can now send mouse clicks, and FakerInput can send hotbar keys like F2.";
            RefreshVirtualDriverStatus();
            return;
        }

        SkillSuggestionStatus = "Virtual HID is not ready. Install FakerInput or the packaged vmouse driver, restart 4ViviTools, then press Enable virtual HID.";
        RefreshVirtualDriverStatus();
    }

    [RelayCommand]
    private void TestVirtualHid()
    {
        if (!_hub.TestVirtualHidClick())
        {
            SkillSuggestionStatus = "Virtual HID click failed. Install FakerInput/vmouse, restart 4ViviTools, then press Enable virtual HID.";
            RefreshVirtualDriverStatus();
            return;
        }

        var keyOk = _hub.TestVirtualHidKey("F2");
        SkillSuggestionStatus = keyOk
            ? "Virtual HID test sent a mouse click and F2 key. reWASD is not required."
            : "Virtual HID mouse click worked, but FakerInput keyboard did not connect. Install/repair FakerInput for skill keys.";
        RefreshVirtualDriverStatus();
    }

    [RelayCommand]
    private void TestVirtualClick()
    {
        _hub.VirtualClickButton = VirtualClickButton;
        _hub.VirtualClickHoldMs = VirtualClickHoldMs;

        if (!_hub.TestVirtualLeftClick())
        {
            SkillSuggestionStatus = "ViGEm driver is not ready. Install the ViGEm driver, then press Check driver.";
            RefreshVirtualDriverStatus();
            return;
        }

        bool reWasdRunning = false;
        try { reWasdRunning = _hub.IsReWasdRunning(); } catch { }
        SkillSuggestionStatus = reWasdRunning
            ? $"Tapped virtual Xbox {VirtualClickButton} for {VirtualClickHoldMs} ms. In reWASD, map this button to Left mouse button down/up."
            : $"Tapped virtual Xbox {VirtualClickButton}. reWASD is optional; use Virtual HID for direct driver clicks/keys without a profile bridge.";
        RefreshVirtualDriverStatus();
    }

    private void ApplyPersistedConfig(SmartBotConfig c)
    {
        var b = _hub.SmartBot;
        b.Enabled = c.Enabled;
        b.AttackKey = c.AttackKey ?? "";
        b.LootKey = c.LootKey ?? "";
        b.TeleportKey = c.TeleportKey ?? "";
        b.ReturnKey = c.ReturnKey ?? "";
        b.FleeAtHpPercent = c.FleeAtHpPercent;
        b.StuckMs = c.StuckMs > 0 ? Math.Clamp(c.StuckMs, 2000, 120000) : Math.Max(2, c.StuckSeconds) * 1000;
        b.FocusKillMs = c.FocusKillMs != 0
            ? (c.FocusKillMs < 0 ? -1 : Math.Clamp(c.FocusKillMs, 1000, 600000))
            : (c.FocusKillSeconds < 0 ? -1 : Math.Clamp(c.FocusKillSeconds, 1, 600) * 1000);
        b.NextMonsterDelayMs = c.NextMonsterDelayMs < 0 ? -1 : Math.Clamp(c.NextMonsterDelayMs, 0, 5000);
        b.ReturnAtWeightPercent = c.ReturnAtWeightPercent;
        b.RotationMs = c.RotationMs < 0 ? -1 : Math.Max(10, c.RotationMs);
        b.BuffIntervalMs = Math.Max(5, c.BuffIntervalSec) * 1000;
        b.ClickToMove = c.ClickToMove;
        b.ClickAttack = c.ClickAttack;
        b.MoveRadius = Math.Max(20, c.MoveRadius);
        b.MoveWaitMs = c.MoveWaitMs < 0 ? -1 : Math.Clamp(c.MoveWaitMs, 400, 5000);
        b.MoveStableMs = c.MoveStableMs < 0 ? -1 : Math.Clamp(c.MoveStableMs, 150, 3000);
        b.UseVision = c.UseVision;
        b.HardwareClick = c.HardwareClick;
        b.UseControllerButtons = c.UseControllerButtons;
        _hub.Autopot.UseControllerButtons = c.UseControllerButtons;
        b.WeaponKey = c.WeaponKey ?? "";
        b.AmmoKey = c.AmmoKey ?? "";
        b.AmmoBagKey = c.AmmoBagKey ?? "";
        b.EquipOnStart = c.EquipOnStart;
        b.StopAtAmmo = Math.Max(0, c.StopAtAmmo);
        b.ManualAmmoCount = Math.Max(0, c.AmmoCount);
        b.AmmoBags = Math.Max(0, c.AmmoBags);
        b.AmmoPerBag = Math.Max(1, c.AmmoPerBag);
        b.UseWalkBox = c.UseWalkBox;
        b.BoxX = c.BoxX;
        b.BoxY = c.BoxY;
        b.BoxW = c.BoxW;
        b.BoxH = c.BoxH;
        b.AutoReconnect = c.AutoReconnect;
        b.TargetMap = c.TargetMap ?? "";
        b.AmmoName = ItemDisplayName(c.AmmoName);
        b.AttackSkill = SkillDisplayName(c.AttackSkill);
        _hub.Autopot.Enabled = c.AutopotEnabled;
        _hub.InputMethod = Enum.IsDefined(typeof(InputMethod), c.InputMethod) ? c.InputMethod : InputMethod.ReWasdClick;
        _hub.VirtualClickButton = c.VirtualClickButton;
        _hub.VirtualClickHoldMs = Math.Clamp(c.VirtualClickHoldMs, 30, 500);
        _hub.VirtualClickFallback = c.VirtualClickFallback;

        b.SkillRotation.Clear();
        b.SkillSpRequired.Clear();
        b.SkillDelayMsByKey.Clear();
        b.ActionDelayMsByKey.Clear();
        if (c.SkillSpamEnabled)
        {
            foreach (var row in c.SkillButtons.Where(x => x.Enabled && x.IsSkill && !string.IsNullOrWhiteSpace(x.SkillName) && !string.IsNullOrWhiteSpace(x.Key)))
            {
                var key = row.Key.Trim();
                b.SkillRotation.Add(key);
                if (row.SpRequired > 0) b.SkillSpRequired[key] = row.SpRequired;
                b.SkillDelayMsByKey[key] = row.SkillDelayMs < 0
                    ? -1
                    : row.SkillDelayMs > 0
                        ? Math.Clamp(row.SkillDelayMs, 10, 5000)
                        : NormalizeAutoDelay(c.RotationMs, 10, 5000);
            }
        }

        b.BuffKeys.Clear();
        b.BuffIntervalByKeyMs.Clear();
        foreach (var row in c.SkillButtons.Where(x => x.Enabled && x.IsBuff && !string.IsNullOrWhiteSpace(x.Key)))
        {
            var key = row.Key.Trim();
            b.BuffKeys.Add(key);
            b.BuffIntervalByKeyMs[key] = Math.Max(5, row.BuffIntervalSec > 0 ? row.BuffIntervalSec : c.BuffIntervalSec) * 1000;
        }
        foreach (var key in c.BuffButtons.Where(x => x.Enabled).Select(x => x.Key).Where(x => !string.IsNullOrWhiteSpace(x)))
            if (!b.BuffKeys.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase))
                b.BuffKeys.Add(key.Trim());
        var teleport = c.SkillButtons.FirstOrDefault(x => x.Enabled && x.IsTeleport && !string.IsNullOrWhiteSpace(x.Key));
        if (teleport != null)
        {
            b.TeleportKey = teleport.Key.Trim();
            b.ActionDelayMsByKey[b.TeleportKey] = NormalizeAutoDelay(teleport.UseDelayMs, 50, 60000);
        }
        var loot = c.SkillButtons.FirstOrDefault(x => x.Enabled && x.IsLoot && !string.IsNullOrWhiteSpace(x.Key));
        if (loot != null)
        {
            b.LootKey = loot.Key.Trim();
            b.ActionDelayMsByKey[b.LootKey] = NormalizeAutoDelay(loot.UseDelayMs, 50, 60000);
        }
        var ret = c.SkillButtons.FirstOrDefault(x => x.Enabled && x.IsReturn && !string.IsNullOrWhiteSpace(x.Key));
        if (ret != null)
        {
            b.ReturnKey = ret.Key.Trim();
            b.ActionDelayMsByKey[b.ReturnKey] = NormalizeAutoDelay(ret.UseDelayMs, 50, 60000);
        }
        var weapon = c.SkillButtons.FirstOrDefault(x => x.Enabled && x.IsWeapon && !string.IsNullOrWhiteSpace(x.Key));
        if (weapon != null)
        {
            b.WeaponKey = weapon.Key.Trim();
            b.ActionDelayMsByKey[b.WeaponKey] = NormalizeAutoDelay(weapon.UseDelayMs, 50, 60000);
        }
        var ammo = c.SkillButtons.FirstOrDefault(x => x.Enabled && x.IsAmmo && !string.IsNullOrWhiteSpace(x.Key));
        if (ammo != null)
        {
            b.AmmoKey = ammo.Key.Trim();
            b.AmmoName = ItemDisplayName(ammo.ItemName);
            b.StopAtAmmo = Math.Max(0, ammo.StopAtAmmo);
            b.ManualAmmoCount = Math.Max(0, ammo.AmmoCount);
            b.ActionDelayMsByKey[b.AmmoKey] = NormalizeAutoDelay(ammo.UseDelayMs, 50, 60000);
        }
        var ammoBag = c.SkillButtons.FirstOrDefault(x => x.Enabled && x.IsAmmoBag && !string.IsNullOrWhiteSpace(x.Key));
        if (ammoBag != null)
        {
            b.AmmoBagKey = ammoBag.Key.Trim();
            b.AmmoBags = Math.Max(0, ammoBag.AmmoBags);
            b.AmmoPerBag = Math.Max(1, ammoBag.AmmoPerBag);
            if (string.IsNullOrWhiteSpace(b.AmmoName)) b.AmmoName = ItemDisplayName(ammoBag.ItemName);
            b.ActionDelayMsByKey[b.AmmoBagKey] = NormalizeAutoDelay(ammoBag.UseDelayMs, 50, 60000);
        }

        b.ReconnectKeys.Clear();
        foreach (var key in c.SkillButtons.Where(x => x.Enabled && x.IsReconnect).Select(x => x.Key).Concat(c.ReconnectKeys).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            b.ReconnectKeys.Add(key.Trim());
            var row = c.SkillButtons.LastOrDefault(x => x.Enabled && x.IsReconnect && string.Equals(x.Key?.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));
            if (row != null) b.ActionDelayMsByKey[key.Trim()] = NormalizeAutoDelay(row.UseDelayMs, 50, 60000);
        }

        b.Monsters.Clear();
        foreach (var m in c.Monsters)
            b.Monsters.Add(new MonsterRule
            {
                Name = MonsterDisplayName(m.Name),
                Attack = m.Attack,
                Estimate = EstimateMonsterKillText(m.Name),
                SkillKey = "",
                SkillCooldownMs = Math.Max(200, m.SkillCooldownMs),
            });
    }

    private static bool AnyActionKind(SmartSkillButtonConfig x)
        => x.IsSkill || x.IsBuff || x.IsTeleport || x.IsYgg || x.IsHpPot || x.IsSpPot || x.IsAmmo || x.IsAmmoBag || x.IsLoot || x.IsReturn || x.IsWeapon || x.IsReconnect;

    private static int NormalizeAutoDelay(int value, int min, int max)
        => value < 0 ? -1 : Math.Clamp(value, min, max);

    private static string ControllerOrDefault(string? value, string fallback)
        => ReWasdMouseMap.IsButtonName(value) ? ReWasdMouseMap.NormalizeName(value) : fallback;

    private void BuildSkillColumns()
    {
        string[][] cols =
            new[]
            {
                new[] { "F1", "1", "Q", "A", "Z" },
                new[] { "F2", "2", "W", "S", "X" },
                new[] { "F3", "3", "E", "D", "C" },
                new[] { "F4", "4", "R", "F", "V" },
                new[] { "F5", "5", "T", "G", "B" },
                new[] { "F6", "6", "Y", "H", "N" },
                new[] { "F7", "7", "U", "J", "M" },
                new[] { "F8", "8", "I", "K" },
                new[] { "F9", "9", "O", "L" },
            };

        SkillButtons.Clear();
        SkillColumns.Clear();
        foreach (var colKeys in cols)
        {
            var col = new ObservableCollection<SmartSkillButton>();
            foreach (var key in colKeys)
            {
                var button = new SmartSkillButton(key, SyncSkillButtons, DescribeSkill);
                SkillButtons.Add(button);
                col.Add(button);
            }
            SkillColumns.Add(col);
        }
    }

    private void LoadSmartSkillButtonsFromRotation()
    {
        var enabled = _hub.SmartBot.SkillRotation.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var b in SkillButtons)
            b.Enabled = enabled.Contains(b.Key);
    }

    private void LoadSmartSkillButtonsFromConfig(SmartBotConfig config)
    {
        var byKey = config.SkillButtons
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var b in SkillButtons)
        {
            if (byKey.TryGetValue(b.Key, out var saved))
            {
                b.Enabled = saved.Enabled;
                b.SkillName = SkillDisplayName(saved.SkillName);
                b.SkillLevel = Math.Max(1, saved.SkillLevel);
                b.SpRequired = Math.Max(0, saved.SpRequired);
                b.BuffIntervalSec = Math.Max(5, saved.BuffIntervalSec > 0 ? saved.BuffIntervalSec : BuffIntervalSec);
                b.PotPercent = Math.Clamp(saved.PotPercent > 0 ? saved.PotPercent : 50, 1, 100);
                b.ItemName = ItemDisplayName(saved.ItemName);
                b.SkillDelayMs = saved.SkillDelayMs < 0
                    ? -1
                    : saved.SkillDelayMs > 0
                        ? Math.Clamp(saved.SkillDelayMs, 10, 5000)
                        : NormalizeAutoDelay(RotationMs, 10, 5000);
                b.ReactionMs = saved.ReactionMs < 0 ? -1 : Math.Clamp(saved.ReactionMs, 0, 5000);
                b.UseDelayMs = saved.UseDelayMs < 0 ? -1 : (saved.UseDelayMs > 0 ? Math.Clamp(saved.UseDelayMs, 50, 60000) : -1);
                b.StopAtAmmo = Math.Max(0, saved.StopAtAmmo);
                b.AmmoCount = Math.Max(0, saved.AmmoCount);
                b.AmmoBags = Math.Max(0, saved.AmmoBags);
                b.AmmoPerBag = Math.Max(1, saved.AmmoPerBag);
                var hasKind = AnyActionKind(saved);
                b.IsSkill = (saved.IsSkill || (!hasKind && saved.Enabled)) && !string.IsNullOrWhiteSpace(saved.SkillName);
                b.IsBuff = saved.IsBuff;
                b.IsTeleport = saved.IsTeleport;
                b.IsYgg = saved.IsYgg;
                b.IsHpPot = saved.IsHpPot;
                b.IsSpPot = saved.IsSpPot;
                b.IsAmmo = saved.IsAmmo;
                b.IsAmmoBag = saved.IsAmmoBag;
                b.IsLoot = saved.IsLoot;
                b.IsReturn = saved.IsReturn;
                b.IsWeapon = saved.IsWeapon;
                b.IsReconnect = saved.IsReconnect;
            }
            else
            {
                b.Enabled = _hub.SmartBot.SkillRotation.Contains(b.Key, StringComparer.OrdinalIgnoreCase);
                b.SpRequired = _hub.SmartBot.SkillSpRequired.TryGetValue(b.Key, out var sp) ? sp : 0;
                b.SkillLevel = 1;
                b.SkillDelayMs = _hub.SmartBot.SkillDelayMsByKey.TryGetValue(b.Key, out var delay) ? delay : -1;
                b.IsSkill = b.Enabled;
            }
        }
    }

    private SmartSkillButton? HotbarButtonForKey(string? key)
    {
        key = (key ?? "").Trim();
        return key.Length == 0 ? null : SkillButtons.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadUnifiedActionsFromLegacy(SmartBotConfig config)
    {
        foreach (var saved in config.BuffButtons.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
        {
            var b = HotbarButtonForKey(saved.Key);
            if (b == null || b.HasConfiguredAction) continue;
            b.Enabled = true;
            b.SkillName = SkillDisplayName(saved.SkillName);
            b.BuffIntervalSec = Math.Max(5, saved.BuffIntervalSec > 0 ? saved.BuffIntervalSec : BuffIntervalSec);
            b.IsBuff = true;
        }

        foreach (var pot in _settings.Current.GetActiveProfile().Pots.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)))
        {
            var b = HotbarButtonForKey(pot.Key);
            if (b == null || b.HasConfiguredAction) continue;
            b.Enabled = true;
            b.ItemName = ItemDisplayName(pot.Name);
            b.PotPercent = Math.Clamp(pot.Percent, 1, 100);
            if (pot.UseSp) b.IsSpPot = true;
            else b.IsHpPot = true;
        }

        foreach (var key in _hub.SmartBot.BuffKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            var b = HotbarButtonForKey(key);
            if (b == null || b.HasConfiguredAction) continue;
            b.Enabled = true;
            b.BuffIntervalSec = BuffIntervalSec;
            b.IsBuff = true;
        }

        var tp = HotbarButtonForKey(config.TeleportKey);
        if (tp != null && !tp.HasConfiguredAction)
        {
            tp.Enabled = true;
            tp.IsTeleport = true;
        }

        ApplyLegacyAction(config.LootKey, b => b.IsLoot = true);
        ApplyLegacyAction(config.ReturnKey, b => b.IsReturn = true);
        ApplyLegacyAction(config.WeaponKey, b => b.IsWeapon = true);
        ApplyLegacyAction(config.AmmoKey, b =>
        {
            b.IsAmmo = true;
            b.ItemName = ItemDisplayName(config.AmmoName);
            b.StopAtAmmo = Math.Max(0, config.StopAtAmmo);
            b.AmmoCount = Math.Max(0, config.AmmoCount);
        });
        ApplyLegacyAction(config.AmmoBagKey, b =>
        {
            b.IsAmmoBag = true;
            b.ItemName = ItemDisplayName(config.AmmoName);
            b.AmmoBags = Math.Max(0, config.AmmoBags);
            b.AmmoPerBag = Math.Max(1, config.AmmoPerBag);
        });
        foreach (var reconnectKey in config.ReconnectKeys)
            ApplyLegacyAction(reconnectKey, b => b.IsReconnect = true);
    }

    private void ApplyLegacyAction(string? key, Action<SmartSkillButton> apply)
    {
        var b = HotbarButtonForKey(key);
        if (b == null || b.HasConfiguredAction) return;
        b.Enabled = true;
        apply(b);
    }

    private void LoadBuffButtonsFromKeys()
    {
        var enabled = _hub.SmartBot.BuffKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var b in BuffButtons)
            b.Enabled = enabled.Contains(b.Key);
    }

    private void LoadBuffButtonsFromConfig(SmartBotConfig config)
    {
        BuffButtons.Clear();
        if (config.BuffButtons.Count > 0)
        {
            foreach (var saved in config.BuffButtons)
            {
                var b = new SmartSkillButton(saved.Key ?? "", SyncBuffButtons, DescribeSkill)
                {
                    Enabled = saved.Enabled,
                    SkillName = SkillDisplayName(saved.SkillName),
                    IsBuff = true,
                    BuffIntervalSec = Math.Max(5, saved.BuffIntervalSec),
                };
                BuffButtons.Add(b);
            }
        }
        else
        {
            foreach (var key in new[] { "F5", "F6", "F7" })
                BuffButtons.Add(new SmartSkillButton(key, SyncBuffButtons, DescribeSkill) { IsBuff = true, BuffIntervalSec = BuffIntervalSec });
            LoadBuffButtonsFromKeys();
        }
    }

    private (string hint, int delayMs, int maxLevel) DescribeSkill(string name, int level)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return ("", 0, 1);
        try
        {
            var db = _db.Value;
            var skill = db.SkillByName(name) ?? db.SearchSkills(name, 1).FirstOrDefault();
            if (skill == null) return ("No rAthena metadata found for this skill.", 0, 1);
            int rec = skill.RecommendedSpamDelayMs;
            int maxLevel = Math.Max(1, skill.MaxLevel);
            level = Math.Clamp(level, 1, maxLevel);
            var parts = new List<string>();
            parts.Add($"Lv {level}/{maxLevel}");
            if (!string.IsNullOrWhiteSpace(skill.Type)) parts.Add(skill.Type);
            if (!string.IsNullOrWhiteSpace(skill.Element)) parts.Add(skill.Element);
            if (skill.Hits > 1) parts.Add($"{skill.Hits} hits");
            if (skill.CastTimeMs > 0) parts.Add($"cast {skill.CastTimeMs} ms");
            if (skill.AfterCastDelayMs > 0) parts.Add($"delay {skill.AfterCastDelayMs} ms");
            if (skill.CooldownMs > 0) parts.Add($"cooldown {skill.CooldownMs} ms");
            if (rec > 0) parts.Add($"suggest >= {rec} ms");
            return (string.Join(" | ", parts), rec, maxLevel);
        }
        catch
        {
            return ("Skill metadata is not loaded yet.", 0, 1);
        }
    }

    private void RefreshAttackSkillInfo()
    {
        var (hint, delayMs, _) = DescribeSkill(AttackSkill, 1);
        AttackSkillHint = hint;
        AttackSkillSuggestedDelayMs = delayMs;
    }

    private void SyncSkillButtons()
    {
        if (_hub == null) return;
        var skillRows = SkillButtons
            .Where(b => b.Enabled && b.IsSkill && !string.IsNullOrWhiteSpace(b.SkillName) && !string.IsNullOrWhiteSpace(b.Key))
            .ToList();
        var blankSkillRows = SkillButtons.Count(b => b.Enabled && b.IsSkill && string.IsNullOrWhiteSpace(b.SkillName));
        var keys = string.Equals(SkillClickMode, "Deactivated", StringComparison.OrdinalIgnoreCase)
            ? new List<string>()
            : skillRows
            .Select(b => b.Key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _hub.SmartBot.SkillRotation.Clear();
        _hub.SmartBot.SkillSpRequired.Clear();
        _hub.SmartBot.SkillDelayMsByKey.Clear();
        _hub.SmartBot.ActionDelayMsByKey.Clear();
        foreach (var k in keys)
        {
            _hub.SmartBot.SkillRotation.Add(k);
            var row = SkillButtons.FirstOrDefault(b => string.Equals(b.Key, k, StringComparison.OrdinalIgnoreCase));
            if (row?.SpRequired > 0) _hub.SmartBot.SkillSpRequired[k] = row.SpRequired;
            _hub.SmartBot.SkillDelayMsByKey[k] = NormalizeAutoDelay(row?.SkillDelayMs ?? -1, 10, 5000);
        }
        Rotation = string.Join(", ", keys);
        if (blankSkillRows > 0)
            SkillSuggestionStatus = $"{blankSkillRows} checked skill key(s) have no skill selected, so they were skipped.";

        var buffRows = SkillButtons
            .Where(b => b.Enabled && b.IsBuff && !string.IsNullOrWhiteSpace(b.Key))
            .GroupBy(b => b.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
        _hub.SmartBot.BuffKeys.Clear();
        _hub.SmartBot.BuffIntervalByKeyMs.Clear();
        foreach (var b in buffRows)
        {
            var key = b.Key.Trim();
            _hub.SmartBot.BuffKeys.Add(key);
            _hub.SmartBot.BuffIntervalByKeyMs[key] = Math.Max(5, b.BuffIntervalSec) * 1000;
        }
        BuffKeys = string.Join(", ", buffRows.Select(b => b.Key.Trim()));

        var teleport = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsTeleport && !string.IsNullOrWhiteSpace(b.Key));
        if (teleport != null)
        {
            _hub.SmartBot.TeleportKey = teleport.Key.Trim();
            _hub.SmartBot.ActionDelayMsByKey[_hub.SmartBot.TeleportKey] = NormalizeAutoDelay(teleport.UseDelayMs, 50, 60000);
            _syncingUnifiedActions = true;
            try { TeleportKey = _hub.SmartBot.TeleportKey; }
            finally { _syncingUnifiedActions = false; }
        }

        var loot = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsLoot && !string.IsNullOrWhiteSpace(b.Key));
        if (loot != null)
        {
            _hub.SmartBot.LootKey = loot.Key.Trim();
            _hub.SmartBot.ActionDelayMsByKey[_hub.SmartBot.LootKey] = NormalizeAutoDelay(loot.UseDelayMs, 50, 60000);
            LootKey = _hub.SmartBot.LootKey;
        }

        var ret = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsReturn && !string.IsNullOrWhiteSpace(b.Key));
        if (ret != null)
        {
            _hub.SmartBot.ReturnKey = ret.Key.Trim();
            _hub.SmartBot.ActionDelayMsByKey[_hub.SmartBot.ReturnKey] = NormalizeAutoDelay(ret.UseDelayMs, 50, 60000);
            ReturnKey = _hub.SmartBot.ReturnKey;
        }

        var weapon = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsWeapon && !string.IsNullOrWhiteSpace(b.Key));
        if (weapon != null)
        {
            _hub.SmartBot.WeaponKey = weapon.Key.Trim();
            _hub.SmartBot.ActionDelayMsByKey[_hub.SmartBot.WeaponKey] = NormalizeAutoDelay(weapon.UseDelayMs, 50, 60000);
            WeaponKey = _hub.SmartBot.WeaponKey;
        }

        var ammo = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsAmmo && !string.IsNullOrWhiteSpace(b.Key));
        if (ammo != null)
        {
            _hub.SmartBot.AmmoKey = ammo.Key.Trim();
            AmmoKey = _hub.SmartBot.AmmoKey;
            _hub.SmartBot.AmmoName = ItemDisplayName(ammo.ItemName);
            _hub.SmartBot.StopAtAmmo = Math.Max(0, ammo.StopAtAmmo);
            _hub.SmartBot.ManualAmmoCount = Math.Max(0, ammo.AmmoCount);
            _hub.SmartBot.ActionDelayMsByKey[_hub.SmartBot.AmmoKey] = NormalizeAutoDelay(ammo.UseDelayMs, 50, 60000);
            AmmoName = _hub.SmartBot.AmmoName;
            StopAtAmmo = _hub.SmartBot.StopAtAmmo;
            AmmoCount = _hub.SmartBot.ManualAmmoCount;
        }

        var ammoBag = SkillButtons.FirstOrDefault(b => b.Enabled && b.IsAmmoBag && !string.IsNullOrWhiteSpace(b.Key));
        if (ammoBag != null)
        {
            _hub.SmartBot.AmmoBagKey = ammoBag.Key.Trim();
            AmmoBagKey = _hub.SmartBot.AmmoBagKey;
            _hub.SmartBot.AmmoBags = Math.Max(0, ammoBag.AmmoBags);
            _hub.SmartBot.AmmoPerBag = Math.Max(1, ammoBag.AmmoPerBag);
            _hub.SmartBot.ActionDelayMsByKey[_hub.SmartBot.AmmoBagKey] = NormalizeAutoDelay(ammoBag.UseDelayMs, 50, 60000);
            if (string.IsNullOrWhiteSpace(_hub.SmartBot.AmmoName))
                _hub.SmartBot.AmmoName = ItemDisplayName(ammoBag.ItemName);
            AmmoBags = _hub.SmartBot.AmmoBags;
            AmmoPerBag = _hub.SmartBot.AmmoPerBag;
        }

        var reconnectRows = SkillButtons
            .Where(b => b.Enabled && b.IsReconnect && !string.IsNullOrWhiteSpace(b.Key))
            .Select(b => b.Key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _hub.SmartBot.ReconnectKeys.Clear();
        foreach (var key in reconnectRows)
        {
            _hub.SmartBot.ReconnectKeys.Add(key);
            var row = SkillButtons.LastOrDefault(b => b.Enabled && b.IsReconnect && string.Equals(b.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            if (row != null) _hub.SmartBot.ActionDelayMsByKey[key] = NormalizeAutoDelay(row.UseDelayMs, 50, 60000);
        }
        _syncingUnifiedActions = true;
        try
        {
            ReconnectKeys = string.Join(", ", reconnectRows);
            ReconnectKey1 = reconnectRows.Count > 0 ? reconnectRows[0] : "";
            ReconnectKey2 = reconnectRows.Count > 1 ? reconnectRows[1] : "";
            ReconnectKey3 = reconnectRows.Count > 2 ? reconnectRows[2] : "";
        }
        finally { _syncingUnifiedActions = false; }

        SyncActionPotRules();
        _syncingUnifiedActions = true;
        try { SkillSpamEnabled = skillRows.Count > 0; }
        finally { _syncingUnifiedActions = false; }
        RefreshMonsterEstimates();
        EnsureControllerMappingsForActiveActions();
        SaveBotProfile();
    }

    private void SyncBuffButtons()
    {
        if (_hub == null) return;
        var keys = BuffButtons
            .Where(b => b.Enabled && !string.IsNullOrWhiteSpace(b.Key))
            .Select(b => b.Key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _hub.SmartBot.BuffKeys.Clear();
        foreach (var k in keys) _hub.SmartBot.BuffKeys.Add(k);
        BuffKeys = string.Join(", ", keys);
        foreach (var b in BuffButtons.Where(b => b.Enabled && !string.IsNullOrWhiteSpace(b.Key)))
        {
            var hotbar = HotbarButtonForKey(b.Key);
            if (hotbar == null || hotbar.HasConfiguredAction) continue;
            hotbar.Enabled = true;
            hotbar.SkillName = b.SkillName;
            hotbar.BuffIntervalSec = BuffIntervalSec;
            hotbar.IsBuff = true;
        }
        EnsureControllerMappingsForActiveActions();
        SaveBotProfile();
    }

    private void SyncActionPotRules()
    {
        var profile = _settings.Current.GetActiveProfile();
        var actionRows = SkillButtons
            .Where(b => b.Enabled && (b.IsHpPot || b.IsSpPot || b.IsYgg) && !string.IsNullOrWhiteSpace(b.Key))
            .ToList();
        var actionKeys = new HashSet<string>(SkillButtons.Select(b => b.Key), StringComparer.OrdinalIgnoreCase);
        var activePotKeys = new HashSet<string>(actionRows.Select(b => b.Key.Trim()), StringComparer.OrdinalIgnoreCase);

        for (int i = profile.Pots.Count - 1; i >= 0; i--)
        {
            var pot = profile.Pots[i];
            if (actionKeys.Contains(pot.Key) && !activePotKeys.Contains(pot.Key))
            {
                _hub.Autopot.Rules.Remove(pot);
                profile.Pots.RemoveAt(i);
            }
        }

        foreach (var row in actionRows)
        {
            var key = row.Key.Trim();
            var useSp = row.IsSpPot;
            var pot = profile.Pots.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase) && p.UseSp == useSp);
            if (pot == null)
            {
                pot = new PotConfig { Key = key, UseSp = useSp, ReactionMs = -1, UseDelayMs = -1 };
                profile.Pots.Add(pot);
                _hub.Autopot.Rules.Add(pot);
            }
            pot.Enabled = true;
            pot.Name = ItemDisplayName(row.ItemName);
            pot.Percent = Math.Clamp(row.PotPercent, 1, 100);
            pot.ReactionMs = NormalizeAutoDelay(row.ReactionMs, 0, 5000);
            pot.UseDelayMs = NormalizeAutoDelay(row.UseDelayMs, 50, 60000);
        }

        _hub.Autopot.Enabled = AutopotEnabled || actionRows.Count > 0;
        _syncingUnifiedActions = true;
        try { AutopotEnabled = _hub.Autopot.Enabled; }
        finally { _syncingUnifiedActions = false; }
        Pots.Clear();
        foreach (var c in profile.Pots) Pots.Add(new PotRowViewModel(c, OnPotChanged));
    }

    private void SyncReconnectKeys()
    {
        if (_hub == null) return;
        if (_syncingUnifiedActions) return;
        var keys = new[] { ReconnectKey1, ReconnectKey2, ReconnectKey3 }
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToList();
        _hub.SmartBot.ReconnectKeys.Clear();
        foreach (var k in keys) _hub.SmartBot.ReconnectKeys.Add(k);
        ReconnectKeys = string.Join(", ", keys);
        EnsureControllerMappingsForActiveActions();
        SaveBotProfile();
    }

    private static IReadOnlyList<string> ControllerKeyboardKeys { get; } =
        new[]
        {
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
            "1","2","3","4","5","6","7","8","9","0",
            "A","B","C","D","E","F","G","H","I","J","K","L","M",
            "N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
        };

    private void LoadControllerKeyMap(SmartBotConfig config)
    {
        var saved = config.ControllerKeyMap
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Button ?? "", StringComparer.OrdinalIgnoreCase);

        ControllerKeyMapRows.Clear();
        foreach (var key in ActiveControllerActionKeys())
        {
            var button = saved.TryGetValue(key, out var mapped) && ReWasdMouseMap.IsButtonChord(mapped)
                ? ReWasdMouseMap.NormalizeChord(mapped)
                : NextAvailableControllerChord();
            ControllerKeyMapRows.Add(new ControllerKeyMapRow(key, button, SyncControllerKeyMap));
        }
        SyncControllerKeyMap();
    }

    private IReadOnlyList<string> ActiveControllerActionKeys()
    {
        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;
            if (ControllerKeyboardKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                keys.Add(key);
        }

        foreach (var b in SkillButtons.Where(b => b.Enabled && b.HasConfiguredAction)) Add(b.Key);
        return keys.ToList();
    }

    private string NextAvailableControllerChord(HashSet<string>? reserved = null)
    {
        reserved ??= ControllerKeyMapRows
            .Select(r => ReWasdMouseMap.NormalizeChord(r.Button))
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ReWasdMouseMap.ButtonChordNames(UseControllerCombos))
        {
            var normalized = ReWasdMouseMap.NormalizeChord(candidate);
            if (normalized.Length > 0 && !reserved.Contains(normalized))
                return normalized;
        }
        return ReWasdMouseMap.NormalizeName("A");
    }

    private void EnsureControllerMappingsForActiveActions(bool forceUnique = false)
    {
        if (_syncingControllerMap || _hub == null) return;
        _syncingControllerMap = true;
        try
        {
            var active = ActiveControllerActionKeys();
            var activeSet = active.ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = ControllerKeyMapRows.Count - 1; i >= 0; i--)
                if (!activeSet.Contains(ControllerKeyMapRows[i].Key))
                    ControllerKeyMapRows.RemoveAt(i);

            var byKey = ControllerKeyMapRows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var key in active)
            {
                if (!byKey.ContainsKey(key))
                {
                    var row = new ControllerKeyMapRow(key, "", SyncControllerKeyMap);
                    ControllerKeyMapRows.Add(row);
                    byKey[key] = row;
                }
            }

            var activeOrder = active.Select((key, index) => (key, index))
                .ToDictionary(x => x.key, x => x.index, StringComparer.OrdinalIgnoreCase);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool ranOut = false;
            foreach (var row in ControllerKeyMapRows.OrderBy(r => activeOrder.TryGetValue(r.Key, out var order) ? order : int.MaxValue))
            {
                var chord = ReWasdMouseMap.NormalizeChord(row.Button);
                bool duplicate = chord.Length > 0 && reserved.Contains(chord);
                bool comboNotAllowed = chord.Contains('+') && !UseControllerCombos;
                if (forceUnique || chord.Length == 0 || duplicate || comboNotAllowed)
                    chord = NextAvailableControllerChord(reserved);

                if (reserved.Contains(chord))
                    ranOut = true;
                else
                    reserved.Add(chord);

                row.Button = chord;
            }

            SkillSuggestionStatus = ranOut
                ? "Controller buttons are full. Enable two-button combos to avoid shared mappings."
                : $"Controller map ready: {ControllerKeyMapRows.Count} RO key(s) assigned without clashes.";
        }
        finally
        {
            _syncingControllerMap = false;
        }

        SyncControllerKeyMap();
        RefreshActionControllerLabels();
    }

    private void SyncControllerKeyMap()
    {
        if (_hub == null || _syncingControllerMap) return;
        if (ControllerKeyMapRows.Any(r => ReWasdMouseMap.NormalizeChord(r.Button).Length == 0
                || (!UseControllerCombos && ReWasdMouseMap.NormalizeChord(r.Button).Contains('+')))
            || ControllerKeyMapRows.Select(r => ReWasdMouseMap.NormalizeChord(r.Button))
                .Where(s => s.Length > 0)
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
        {
            EnsureControllerMappingsForActiveActions(forceUnique: true);
            return;
        }

        _hub.SmartBot.ControllerKeyMap.Clear();
        _hub.Autopot.ControllerKeyMap.Clear();
        foreach (var row in ControllerKeyMapRows)
        {
            var chord = ReWasdMouseMap.NormalizeChord(row.Button);
            if (!string.IsNullOrWhiteSpace(row.Key) && chord.Length > 0)
            {
                _hub.SmartBot.ControllerKeyMap[row.Key] = chord;
                _hub.Autopot.ControllerKeyMap[row.Key] = chord;
            }
        }
        DebugTrace.Write("SmartBotVM", $"Synced controller key map rows={ControllerKeyMapRows.Count} valid={_hub.SmartBot.ControllerKeyMap.Count}.");
        RefreshActionControllerLabels();
        SaveBotProfile();
    }

    private void RefreshActionControllerLabels()
    {
        var map = ControllerKeyMapRows
            .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ReWasdMouseMap.NormalizeChord(g.Last().Button), StringComparer.OrdinalIgnoreCase);
        foreach (var b in SkillButtons)
            b.ControllerButton = map.TryGetValue(b.Key, out var button) ? button : "";
    }

    private void LoadPotRules()
    {
        var prof = _settings.Current.GetActiveProfile();
        if (prof.Pots.Count == 0)
            prof.Pots.Add(new PotConfig { Enabled = true, Key = "F1", Percent = 50, UseSp = false, ReactionMs = -1, UseDelayMs = -1 });

        if (_hub.Autopot.Rules.Count == 0)
            foreach (var c in prof.Pots) _hub.Autopot.Rules.Add(c);

        Pots.Clear();
        foreach (var c in prof.Pots) Pots.Add(new PotRowViewModel(c, OnPotChanged));
    }

    private void SaveSettings() => _settings.Save();

    private void OnPotChanged()
    {
        SaveSettings();
        EnsureControllerMappingsForActiveActions();
        SaveBotProfile();
    }

    private void SaveBotProfile()
    {
        if (_hydrating || _settings == null || _hub == null) return;

        var profile = _settings.Current.GetActiveProfile();
        profile.SmartBot ??= new SmartBotConfig();
        var c = profile.SmartBot;
        c.Enabled = Enabled;
        c.AttackKey = AttackKey ?? "";
        c.LootKey = LootKey ?? "";
        c.TeleportKey = TeleportKey ?? "";
        c.ReturnKey = ReturnKey ?? "";
        c.SkillButtons = SkillButtons
            .Where(b => b.Enabled || b.HasConfiguredAction || !string.IsNullOrWhiteSpace(b.SkillName) || b.SpRequired > 0)
            .Select(b => new SmartSkillButtonConfig
            {
                Enabled = b.Enabled,
                Key = b.Key ?? "",
                SkillName = SkillDisplayName(b.SkillName),
                SpRequired = Math.Max(0, b.SpRequired),
                SkillLevel = Math.Max(1, b.SkillLevel),
                IsSkill = b.IsSkill && !string.IsNullOrWhiteSpace(b.SkillName),
                IsBuff = b.IsBuff,
                IsTeleport = b.IsTeleport,
                IsYgg = b.IsYgg,
                IsHpPot = b.IsHpPot,
                IsSpPot = b.IsSpPot,
                IsAmmo = b.IsAmmo,
                IsAmmoBag = b.IsAmmoBag,
                IsLoot = b.IsLoot,
                IsReturn = b.IsReturn,
                IsWeapon = b.IsWeapon,
                IsReconnect = b.IsReconnect,
                ItemName = ItemDisplayName(b.ItemName),
                SkillDelayMs = NormalizeAutoDelay(b.SkillDelayMs, 10, 5000),
                ReactionMs = NormalizeAutoDelay(b.ReactionMs, 0, 5000),
                UseDelayMs = NormalizeAutoDelay(b.UseDelayMs, 50, 60000),
                StopAtAmmo = Math.Max(0, b.StopAtAmmo),
                AmmoCount = Math.Max(0, b.AmmoCount),
                AmmoBags = Math.Max(0, b.AmmoBags),
                AmmoPerBag = Math.Max(1, b.AmmoPerBag),
                BuffIntervalSec = Math.Max(5, b.BuffIntervalSec),
                PotPercent = Math.Clamp(b.PotPercent, 1, 100),
            })
            .ToList();
        c.BuffButtons = SkillButtons.Where(b => b.Enabled && b.IsBuff)
            .Select(b => new SmartSkillButtonConfig
            {
                Enabled = true,
                Key = b.Key ?? "",
                SkillName = SkillDisplayName(b.SkillName),
                IsBuff = true,
                BuffIntervalSec = Math.Max(5, b.BuffIntervalSec),
            })
            .Concat(BuffButtons
            .Where(b => b.Enabled || !string.IsNullOrWhiteSpace(b.Key) || !string.IsNullOrWhiteSpace(b.SkillName))
            .Select(b => new SmartSkillButtonConfig
            {
                Enabled = b.Enabled,
                Key = b.Key ?? "",
                SkillName = SkillDisplayName(b.SkillName),
                IsBuff = true,
                BuffIntervalSec = Math.Max(5, b.BuffIntervalSec),
            }))
            .ToList();
        c.ControllerKeyMap = ControllerKeyMapRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Key) && ReWasdMouseMap.IsButtonChord(r.Button))
            .Select(r => new ControllerKeyMapConfig
            {
                Key = r.Key,
                Button = ReWasdMouseMap.NormalizeChord(r.Button),
            })
            .ToList();
        c.Monsters = Monsters
            .Select(m => new MonsterRule
            {
                Name = MonsterDisplayName(m.Name),
                Attack = m.Attack,
                Estimate = EstimateMonsterKillText(m.Name),
                SkillKey = "",
                SkillCooldownMs = Math.Max(200, m.SkillCooldownMs),
            })
            .ToList();
        c.FleeAtHpPercent = FleeAtHpPercent;
        c.StuckMs = Math.Clamp(StuckMs, 2000, 120000);
        c.FocusKillMs = FocusKillMs < 0 ? -1 : Math.Clamp(FocusKillMs, 1000, 600000);
        c.StuckSeconds = Math.Max(2, c.StuckMs / 1000);
        c.FocusKillSeconds = c.FocusKillMs < 0 ? -1 : Math.Max(1, (int)Math.Ceiling(c.FocusKillMs / 1000.0));
        c.NextMonsterDelayMs = NextMonsterDelayMs < 0 ? -1 : Math.Clamp(NextMonsterDelayMs, 0, 5000);
        c.ReturnAtWeightPercent = ReturnAtWeightPercent;
        c.RotationMs = RotationMs;
        c.SkillSpamEnabled = SkillSpamEnabled;
        c.SkillClickMode = SkillClickMode ?? "No mouse click";
        c.AhkMode = AhkMode ?? "Compatibility";
        c.MouseFlick = MouseFlick;
        c.NoShift = NoShift;
        c.BuffIntervalSec = BuffIntervalSec;
        c.ClickToMove = ClickToMove;
        c.ClickAttack = ClickAttack;
        c.MoveRadius = MoveRadius;
        c.MoveWaitMs = MoveWaitMs;
        c.MoveStableMs = MoveStableMs;
        c.UseVision = UseVision;
        c.HardwareClick = HardwareClick;
        c.UseControllerButtons = UseControllerButtons;
        c.UseControllerCombos = UseControllerCombos;
        c.ShowControllerAssignments = ShowControllerAssignments;
        c.ShowAdvancedTiming = ShowAdvancedTiming;
        c.AutopotEnabled = AutopotEnabled;
        c.InputMethod = (InputMethod)InputMethodIndex;
        c.VirtualClickButton = VirtualClickButton ?? "A";
        c.VirtualClickHoldMs = Math.Clamp(VirtualClickHoldMs, 30, 500);
        c.VirtualClickFallback = VirtualClickFallback;
        c.ToggleHotkey = ToggleHotkey ?? "";
        c.StartHotkey = StartHotkey ?? "";
        c.StopHotkey = StopHotkey ?? "";
        c.WeaponKey = WeaponKey ?? "";
        c.AmmoKey = AmmoKey ?? "";
        c.AmmoBagKey = AmmoBagKey ?? "";
        c.EquipOnStart = EquipOnStart;
        c.StopAtAmmo = StopAtAmmo;
        c.AmmoCount = AmmoCount;
        c.AmmoBags = AmmoBags;
        c.AmmoPerBag = AmmoPerBag;
        c.UseWalkBox = UseWalkBox;
        c.ShowWalkBoxOverlay = ShowWalkBoxOverlay;
        c.BoxX = BoxX;
        c.BoxY = BoxY;
        c.BoxW = BoxW;
        c.BoxH = BoxH;
        c.AutoReconnect = AutoReconnect;
        c.ReconnectKeys = _hub.SmartBot.ReconnectKeys.ToList();
        c.TargetMap = TargetMap ?? "";
        c.AmmoName = ItemDisplayName(AmmoName);
        c.AttackSkill = SkillDisplayName(AttackSkill);
        _settings.Save();
    }

    [RelayCommand] private void RefreshAddresses()
    {
        string Mark(string role) => _session.HasRole(role) ? "OK" : "missing";
        AddressStatus =
            $"HP {Mark(Roles.Hp)}   EXP {Mark(Roles.Exp)}   Weight {Mark(Roles.Weight)}/{Mark(Roles.MaxWeight)}   Pos {Mark(Roles.PosX)}/{Mark(Roles.PosY)}   Ammo {Mark(Roles.Ammo)}   " +
            "- set missing ones in the Scanner (bot still works best-effort without them).";
    }
}
