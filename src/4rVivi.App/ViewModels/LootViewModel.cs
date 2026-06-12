using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Trackers;

namespace FourRVivi.App.ViewModels;

public sealed partial class LootViewModel : ViewModelBase
{
    private readonly LootLog _log;
    public ObservableCollection<LootRow> Rows { get; } = new();
    [ObservableProperty] private string _item = "";
    [ObservableProperty] private int _qty = 1;

    public LootViewModel(LootLog log)
    {
        _log = log;
        foreach (var r in log.Rows) Rows.Add(r);
    }

    [RelayCommand] private void Add()
    {
        if (string.IsNullOrWhiteSpace(Item)) return;
        _log.Add(Item.Trim(), Qty);
        Rows.Clear(); foreach (var r in _log.Rows) Rows.Add(r);
        Item = "";
    }
    [RelayCommand] private void Clear() { _log.Clear(); Rows.Clear(); }
}
