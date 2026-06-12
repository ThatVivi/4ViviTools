using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Settings;
using FourRVivi.Core.Trackers;

namespace FourRVivi.App.ViewModels;

public sealed partial class HudRowViewModel : ObservableObject
{
    public BuffTimer Model { get; }
    public HudRowViewModel(BuffTimer m) => Model = m;
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); } }
    public int DurationSec { get => Model.DurationSec; set { Model.DurationSec = value; OnPropertyChanged(); } }
    public string Display => Model.Display;
    public void Start() { Model.Start(); OnPropertyChanged(nameof(Display)); }
    public void Tick() => OnPropertyChanged(nameof(Display));
}

public sealed partial class HudViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    public ObservableCollection<HudRowViewModel> Timers { get; } = new();

    public HudViewModel(SettingsStore settings)
    {
        _settings = settings;
        foreach (var b in settings.Current.BuffTimers) Timers.Add(new HudRowViewModel(b));
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => { foreach (var r in Timers) r.Tick(); };
        t.Start();
    }

    [RelayCommand] private void Add()
    {
        var b = new BuffTimer { Name = "Buff", DurationSec = 120 };
        _settings.Current.BuffTimers.Add(b); Timers.Add(new HudRowViewModel(b)); _settings.Save();
    }
    [RelayCommand] private void StartTimer(HudRowViewModel row) => row.Start();
    [RelayCommand] private void Remove(HudRowViewModel row) { _settings.Current.BuffTimers.Remove(row.Model); Timers.Remove(row); _settings.Save(); }
    [RelayCommand] private void Save() => _settings.Save();
}
