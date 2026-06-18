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
    [ObservableProperty] private long _valueEach;
    [ObservableProperty] private string _totals = "0 items";

    public LootViewModel(LootLog log)
    {
        _log = log;
        foreach (var r in log.Rows) Rows.Add(r);
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear(); foreach (var r in _log.Rows) Rows.Add(r);
        Totals = $"{_log.TotalCount:N0} items \u2022 {_log.TotalValue:N0}z total";
    }

    [RelayCommand] private void Add()
    {
        if (string.IsNullOrWhiteSpace(Item)) return;
        _log.Add(Item.Trim(), Qty, ValueEach);
        Item = ""; ValueEach = 0; Qty = 1;
        Refresh();
    }
    [RelayCommand] private void Clear() { _log.Clear(); Refresh(); }
}
