using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Data;
using FourRVivi.Core.Tools;

namespace FourRVivi.App.ViewModels;

public sealed partial class CalculatorViewModel : ViewModelBase
{
    private readonly Lazy<GameDatabase> _db;

    public string[] Classes { get; } =
    {
        "Novice","Swordman","Knight","Lord Knight","Crusader","Paladin","Mage","Wizard","High Wizard",
        "Sage","Professor","Archer","Hunter","Sniper","Bard","Clown","Dancer","Gypsy","Acolyte","Priest",
        "High Priest","Monk","Champion","Merchant","Blacksmith","Whitesmith","Alchemist","Creator",
        "Thief","Assassin","Assassin Cross","Rogue","Stalker","Gunslinger","Ninja","Super Novice"
    };
    public string[] WeaponTypes { get; } =
    {
        "Bare fist","Dagger","Sword","Two-hand Sword","Spear","Two-hand Spear","Axe","Mace",
        "Staff","Bow","Katar","Book","Knuckle","Instrument","Whip","Gun"
    };
    public string[] Slots { get; } =
    {
        "Any","Head_Top","Head_Mid","Head_Low","Armor","Right_Hand","Left_Hand","Both_Hand",
        "Garment","Shoes","Right_Accessory","Left_Accessory",
        "Costume_Head_Top","Costume_Head_Mid","Costume_Head_Low","Costume_Garment"
    };

    [ObservableProperty] private string _selectedClass = "Knight";
    [ObservableProperty] private string _selectedWeapon = "Sword";
    [ObservableProperty] private int _baseLevel = 99;
    [ObservableProperty] private int _str = 1;
    [ObservableProperty] private int _agi = 1;
    [ObservableProperty] private int _vit = 1;
    [ObservableProperty] private int _intel = 1;
    [ObservableProperty] private int _dex = 1;
    [ObservableProperty] private int _luk = 1;
    [ObservableProperty] private int _weaponAtk = 0;
    // manual enchant/card extras
    [ObservableProperty] private int _enchStr;
    [ObservableProperty] private int _enchAgi;
    [ObservableProperty] private int _enchVit;
    [ObservableProperty] private int _enchInt;
    [ObservableProperty] private int _enchDex;
    [ObservableProperty] private int _enchLuk;

    [ObservableProperty] private string _selectedSlot = "Any";
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private EquipInfo? _selectedEquip;
    [ObservableProperty] private string _effect = "";

    public ObservableCollection<EquipInfo> SearchResults { get; } = new();
    public ObservableCollection<EquipInfo> Build { get; } = new();
    public ObservableCollection<string> Results { get; } = new();

    public CalculatorViewModel(Lazy<GameDatabase> db) => _db = db;

    partial void OnSelectedEquipChanged(EquipInfo? v) => Effect = v?.Effect ?? "";

    [RelayCommand] private void SearchEquip()
    {
        SearchResults.Clear();
        string slot = SelectedSlot == "Any" ? "" : SelectedSlot;
        foreach (var e in _db.Value.SearchEquips(Query.Trim(), slot)) SearchResults.Add(e);
    }

    [RelayCommand] private void AddToBuild() { if (SelectedEquip is not null) { Build.Add(SelectedEquip); Compute(); } }
    [RelayCommand] private void RemoveFromBuild(EquipInfo e) { Build.Remove(e); Compute(); }
    [RelayCommand] private void ClearBuild() { Build.Clear(); Compute(); }

    [RelayCommand] private void Compute()
    {
        var i = new CalcInput
        {
            BaseLevel = BaseLevel, Str = Str, Agi = Agi, Vit = Vit, Int = Intel, Dex = Dex, Luk = Luk,
            WeaponAtk = WeaponAtk, WeaponType = SelectedWeapon,
            AddStr = EnchStr, AddAgi = EnchAgi, AddVit = EnchVit, AddInt = EnchInt, AddDex = EnchDex, AddLuk = EnchLuk
        };
        foreach (var e in Build) Aggregate(i, e.Bonuses);
        Results.Clear();
        foreach (var kv in StatCalculator.Compute(i)) Results.Add($"{kv.Key,-12} {kv.Value}");
    }

    private static void Aggregate(CalcInput i, Dictionary<string, int> b)
    {
        int G(string k) => b.TryGetValue(k, out var v) ? v : 0;
        i.AddStr += G("Str"); i.AddAgi += G("Agi"); i.AddVit += G("Vit");
        i.AddInt += G("Int"); i.AddDex += G("Dex"); i.AddLuk += G("Luk");
        i.AddAtk += G("Atk"); i.AddMatk += G("Matk"); i.AddDef += G("Def"); i.AddMdef += G("Mdef");
        i.AddHit += G("Hit"); i.AddFlee += G("Flee"); i.AddCrit += G("Crit");
        i.AddAspdRate += G("AspdRate"); i.AddMaxHP += G("MaxHP"); i.AddMaxSP += G("MaxSP");
    }
}
