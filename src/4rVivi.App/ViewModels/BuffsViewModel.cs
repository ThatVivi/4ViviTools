using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

public sealed partial class BuffsViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    public ObservableCollection<BuffRowViewModel> SkillBuffs { get; } = new();
    public ObservableCollection<BuffRowViewModel> ItemBuffs { get; } = new();

    public BuffsViewModel(EngineHub hub)
    {
        _hub = hub;
        foreach (var r in _hub.SkillBuffs.Rules) SkillBuffs.Add(new BuffRowViewModel(r));
        foreach (var r in _hub.ItemBuffs.Rules) ItemBuffs.Add(new BuffRowViewModel(r));
    }

    [RelayCommand] private void AddSkillBuff()
    {
        var r = new BuffRule { Key = "F5", IntervalMs = 30000 };
        _hub.SkillBuffs.Rules.Add(r); SkillBuffs.Add(new BuffRowViewModel(r));
    }
    [RelayCommand] private void RemoveSkillBuff(BuffRowViewModel row)
    { _hub.SkillBuffs.Rules.Remove(row.Model); SkillBuffs.Remove(row); }

    [RelayCommand] private void AddItemBuff()
    {
        var r = new BuffRule { Key = "F6", IntervalMs = 60000 };
        _hub.ItemBuffs.Rules.Add(r); ItemBuffs.Add(new BuffRowViewModel(r));
    }
    [RelayCommand] private void RemoveItemBuff(BuffRowViewModel row)
    { _hub.ItemBuffs.Rules.Remove(row.Model); ItemBuffs.Remove(row); }
}
