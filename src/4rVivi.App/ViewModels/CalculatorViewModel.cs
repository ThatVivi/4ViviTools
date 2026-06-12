using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Tools;

namespace FourRVivi.App.ViewModels;

public sealed partial class CalculatorViewModel : ViewModelBase
{
    [ObservableProperty] private int _baseLevel = 99;
    [ObservableProperty] private int _str = 1;
    [ObservableProperty] private int _agi = 1;
    [ObservableProperty] private int _vit = 1;
    [ObservableProperty] private int _intel = 1;
    [ObservableProperty] private int _dex = 1;
    [ObservableProperty] private int _luk = 1;
    [ObservableProperty] private int _weaponAtk = 0;
    public ObservableCollection<string> Results { get; } = new();

    [RelayCommand] private void Compute()
    {
        Results.Clear();
        var r = StatCalculator.Compute(new CalcInput(BaseLevel, Str, Agi, Vit, Intel, Dex, Luk, WeaponAtk));
        foreach (var kv in r) Results.Add($"{kv.Key,-12} {kv.Value}");
    }
}
