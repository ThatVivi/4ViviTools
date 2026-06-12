using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using FourRVivi.Core.Trackers;

namespace FourRVivi.App.ViewModels;

public sealed partial class StatsViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly SessionTracker _session;
    private readonly StatReader _stat;

    [ObservableProperty] private string _elapsed = "00:00:00";
    [ObservableProperty] private int _kills;
    [ObservableProperty] private string _expPerHour = "0";
    [ObservableProperty] private string _zenyPerHour = "0";
    [ObservableProperty] private string _hpText = "—";
    [ObservableProperty] private string _spText = "—";
    [ObservableProperty] private string _weightText = "—";

    public StatsViewModel(EngineHub hub, SessionTracker session, GameSession gameSession)
    {
        _hub = hub; _session = session; _stat = new StatReader(gameSession);
        _session.Reset();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => Update();
        t.Start();
    }

    private void Update()
    {
        Elapsed = _session.Elapsed.ToString(@"hh\:mm\:ss");
        Kills = _hub.SmartBot.Kills;
        ExpPerHour = _session.ExpPerHour.ToString("N0");
        ZenyPerHour = _session.ZenyPerHour.ToString("N0");
        double hp = _stat.HpPercent, sp = _stat.SpPercent, wt = _stat.WeightPercent;
        HpText = hp < 0 ? "—" : $"{hp:0}%";
        SpText = sp < 0 ? "—" : $"{sp:0}%";
        WeightText = wt < 0 ? "—" : $"{wt:0}%";
    }

    [RelayCommand] private void Reset() => _session.Reset();
}
