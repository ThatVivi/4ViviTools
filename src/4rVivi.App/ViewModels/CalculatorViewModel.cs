using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Data;
using FourRVivi.Core.Tools;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class CalculatorViewModel : ViewModelBase
{
    private readonly Lazy<GameDatabase> _db;

    /// <summary>Class dropdown, filtered by the Normal/Baby/Extended checkboxes (includes 4th classes).</summary>
    public ObservableCollection<string> Classes { get; } = new();
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

    // ===================== ROratorio-style layout fields =====================
    // (Layout pass: faithful structure; engine accuracy is the follow-up pass.)
    public string[] ElementOptions { get; } = { "Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Shadow", "Ghost", "Undead" };
    public string[] RaceOptions { get; } = { "Formless", "Undead", "Brute", "Plant", "Insect", "Fish", "Demon", "Demi-Human", "Angel", "Dragon" };
    public string[] SizeOptions { get; } = { "Small", "Medium", "Large" };
    public string[] TypeOptions { get; } = { "Normal", "Boss" };
    public string[] EnvOptions { get; } = { "PvM / PvP", "PvM", "PvP", "WoE" };
    public string[] DelayOptions { get; } = { "0.2s (5 per second)", "0.5s", "1s", "No delay" };
    public string[] AttributeOptions { get; } = { "(unchanged)", "Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Shadow", "Ghost", "Undead" };
    public string[] PlaceOptions { get; } = { "All Regions" };

    // ---- Enemy / target ----
    [ObservableProperty] private string _enemyName = "[Custom Player]";
    [ObservableProperty] private int _enemyLevel = 1;
    [ObservableProperty] private int _enemyMaxHp = 1;
    [ObservableProperty] private int _enemyAgi = 1, _enemyVit = 1, _enemyInt = 1, _enemyDex = 1, _enemyLuk = 76;
    [ObservableProperty] private int _enemyDef = 1, _enemyMdef = 1;
    [ObservableProperty] private string _enemyElement = "Neutral";
    [ObservableProperty] private int _enemyElementLevel = 1;
    [ObservableProperty] private string _enemyRace = "Demi-Human";
    [ObservableProperty] private string _enemySize = "Medium";
    [ObservableProperty] private string _enemyType = "Normal";

    // ---- Player readouts (filled by Compute) ----
    [ObservableProperty] private string _hitRate = "—";
    [ObservableProperty] private string _attackElementText = "Neutral (100% vs Neutral)";
    [ObservableProperty] private string _weaponSizeMod = "100%";
    [ObservableProperty] private string _critRange = "1~1";
    [ObservableProperty] private string _minDamage = "—";
    [ObservableProperty] private string _maxDamage = "—";
    [ObservableProperty] private string _dps = "—";
    [ObservableProperty] private string _bestElementText = "—";

    // ---- Calc mode (each has different formulas — engine pass implements the deltas) ----
    public string[] CalcModes { get; } = { "Classic", "Reborn", "Renewal (Lv175)", "Renewal (Lv185)", "4th Class" };
    [ObservableProperty] private string _calcMode = "Renewal (Lv185)";
    [ObservableProperty] private bool _grpNormal = true;
    [ObservableProperty] private bool _grpBaby;
    [ObservableProperty] private bool _grpExtended;

    // ---- Character ----
    [ObservableProperty] private int _jobLevel = 1;
    [ObservableProperty] private bool _adopted;
    [ObservableProperty] private string _bodyElement = "Neutral";
    public int RemainingPoints => System.Math.Max(0, StatBudget() - StatCost());

    // right-side character readouts
    [ObservableProperty] private string _charHp = "—", _charSp = "—", _charAspd = "—", _charHit = "—", _charFlee = "—", _charCrit = "—", _charDef = "—", _charMdef = "—";

    // ---- Equipment slots (faithful grid). Card slots are strings for the layout pass. ----
    [ObservableProperty] private string _eqWeapon = "(no weapon)";
    [ObservableProperty] private int _eqRefine;
    [ObservableProperty] private string _eqAttribute = "(unchanged)";
    [ObservableProperty] private string _eqWeaponCard1 = "(no card)", _eqWeaponCard2 = "(no card)", _eqWeaponCard3 = "(no card)", _eqWeaponCard4 = "(no card)";
    [ObservableProperty] private string _eqUpper = "(no upper headgear)", _eqUpperCard = "(no card)";
    [ObservableProperty] private string _eqMiddle = "(no middle headgear)", _eqMiddleCard = "(no card)";
    [ObservableProperty] private string _eqLower = "(no lower headgear)", _eqLowerCard = "(no card)";
    [ObservableProperty] private string _eqArmor = "(no armor)", _eqArmorCard = "(no card)";
    [ObservableProperty] private string _eqShield = "(no shield)", _eqShieldCard = "(no card)";
    [ObservableProperty] private string _eqGarment = "(no garment)", _eqGarmentCard = "(no card)";
    [ObservableProperty] private string _eqFootgear = "(no footgear)", _eqFootgearCard = "(no card)";
    [ObservableProperty] private string _eqAccLeft = "(no left accessory)", _eqAccLeftCard = "(no card)";
    [ObservableProperty] private string _eqAccRight = "(no right accessory)", _eqAccRightCard = "(no card)";

    // ---- Damaging skill ----
    [ObservableProperty] private string _skillName = "Basic Attack";
    [ObservableProperty] private double _skillMultiplier = 1.0;
    [ObservableProperty] private int _skillHits = 1;
    [ObservableProperty] private bool _skillMagic;
    [ObservableProperty] private int _skillLevel = 1;
    [ObservableProperty] private int _skillMaxLevel = 1;
    private FourRVivi.Core.Data.SkillInfo? _currentSkill;
    // Critical
    [ObservableProperty] private bool _isCritical;
    [ObservableProperty] private int _critDamageBonus;   // +% on top of the 140% renewal crit base

    // ---- Searchable dropdown sources (top = search box, list = items for that slot) ----
    public ObservableCollection<string> MobNames { get; } = new();
    public ObservableCollection<string> SkillNames { get; } = new();
    public ObservableCollection<string> ClassSkillNames { get; } = new();   // offensive skills for the selected class
    [ObservableProperty] private string _skillInfoText = "";                 // element / hits / how-to-increase note
    public ObservableCollection<string> WeaponList { get; } = new();
    public ObservableCollection<string> HeadgearList { get; } = new();
    public ObservableCollection<string> ArmorList { get; } = new();
    public ObservableCollection<string> ShieldList { get; } = new();
    public ObservableCollection<string> GarmentList { get; } = new();
    public ObservableCollection<string> FootgearList { get; } = new();
    public ObservableCollection<string> AccessoryList { get; } = new();
    public ObservableCollection<string> CardList { get; } = new();
    public ObservableCollection<string> EnchantList { get; } = new();    // random options / enchants
    [ObservableProperty] private string _eqEnchant1 = "(no option)";
    [ObservableProperty] private string _eqEnchant2 = "(no option)";
    [ObservableProperty] private string _eqEnchant3 = "(no option)";
    [ObservableProperty] private string _eqEnchant4 = "(no option)";
    public ObservableCollection<string> BuffItemList { get; } = new();   // consumables/foods that grant buffs
    public ObservableCollection<string> PickedBuffs { get; } = new();    // user-chosen buffs (items + skills)
    [ObservableProperty] private string _buffItemPick = "";
    [ObservableProperty] private string _buffSkillPick = "";

    [RelayCommand] private void AddBuffItem() { if (!string.IsNullOrWhiteSpace(BuffItemPick)) { PickedBuffs.Add("Item: " + BuffItemPick); BuffItemPick = ""; } }
    [RelayCommand] private void AddBuffSkill() { if (!string.IsNullOrWhiteSpace(BuffSkillPick)) { PickedBuffs.Add("Skill: " + BuffSkillPick); BuffSkillPick = ""; } }
    [RelayCommand] private void RemoveBuff(string b) { PickedBuffs.Remove(b); }
    [RelayCommand] private void ClearBuffs() { PickedBuffs.Clear(); }

    [RelayCommand]
    private void OpenEnemy()
    {
        var mob = _db.Value.SearchMobs(EnemyName, 10).FirstOrDefault(m => string.Equals(m.Name, EnemyName, StringComparison.OrdinalIgnoreCase));
        if (mob != null) DivinePrideLinks.OpenMonster(mob.Id);
    }

    [RelayCommand]
    private void OpenSkill()
    {
        var skill = _db.Value.SkillByName(SkillName);
        if (skill != null) DivinePrideLinks.OpenSkill(skill.Id);
    }

    [RelayCommand]
    private void OpenWeapon()
    {
        var equip = _db.Value.EquipByName(EqWeapon);
        if (equip != null) DivinePrideLinks.OpenItem(equip.Id);
    }

    [RelayCommand]
    private void OpenBuffItem()
    {
        int id = _db.Value.IconId(BuffItemPick);
        if (id > 0) DivinePrideLinks.OpenItem(id);
    }

    [ObservableProperty] private string _pickerInfo = "";

    private void LoadPickers()
    {
        try
        {
            var db = _db.Value;
            void Fill(ObservableCollection<string> c, IEnumerable<string> names)
            { c.Clear(); foreach (var n in names) c.Add(n); }

            static List<string> Norm(IEnumerable<string> s) =>
                s.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList();

            // Prefer real per-slot equipment (item_db_equip via Locations); fall back to
            // item-type pools when the Equips array is empty (un-regenerated gamedata.json).
            List<string> Slot(string loc, params string[] fallbackTypes)
            {
                var n = Norm(db.SearchEquips("", loc, 9000).Select(e => e.Name));
                return n.Count > 0 ? n : db.ItemNamesByType(fallbackTypes);
            }

            var weapons = Norm(Slot("Right_Hand", "Weapon").Concat(Slot("Both_Hand", "Weapon")));
            var cards   = db.ItemNamesByType("Card");

            Fill(MobNames, Norm(db.AllMobs().Select(m => m.Name)));
            Fill(SkillNames, Norm(db.AllSkills().Select(s => s.Name)));
            Fill(WeaponList, weapons);
            Fill(HeadgearList, Norm(Slot("Head_Top", "Armor").Concat(Slot("Head_Mid", "Armor")).Concat(Slot("Head_Low", "Armor"))));
            Fill(ArmorList, Slot("Armor", "Armor"));
            Fill(ShieldList, Slot("Left_Hand", "Armor"));
            Fill(GarmentList, Slot("Garment", "Armor"));
            Fill(FootgearList, Slot("Shoes", "Armor"));
            Fill(AccessoryList, Norm(Slot("Both_Accessory", "Armor")
                .Concat(Slot("Right_Accessory")).Concat(Slot("Left_Accessory"))));
            Fill(CardList, cards);
            Fill(EnchantList, db.EnchantNames());
            Fill(BuffItemList, db.ItemNamesByType("Usable", "DelayConsume", "Healing"));

            PickerInfo = $"DB loaded — {MobNames.Count} monsters · {weapons.Count} weapons · {ArmorList.Count} armors · {cards.Count} cards · {SkillNames.Count} skills";
        }
        catch (Exception ex) { PickerInfo = "DB load failed: " + ex.Message; }
    }

    private int _wlvl = 4;   // weapon level of the picked weapon (affects refine bonus + variance)
    private bool _weaponRanged;   // ranged weapons use DEX (not STR) for StatusATK (rAthena status_base_atk)

    // Ranged weapon subtypes per rAthena weapon_type (pc.hpp): bow, instrument, whip, all guns.
    private static readonly System.Collections.Generic.HashSet<string> RangedSub =
        new(System.StringComparer.OrdinalIgnoreCase) { "Bow", "Musical", "Whip", "Revolver", "Rifle", "Shotgun", "Gatling", "Grenade" };

    /// <summary>Weapon picked → pull its ATK, weapon level and ranged/melee class into the engine.</summary>
    partial void OnEqWeaponChanged(string value)
    {
        try
        {
            var e = _db.Value.EquipByName(value);
            if (e != null)
            {
                if (e.Atk > 0) WeaponAtk = e.Atk;
                if (e.WeaponLevel is >= 1 and <= 5) _wlvl = e.WeaponLevel;
                _weaponRanged = RangedSub.Contains(e.SubType ?? "");
            }
        }
        catch { }
    }

    // Live recompute: any input change refreshes the readout immediately (no Recalculate click needed).
    private static readonly System.Collections.Generic.HashSet<string> RecalcOn = new()
    {
        nameof(Str), nameof(Agi), nameof(Vit), nameof(Intel), nameof(Dex), nameof(Luk), nameof(BaseLevel),
        nameof(EnchStr), nameof(EnchAgi), nameof(EnchVit), nameof(EnchInt), nameof(EnchDex), nameof(EnchLuk),
        nameof(SelectedWeapon), nameof(WeaponAtk), nameof(EqWeapon), nameof(EqRefine), nameof(EqAttribute),
        nameof(EnemyElement), nameof(EnemyElementLevel), nameof(EnemyDef), nameof(EnemyMdef), nameof(EnemyLevel),
        nameof(EnemyAgi), nameof(EnemySize), nameof(EnemyRace), nameof(BodyElement),
        nameof(SkillName), nameof(SkillMultiplier), nameof(SkillHits), nameof(SkillMagic), nameof(CalcMode),
        nameof(IsCritical), nameof(CritDamageBonus), nameof(SkillLevel),
        nameof(EqUpper), nameof(EqMiddle), nameof(EqLower), nameof(EqArmor), nameof(EqShield),
        nameof(EqGarment), nameof(EqFootgear), nameof(EqAccLeft), nameof(EqAccRight),
        nameof(EqWeaponCard1), nameof(EqWeaponCard2), nameof(EqWeaponCard3), nameof(EqWeaponCard4),
        nameof(EqUpperCard), nameof(EqMiddleCard), nameof(EqLowerCard), nameof(EqArmorCard), nameof(EqShieldCard),
        nameof(EqGarmentCard), nameof(EqFootgearCard), nameof(EqAccLeftCard), nameof(EqAccRightCard),
        nameof(EqEnchant1), nameof(EqEnchant2), nameof(EqEnchant3), nameof(EqEnchant4),
    };
    private bool _recalcBusy;
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_recalcBusy || e.PropertyName == null || !RecalcOn.Contains(e.PropertyName)) return;
        _recalcBusy = true;
        try { Compute(); } catch { } finally { _recalcBusy = false; }
    }

    /// <summary>Enemy picked from the dropdown → auto-fill its known stats from the database.</summary>
    partial void OnEnemyNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            var mob = _db.Value.SearchMobs(value, 5).FirstOrDefault(m => string.Equals(m.Name, value, StringComparison.OrdinalIgnoreCase));
            if (mob is null) return;
            if (mob.Level > 0) EnemyLevel = mob.Level;
            if (mob.Hp > 0) EnemyMaxHp = (int)System.Math.Min(int.MaxValue, mob.Hp);
            EnemyDef = mob.Def; EnemyMdef = mob.Mdef;
            if (mob.Agi > 0) EnemyAgi = mob.Agi;
            if (mob.Vit > 0) EnemyVit = mob.Vit;
            if (mob.Int > 0) EnemyInt = mob.Int;
            if (mob.Dex > 0) EnemyDex = mob.Dex;
            if (mob.Luk > 0) EnemyLuk = mob.Luk;
            var elTok = mob.Element.Split(' ', '_', '/');
            var parsed = FourRVivi.Core.Calc.Elements.TryParse(elTok.Length > 0 ? elTok[0] : null);
            if (parsed is { } p && ElementOptions.Contains(p.ToString())) EnemyElement = p.ToString();
            if (mob.ElementLevel is >= 1 and <= 4) EnemyElementLevel = mob.ElementLevel;
            else if (elTok.Length > 1 && int.TryParse(elTok[^1], out var lvl) && lvl is >= 1 and <= 4) EnemyElementLevel = lvl;
            var race = mob.Race.Replace("DemiHuman", "Demi-Human");
            if (RaceOptions.Contains(race)) EnemyRace = race;
            if (SizeOptions.Contains(mob.Size)) EnemySize = mob.Size;
        }
        catch { }
    }

    // ---- Combat simulator ----
    [ObservableProperty] private int _numEnemies = 1;
    [ObservableProperty] private string _minSkillDelay = "0.2s (5 per second)";
    [ObservableProperty] private int _setTimeMs;
    [ObservableProperty] private int _pingMs;
    [ObservableProperty] private string _environment = "PvM / PvP";

    private int StatBudget()
    {
        // Renewal-ish status point budget by base level (approx; engine pass refines this).
        int lv = BaseLevel;
        return lv <= 1 ? 48 : 48 + (lv - 1) * 5;
    }
    private int StatCost()
    {
        int Cost(int s) { int c = 0; for (int v = 2; v <= s; v++) c += (v - 1) / 10 + 2; return c; }
        return Cost(Str + EnchStr) + Cost(Agi + EnchAgi) + Cost(Vit + EnchVit)
             + Cost(Intel + EnchInt) + Cost(Dex + EnchDex) + Cost(Luk + EnchLuk);
    }

    partial void OnStrChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));
    partial void OnAgiChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));
    partial void OnVitChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));
    partial void OnIntelChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));
    partial void OnDexChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));
    partial void OnLukChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));
    partial void OnBaseLevelChanged(int value) => OnPropertyChanged(nameof(RemainingPoints));

    public ObservableCollection<EquipInfo> SearchResults { get; } = new();
    public ObservableCollection<EquipInfo> Build { get; } = new();
    public ObservableCollection<string> Results { get; } = new();

    /// <summary>The engine-backed damage calculator, hosted on the same screen (second tab).</summary>
    public DamageCalcViewModel Damage { get; }

    public CalculatorViewModel(Lazy<GameDatabase> db, DamageCalcViewModel damage)
    {
        _db = db;
        Damage = damage;
        RebuildClasses();
        LoadPickers();
        RebuildClassSkills();
        RebuildWeaponList();
        try { Compute(); } catch { }
    }

    private void RebuildClasses()
    {
        var prev = SelectedClass;
        Classes.Clear();
        foreach (var c in FourRVivi.Core.Calc.ClassCatalog.Filter(GrpNormal, GrpBaby, GrpExtended)) Classes.Add(c);
        SelectedClass = (prev != null && Classes.Contains(prev)) ? prev : Classes.FirstOrDefault() ?? "Novice";
    }

    partial void OnGrpNormalChanged(bool value) => RebuildClasses();
    partial void OnGrpBabyChanged(bool value) => RebuildClasses();
    partial void OnGrpExtendedChanged(bool value) => RebuildClasses();

    /// <summary>Selected class → its offensive skills (client skill tree); falls back to all skills.</summary>
    private void RebuildClassSkills()
    {
        ClassSkillNames.Clear();
        ClassSkillNames.Add("Basic Attack");                       // always available
        foreach (var n in _db.Value.SkillsForClass(SelectedClass ?? "")) ClassSkillNames.Add(n);
        // if the skill currently selected isn't in this class, reset to Basic Attack
        if (!string.IsNullOrEmpty(SkillName) && !ClassSkillNames.Contains(SkillName)) SkillName = "Basic Attack";
    }
    partial void OnSelectedClassChanged(string value) { RebuildClassSkills(); RebuildWeaponList(); }

    /// <summary>Weapon dropdown filtered to what the selected class can equip (item_db Jobs mask).
    /// Falls back to all weapons if the filter would be empty.</summary>
    private void RebuildWeaponList()
    {
        try
        {
            var db = _db.Value;
            var all = db.SearchEquips("", "Right_Hand", 9000).Concat(db.SearchEquips("", "Both_Hand", 9000)).ToList();
            var usable = all.Where(e => FourRVivi.Core.Calc.ClassEquip.CanEquip(e.Jobs, SelectedClass))
                            .Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
            if (usable.Count == 0) usable = all.Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
            WeaponList.Clear();
            foreach (var n in usable) WeaponList.Add(n);
        }
        catch { }
    }

    /// <summary>Skill picked → auto-fill hits/magic and show element + how-to-increase note.</summary>
    partial void OnSkillNameChanged(string value)
    {
        var s = _db.Value.SkillByName(value);
        _currentSkill = s;
        if (s == null) { SkillInfoText = ""; SkillMaxLevel = 1; return; }
        SkillMaxLevel = s.MaxLevel;
        if (SkillLevel > SkillMaxLevel) SkillLevel = SkillMaxLevel;
        if (s.Hits > 1) SkillHits = s.Hits;
        SkillMultiplier = s.MultAt(SkillLevel);                 // auto: rAthena ratio at the chosen level
        SkillMagic = s.Magic;
        string elem = string.Equals(s.Element, "Weapon", System.StringComparison.OrdinalIgnoreCase) ? "weapon/ammo/endow element" : s.Element;
        SkillInfoText = $"{(string.IsNullOrEmpty(s.Type) ? "Skill" : s.Type)} · Lv {SkillLevel}/{SkillMaxLevel} · {System.Math.Max(1, s.Hits)} hit(s) · element: {elem}.  Increase via " +
            (s.Magic ? "MATK, INT/SPL, +MATK% and vs-element/race% cards."
                     : "ATK, STR (DEX if ranged), refine, +Race/Size/Element% cards, crit & crit-dmg.");
    }

    partial void OnSkillLevelChanged(int value)
    {
        if (_currentSkill != null) SkillMultiplier = _currentSkill.MultAt(value);   // recompute triggers via SkillMultiplier in RecalcOn
    }

    /// <summary>Push the planned build (stats + weapon + summed gear bonuses) into the damage
    /// calculator and run it, so "build planner → engine → damage + advice" is one flow.</summary>
    [RelayCommand]
    private void SyncToDamage()
    {
        var atk = FourRVivi.Core.Calc.Elements.TryParse(EqAttribute)
                  ?? FourRVivi.Core.Calc.Elements.TryParse(BodyElement)
                  ?? FourRVivi.Core.Calc.Element.Neutral;
        var def = FourRVivi.Core.Calc.Elements.TryParse(EnemyElement) ?? FourRVivi.Core.Calc.Element.Neutral;
        var weapon = _db.Value.EquipByName(EqWeapon);
        var skillInfo = _db.Value.SkillByName(SkillName);
        var mode = CalcMode.StartsWith("Classic") || CalcMode.StartsWith("Reborn")
            ? FourRVivi.Core.Calc.CalcMode.Classic
            : CalcMode.StartsWith("4th")
                ? FourRVivi.Core.Calc.CalcMode.Fourth
                : FourRVivi.Core.Calc.CalcMode.Renewal;

        var st = new FourRVivi.Core.Calc.StatBlock
        {
            BaseLevel = BaseLevel,
            Str = Str + EnchStr,
            Agi = Agi + EnchAgi,
            Vit = Vit + EnchVit,
            Int = Intel + EnchInt,
            Dex = Dex + EnchDex,
            Luk = Luk + EnchLuk,
        };
        var w = new FourRVivi.Core.Calc.Weapon
        {
            BaseDamage = WeaponAtk,
            Level = _wlvl,
            Refine = EqRefine,
            FlatMatk = weapon?.Matk ?? 0,
            Element = atk,
            Class = _weaponRanged ? FourRVivi.Core.Calc.WeaponClass.Ranged : FourRVivi.Core.Calc.WeaponClass.Melee,
        };
        var t = new FourRVivi.Core.Calc.Target
        {
            Element = def,
            ElementLevel = EnemyElementLevel,
            Size = System.Enum.TryParse<FourRVivi.Core.Calc.Size>(EnemySize, true, out var sz) ? sz : FourRVivi.Core.Calc.Size.Medium,
            Race = ParseRace(EnemyRace),
            HardDef = EnemyDef,
            HardMdef = EnemyMdef,
            IsBoss = string.Equals(EnemyType, "Boss", StringComparison.OrdinalIgnoreCase),
        };
        var skill = new FourRVivi.Core.Calc.SkillProfile
        {
            Name = string.IsNullOrWhiteSpace(SkillName) ? "Basic Attack" : SkillName,
            Magic = SkillMagic,
            Hits = Math.Max(1, SkillHits),
            RenewalMultiplier = SkillMultiplier,
            ClassicMultiplier = SkillMultiplier,
            ForcedElement = skillInfo is { Element: { Length: > 0 } se } && !string.Equals(se, "Weapon", StringComparison.OrdinalIgnoreCase)
                ? FourRVivi.Core.Calc.Elements.TryParse(se)
                : null,
        };

        Damage.CalculateExternal(mode, st, w, t, BuildGear(), skill, IsCritical, CritDamageBonus);
    }

    partial void OnSelectedEquipChanged(EquipInfo? value) => Effect = value?.Effect ?? "";

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
        var stats = StatCalculator.Compute(i);
        foreach (var kv in stats) Results.Add($"{kv.Key,-12} {kv.Value}");

        // Character readout — computed from rAthena renewal sub-stat formulas (see docs/rathena/stats-substats.md).
        double lv = BaseLevel;
        double tStr = Str + EnchStr, tAgi = Agi + EnchAgi, tVit = Vit + EnchVit,
               tInt = Intel + EnchInt, tDex = Dex + EnchDex, tLuk = Luk + EnchLuk;
        CharHit = System.Math.Floor(lv + tDex + tLuk / 3.0).ToString("0");
        CharFlee = System.Math.Floor(lv + tAgi + tLuk / 5.0).ToString("0");
        CharCrit = (1 + tLuk * 0.3).ToString("0.#");
        CharDef = System.Math.Floor((lv + tVit) / 2.0 + tAgi / 5.0).ToString("0");
        CharMdef = System.Math.Floor(tInt + lv / 4.0 + (tDex + tVit) / 5.0).ToString("0");
        // HP/SP/ASPD are job-table dependent — shown as estimates (accurate tables are a follow-up).
        CharHp = (35.0 * lv * (1 + tVit / 100.0)).ToString("N0");
        CharSp = (10.0 * lv * (1 + tInt / 100.0)).ToString("N0");
        CharAspd = System.Math.Min(193, 150 + (tAgi + tDex / 4.0) / 8.0).ToString("0");

        ComputeDamageReadout(i);
    }

    /// <summary>Runs the damage engine against the enemy block and fills the ROratorio readout strings.</summary>
    private void ComputeDamageReadout(CalcInput i)
    {
        var def = FourRVivi.Core.Calc.Elements.TryParse(EnemyElement) ?? FourRVivi.Core.Calc.Element.Neutral;
        // attack element: weapon Attribute (endow) wins, else body/forged element, else Neutral
        var atk = FourRVivi.Core.Calc.Elements.TryParse(EqAttribute)
                  ?? FourRVivi.Core.Calc.Elements.TryParse(BodyElement)
                  ?? FourRVivi.Core.Calc.Element.Neutral;
        double mod = FourRVivi.Core.Calc.Elements.Modifier(atk, def, EnemyElementLevel);
        AttackElementText = $"{atk} ({mod * 100:0}% vs {EnemyElement})";
        var best = FourRVivi.Core.Calc.Elements.BestAttackElement(def, EnemyElementLevel);
        BestElementText = $"Best: {best} ({FourRVivi.Core.Calc.Elements.Modifier(best, def, EnemyElementLevel) * 100:0}%)";

        var st = new FourRVivi.Core.Calc.StatBlock
        {
            BaseLevel = BaseLevel, Str = i.Str + i.AddStr, Agi = i.Agi + i.AddAgi, Vit = i.Vit + i.AddVit,
            Int = i.Int + i.AddInt, Dex = i.Dex + i.AddDex, Luk = i.Luk + i.AddLuk
        };
        // Aggregate every selected gear / card / enchant's parsed script bonuses.
        var gear = BuildGear();
        int flatAtkFromGear = gear.Sum(g => g.FlatAtk);

        var w = new FourRVivi.Core.Calc.Weapon { BaseDamage = WeaponAtk + i.AddAtk + flatAtkFromGear, Level = _wlvl, Refine = EqRefine, Element = atk,
                                                 Class = _weaponRanged ? FourRVivi.Core.Calc.WeaponClass.Ranged : FourRVivi.Core.Calc.WeaponClass.Melee };
        var tgt = new FourRVivi.Core.Calc.Target
        {
            Element = def, ElementLevel = EnemyElementLevel,
            Size = System.Enum.TryParse<FourRVivi.Core.Calc.Size>(EnemySize, out var sz) ? sz : FourRVivi.Core.Calc.Size.Medium,
            Race = ParseRace(EnemyRace), HardDef = EnemyDef, HardMdef = EnemyMdef
        };
        var skill = new FourRVivi.Core.Calc.SkillProfile
        {
            Name = string.IsNullOrWhiteSpace(SkillName) ? "Basic Attack" : SkillName,
            Magic = SkillMagic, Hits = System.Math.Max(1, SkillHits),
            RenewalMultiplier = SkillMultiplier, ClassicMultiplier = SkillMultiplier
        };
        var engine = new FourRVivi.Core.Calc.DamageCalculator
        {
            // Reborn/Renewal-175/185 deltas are refined in the engine-accuracy pass.
            Mode = CalcMode.StartsWith("Classic") || CalcMode.StartsWith("Reborn")
                ? FourRVivi.Core.Calc.CalcMode.Classic
                : CalcMode.StartsWith("4th") ? FourRVivi.Core.Calc.CalcMode.Fourth
                : FourRVivi.Core.Calc.CalcMode.Renewal,
            IsCritical = IsCritical,
            CritDamageBonus = CritDamageBonus / 100.0,
        };
        var r = engine.Calculate(st, w, tgt, gear, skill);
        MinDamage = r.Min.ToString("N0"); MaxDamage = r.Max.ToString("N0");
        Dps = r.Avg.ToString("N0");
        HitRate = $"{System.Math.Clamp(100 + (st.Dex + BaseLevel) - (EnemyLevel + EnemyAgi), 5, 100)}%";

        // Crit rate (renewal): base 1 + LUK*0.3, +bonus; vs enemy LUK reduces it slightly.
        double luk = i.Luk + i.AddLuk;
        double critRate = System.Math.Max(0, 1 + luk * 0.3 - EnemyLuk * 0.2);
        CritRange = IsCritical ? "ON" : $"{critRate:0.#}%";
    }

    /// <summary>Collect parsed script-bonuses from every equipped slot, card and enchant into a
    /// GearBonus list the engine applies (stats fold into ATK; race/size/element % apply vs the target).</summary>
    private System.Collections.Generic.List<FourRVivi.Core.Calc.GearBonus> BuildGear()
    {
        var list = new System.Collections.Generic.List<FourRVivi.Core.Calc.GearBonus>();
        var db = _db.Value;
        var worn = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        void Wear(string? name) { if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("(no", System.StringComparison.OrdinalIgnoreCase)) worn.Add(name); }
        void AddEquip(string name) { Wear(name); var e = db.EquipByName(name); if (e?.Mods != null) foreach (var m in e.Mods) list.Add(ToGear(m)); }
        void AddCard(string name) { Wear(name); var c = db.CardByName(name); if (c?.Mods != null) foreach (var m in c.Mods) list.Add(ToGear(m)); }
        void AddEnch(string name) { var en = db.EnchantByName(name); if (en?.Mods != null) foreach (var m in en.Mods) list.Add(ToGear(m)); }

        AddEquip(EqWeapon); AddEquip(EqUpper); AddEquip(EqMiddle); AddEquip(EqLower); AddEquip(EqArmor);
        AddEquip(EqShield); AddEquip(EqGarment); AddEquip(EqFootgear); AddEquip(EqAccLeft); AddEquip(EqAccRight);
        AddCard(EqWeaponCard1); AddCard(EqWeaponCard2); AddCard(EqWeaponCard3); AddCard(EqWeaponCard4);
        AddCard(EqUpperCard); AddCard(EqMiddleCard); AddCard(EqLowerCard); AddCard(EqArmorCard); AddCard(EqShieldCard);
        AddCard(EqGarmentCard); AddCard(EqFootgearCard); AddCard(EqAccLeftCard); AddCard(EqAccRightCard);
        AddEnch(EqEnchant1); AddEnch(EqEnchant2); AddEnch(EqEnchant3); AddEnch(EqEnchant4);
        foreach (var buff in PickedBuffs)
        {
            const string itemPrefix = "Item: ";
            if (!buff.StartsWith(itemPrefix, System.StringComparison.OrdinalIgnoreCase)) continue;
            AddEquip(buff.Substring(itemPrefix.Length));
        }

        // Item combos: if every item of any one set is worn, apply the combo's bonuses ("group of gears").
        foreach (var combo in db.AllCombos())
            if (combo.Sets.Any(set => set.Count > 0 && set.All(worn.Contains)))
                foreach (var m in combo.Mods) list.Add(ToGear(m));

        return list;
    }

    /// <summary>Fold one gear/card/enchant's parsed flat bonuses into the calc input (keys match
    /// the item bonus columns: Str/Agi/.../Atk/Matk/Def/Mdef/Hit/Flee/Crit/Aspd/MaxHP/MaxSP).</summary>
    private static void Aggregate(FourRVivi.Core.Tools.CalcInput i, System.Collections.Generic.Dictionary<string, int> b)
    {
        if (b == null) return;
        int G(string k) => b.TryGetValue(k, out var v) ? v : 0;
        i.AddStr += G("Str"); i.AddAgi += G("Agi"); i.AddVit += G("Vit");
        i.AddInt += G("Int"); i.AddDex += G("Dex"); i.AddLuk += G("Luk");
        i.AddAtk += G("Atk"); i.AddMatk += G("Matk");
        i.AddDef += G("Def"); i.AddMdef += G("Mdef");
        i.AddHit += G("Hit"); i.AddFlee += G("Flee"); i.AddCrit += G("Crit");
        i.AddAspdRate += G("Aspd"); i.AddMaxHP += G("MaxHP"); i.AddMaxSP += G("MaxSP");
    }

    private static FourRVivi.Core.Calc.GearBonus ToGear(FourRVivi.Core.Data.ModEntry m)
    {
        FourRVivi.Core.Calc.Race? rt = (m.Race == null || m.Race == "All") ? null
            : System.Enum.TryParse<FourRVivi.Core.Calc.Race>(m.Race, true, out var r) ? r : null;
        FourRVivi.Core.Calc.Size? zt = (m.Size == null || m.Size == "All") ? null
            : System.Enum.TryParse<FourRVivi.Core.Calc.Size>(m.Size, true, out var z) ? z : null;
        FourRVivi.Core.Calc.Element? et = (m.Ele == null || m.Ele == "All") ? null
            : FourRVivi.Core.Calc.Elements.TryParse(m.Ele);
        return new FourRVivi.Core.Calc.GearBonus
        {
            Str = m.Str, Agi = m.Agi, Vit = m.Vit, Int = m.Int, Dex = m.Dex, Luk = m.Luk,
            FlatAtk = m.Atk, FlatMatk = m.Matk,
            AtkPercent = m.AtkPct / 100.0,
            RacePercent = m.RacePct / 100.0, RaceTarget = rt,
            SizePercent = m.SizePct / 100.0, SizeTarget = zt,
            ElementPercent = m.ElePct / 100.0, ElementTarget = et,
        };
    }

    public string EstimateKillTime(MobInfo mob, SkillInfo? skillInfo, int skillLevel, int delayMs)
    {
        if (mob == null || mob.Hp <= 0)
            return "No HP data";

        var def = FourRVivi.Core.Calc.Elements.TryParse(mob.Element) ?? FourRVivi.Core.Calc.Element.Neutral;
        var atk = FourRVivi.Core.Calc.Elements.TryParse(EqAttribute)
                  ?? FourRVivi.Core.Calc.Elements.TryParse(BodyElement)
                  ?? FourRVivi.Core.Calc.Element.Neutral;
        var st = new FourRVivi.Core.Calc.StatBlock
        {
            BaseLevel = BaseLevel,
            Str = Str + EnchStr,
            Agi = Agi + EnchAgi,
            Vit = Vit + EnchVit,
            Int = Intel + EnchInt,
            Dex = Dex + EnchDex,
            Luk = Luk + EnchLuk
        };
        var gear = BuildGear();
        int flatAtkFromGear = gear.Sum(g => g.FlatAtk);
        int weaponMatk = 0;
        try { weaponMatk = _db.Value.EquipByName(EqWeapon)?.Matk ?? 0; } catch { }
        var w = new FourRVivi.Core.Calc.Weapon
        {
            BaseDamage = WeaponAtk + flatAtkFromGear,
            Level = _wlvl,
            Refine = EqRefine,
            FlatMatk = weaponMatk,
            Element = atk,
            Class = _weaponRanged ? FourRVivi.Core.Calc.WeaponClass.Ranged : FourRVivi.Core.Calc.WeaponClass.Melee
        };
        var tgt = new FourRVivi.Core.Calc.Target
        {
            Element = def,
            ElementLevel = mob.ElementLevel is >= 1 and <= 4 ? mob.ElementLevel : 1,
            Size = System.Enum.TryParse<FourRVivi.Core.Calc.Size>(mob.Size, true, out var sz) ? sz : FourRVivi.Core.Calc.Size.Medium,
            Race = ParseRace(mob.Race),
            HardDef = mob.Def,
            HardMdef = mob.Mdef,
            IsBoss = mob.Mvp
        };
        var skill = new FourRVivi.Core.Calc.SkillProfile
        {
            Name = skillInfo?.Name ?? "Basic Attack",
            Magic = skillInfo?.Magic == true,
            Hits = Math.Max(1, skillInfo?.Hits ?? 1),
            RenewalMultiplier = skillInfo == null ? 1.0 : skillInfo.MultAt(skillLevel),
            ClassicMultiplier = skillInfo == null ? 1.0 : skillInfo.MultAt(skillLevel),
            ForcedElement = skillInfo is { Element: { Length: > 0 } se } && !string.Equals(se, "Weapon", StringComparison.OrdinalIgnoreCase)
                ? FourRVivi.Core.Calc.Elements.TryParse(se)
                : null
        };
        var engine = new FourRVivi.Core.Calc.DamageCalculator
        {
            Mode = CalcMode.StartsWith("Classic") || CalcMode.StartsWith("Reborn")
                ? FourRVivi.Core.Calc.CalcMode.Classic
                : CalcMode.StartsWith("4th") ? FourRVivi.Core.Calc.CalcMode.Fourth
                : FourRVivi.Core.Calc.CalcMode.Renewal,
            IsCritical = IsCritical,
            CritDamageBonus = CritDamageBonus / 100.0,
        };
        var r = engine.Calculate(st, w, tgt, gear, skill);
        if (r.Avg <= 0)
            return $"HP {mob.Hp:N0}; damage too low";

        int casts = (int)Math.Ceiling(mob.Hp / Math.Max(1.0, r.Avg));
        double sec = casts * Math.Max(80, delayMs) / 1000.0;
        return $"{r.Avg:N0}/hit · {casts} cast(s) · ~{sec:0.0}s";
    }

    private static FourRVivi.Core.Calc.Race ParseRace(string s) =>
        System.Enum.TryParse<FourRVivi.Core.Calc.Race>(s.Replace("-", "").Replace(" ", ""), true, out var r)
            ? r : FourRVivi.Core.Calc.Race.Formless;
}
