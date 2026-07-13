using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Data;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.App.ViewModels;

public sealed partial class BuffsViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly GameSession _session;
    private readonly Lazy<GameDatabase> _db;
    private readonly KeySender _keys = new();
    public ObservableCollection<BuffRowViewModel> SkillBuffs { get; } = new();
    public ObservableCollection<BuffRowViewModel> ItemBuffs { get; } = new();
    public ObservableCollection<string> SkillNames { get; } = new();
    public ObservableCollection<string> ItemNames { get; } = new();

        [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) { _hub.SkillBuffs.Enabled = value; _hub.ItemBuffs.Enabled = value; }

    public BuffsViewModel(EngineHub hub, GameSession session, Lazy<GameDatabase> db)
    {
        _hub = hub; _session = session; _db = db;
        LoadPickerNames();
        foreach (var r in _hub.SkillBuffs.Rules) { r.Name = SkillDisplayName(r.Name); SkillBuffs.Add(new BuffRowViewModel(r)); }
        foreach (var r in _hub.ItemBuffs.Rules) { r.Name = ItemDisplayName(r.Name); ItemBuffs.Add(new BuffRowViewModel(r)); }
    }

    [RelayCommand] private void AddSkillBuff()
    {
        var r = new BuffRule { Name = SkillNames.FirstOrDefault() ?? "", Key = "F5", IntervalMs = 30000 };
        _hub.SkillBuffs.Rules.Add(r); SkillBuffs.Add(new BuffRowViewModel(r));
    }
    [RelayCommand] private void RemoveSkillBuff(BuffRowViewModel row)
    { _hub.SkillBuffs.Rules.Remove(row.Model); SkillBuffs.Remove(row); }

    [RelayCommand] private void AddItemBuff()
    {
        var r = new BuffRule { Name = ItemNames.FirstOrDefault() ?? "", Key = "F6", IntervalMs = 60000 };
        _hub.ItemBuffs.Rules.Add(r); ItemBuffs.Add(new BuffRowViewModel(r));
    }
    [RelayCommand] private void RemoveItemBuff(BuffRowViewModel row)
    { _hub.ItemBuffs.Rules.Remove(row.Model); ItemBuffs.Remove(row); }

    /// <summary>One button: fire every enabled skill buff key in order.</summary>
    [RelayCommand] private async Task RunBuffSequence()
    {
        if (_session.WindowHandle == IntPtr.Zero) return;
        foreach (var b in SkillBuffs)
        {
            if (!b.Enabled || string.IsNullOrWhiteSpace(b.Key)) continue;
            _keys.Tap(_session.WindowHandle, KeyName.ToVk(b.Key), 20);
            await Task.Delay(300);
        }
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
            Fill(SkillNames, db.SkillDisplayNames());
            Fill(ItemNames, db.ItemNamesByType("Usable", "DelayConsume", "Healing"));
        }
        catch { }
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
}
