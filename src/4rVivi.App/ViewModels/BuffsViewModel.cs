using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.App.ViewModels;

public sealed partial class BuffsViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly GameSession _session;
    private readonly KeySender _keys = new();
    public ObservableCollection<BuffRowViewModel> SkillBuffs { get; } = new();
    public ObservableCollection<BuffRowViewModel> ItemBuffs { get; } = new();

    public BuffsViewModel(EngineHub hub, GameSession session)
    {
        _hub = hub; _session = session;
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
}
