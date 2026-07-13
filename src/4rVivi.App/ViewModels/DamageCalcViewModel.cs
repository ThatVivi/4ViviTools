using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Calc;
using FourRVivi.Core.Data;

namespace FourRVivi.App.ViewModels;

/// <summary>One editable card/enchant row bound in the UI.</summary>
public sealed partial class GearBonusRow : ObservableObject
{
    [ObservableProperty] private string _name = "Card / Enchant";
    [ObservableProperty] private int _str, _agi, _vit, _intt, _dex, _luk;
    [ObservableProperty] private int _flatAtk, _flatMatk;
    [ObservableProperty] private double _atkPercent, _racePercent, _sizePercent, _elementPercent, _skillPercent;

    public GearBonus ToBonus() => new()
    {
        Name = Name, Str = Str, Agi = Agi, Vit = Vit, Int = Intt, Dex = Dex, Luk = Luk,
        FlatAtk = FlatAtk, FlatMatk = FlatMatk,
        AtkPercent = AtkPercent / 100.0, RacePercent = RacePercent / 100.0,
        SizePercent = SizePercent / 100.0, ElementPercent = ElementPercent / 100.0,
        SkillPercent = SkillPercent / 100.0,
    };
}

/// <summary>Engine-backed RO damage calculator: classic / renewal / 4th, class-group filter,
/// cards+enchants, element/size/race, skill multiplier, damage + focus advice.</summary>
public sealed partial class DamageCalcViewModel : ViewModelBase
{
    private readonly DamageCalculator _calc = new();
    private readonly Lazy<GameDatabase> _db;

    public string[] Modes { get; } = { "Renewal", "Classic", "Fourth" };
    public Array Elements { get; } = Enum.GetValues(typeof(Element));
    public Array Sizes { get; } = Enum.GetValues(typeof(Size));
    public Array Races { get; } = Enum.GetValues(typeof(Race));
    public Array WeaponClasses { get; } = Enum.GetValues(typeof(WeaponClass));

    [ObservableProperty] private string _mode = "Renewal";
    public bool IsFourth => Mode == "Fourth";

    // Class group filter
    [ObservableProperty] private bool _showNormal = true;
    [ObservableProperty] private bool _showBaby;
    [ObservableProperty] private bool _showExtended;
    public ObservableCollection<string> Classes { get; } = new();
    [ObservableProperty] private string? _selectedClass;

    // Stats
    [ObservableProperty] private int _baseLevel = 99;
    [ObservableProperty] private int _str = 1, _agi = 1, _vit = 1, _intt = 1, _dex = 1, _luk = 1;
    [ObservableProperty] private int _pow, _sta, _wis, _spl, _con, _crt;

    // Weapon
    [ObservableProperty] private int _weaponDamage = 100;
    [ObservableProperty] private int _weaponLevel = 4;
    [ObservableProperty] private int _refine;
    [ObservableProperty] private int _weaponMatk;
    [ObservableProperty] private Element _weaponElement = Element.Neutral;
    [ObservableProperty] private WeaponClass _weaponClass = WeaponClass.Melee;

    // Target
    [ObservableProperty] private Element _targetElement = Element.Neutral;
    [ObservableProperty] private int _targetElementLevel = 1;
    [ObservableProperty] private Size _targetSize = Size.Medium;
    [ObservableProperty] private Race _targetRace = Race.Formless;
    [ObservableProperty] private int _hardDef, _softDef, _hardMdef, _softMdef;

    // Skill
    [ObservableProperty] private string _skillName = "Basic Attack";
    [ObservableProperty] private bool _skillMagic;
    [ObservableProperty] private double _skillMultiplier = 1.0;
    [ObservableProperty] private int _skillHits = 1;
    [ObservableProperty] private bool _isCritical;
    [ObservableProperty] private double _critDamageBonus;

    public ObservableCollection<GearBonusRow> Gear { get; } = new();
    public ObservableCollection<GearBonusRow> Buffs { get; } = new();
    public ObservableCollection<string> SkillNames { get; } = new();
    public ObservableCollection<string> GearNameChoices { get; } = new();
    public ObservableCollection<string> BuffNameChoices { get; } = new();

    [ObservableProperty] private string _resultText = "Set your build, then Calculate.";
    [ObservableProperty] private string _adviceText = "";

    public DamageCalcViewModel() : this(new Lazy<GameDatabase>(() => new GameDatabase())) { }

    public DamageCalcViewModel(Lazy<GameDatabase> db)
    {
        _db = db;
        LoadPickerNames();
        RebuildClasses();
    }

    private void LoadPickerNames()
    {
        try
        {
            static void Fill(ObservableCollection<string> target, IEnumerable<string> names)
            {
                target.Clear();
                foreach (var n in names.Where(n => !string.IsNullOrWhiteSpace(n))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(n => n))
                    target.Add(n);
            }

            var db = _db.Value;
            Fill(SkillNames, new[] { "Basic Attack" }.Concat(db.SkillDisplayNames()));
            Fill(GearNameChoices, db.ItemNamesByType("Weapon", "Armor", "ShadowGear", "Shadowgear", "Card").Concat(db.EnchantNames()));
            Fill(BuffNameChoices, db.SkillDisplayNames().Concat(db.ItemNamesByType("Usable", "DelayConsume", "Healing")));
        }
        catch
        {
            SkillNames.Clear();
            SkillNames.Add("Basic Attack");
        }
    }

    partial void OnModeChanged(string value) { OnPropertyChanged(nameof(IsFourth)); }
    partial void OnShowNormalChanged(bool value) => RebuildClasses();
    partial void OnShowBabyChanged(bool value) => RebuildClasses();
    partial void OnShowExtendedChanged(bool value) => RebuildClasses();

    private void RebuildClasses()
    {
        Classes.Clear();
        foreach (var c in ClassCatalog.Filter(ShowNormal, ShowBaby, ShowExtended)) Classes.Add(c);
        if (SelectedClass == null || !Classes.Contains(SelectedClass))
            SelectedClass = Classes.FirstOrDefault();
    }

    [RelayCommand] private void AddGear() => Gear.Add(new GearBonusRow());
    [RelayCommand] private void RemoveGear(GearBonusRow? row) { if (row != null) Gear.Remove(row); }
    [RelayCommand] private void AddBuff() => Buffs.Add(new GearBonusRow { Name = "Buff" });
    [RelayCommand] private void RemoveBuff(GearBonusRow? row) { if (row != null) Buffs.Remove(row); }

    [RelayCommand]
    private void Calculate()
    {
        _calc.Mode = Mode switch { "Classic" => CalcMode.Classic, "Fourth" => CalcMode.Fourth, _ => CalcMode.Renewal };
        _calc.IsCritical = IsCritical;
        _calc.CritDamageBonus = CritDamageBonus / 100.0;

        var st = new StatBlock { BaseLevel = BaseLevel, Str = Str, Agi = Agi, Vit = Vit, Int = Intt, Dex = Dex, Luk = Luk,
                                 Pow = Pow, Sta = Sta, Wis = Wis, Spl = Spl, Con = Con, Crt = Crt };
        var w = new Weapon { BaseDamage = WeaponDamage, Level = WeaponLevel, Refine = Refine, FlatMatk = WeaponMatk,
                             Element = WeaponElement, Class = WeaponClass };
        var t = new Target { Element = TargetElement, ElementLevel = TargetElementLevel, Size = TargetSize, Race = TargetRace,
                             HardDef = HardDef, SoftDef = SoftDef, HardMdef = HardMdef, SoftMdef = SoftMdef };
        var skill = new SkillProfile { Name = SkillName, Magic = SkillMagic, Hits = Math.Max(1, SkillHits),
                                       RenewalMultiplier = SkillMultiplier, ClassicMultiplier = SkillMultiplier };
        var gear = Gear.Select(g => g.ToBonus()).Concat(Buffs.Select(b => b.ToBonus())).ToList();

        var r = _calc.Calculate(st, w, t, gear, skill);
        ResultText = $"Damage:  {r.Min:N0} – {r.Max:N0}   (avg {r.Avg:N0})\n{r.Breakdown}";
        AdviceText = _calc.Advise(st, w, t, gear, skill);
    }

    public void CalculateExternal(
        CalcMode mode,
        StatBlock st,
        Weapon w,
        Target t,
        IReadOnlyList<GearBonus> gear,
        SkillProfile skill,
        bool isCritical,
        double critDamageBonusPercent)
    {
        _calc.Mode = mode;
        _calc.IsCritical = isCritical;
        _calc.CritDamageBonus = critDamageBonusPercent / 100.0;

        var r = _calc.Calculate(st, w, t, gear, skill);
        ResultText = $"Damage:  {r.Min:N0} - {r.Max:N0}   (avg {r.Avg:N0})\n{r.Breakdown}";
        AdviceText = _calc.Advise(st, w, t, gear, skill);
    }
}
