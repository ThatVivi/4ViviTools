using System.Collections.ObjectModel;
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
    private int _lastExp = -1;

    [ObservableProperty] private string _elapsed = "00:00:00";
    [ObservableProperty] private int _kills;
    [ObservableProperty] private string _expPerHour = "0";
    [ObservableProperty] private string _zenyPerHour = "0";
    [ObservableProperty] private string _expGained = "0";
    [ObservableProperty] private string _hpText = "—";
    [ObservableProperty] private string _spText = "—";
    [ObservableProperty] private string _weightText = "—";

    public ObservableCollection<string> ExpLog { get; } = new();

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
        ExpGained = _session.ExpGained.ToString("N0");

        int hp = _stat.Hp, maxHp = _stat.MaxHp, sp = _stat.Sp, maxSp = _stat.MaxSp, wt = _stat.Weight, maxWt = _stat.MaxWeight;
        HpText = hp < 0 ? "—" : (maxHp > 0 ? $"{hp}/{maxHp}" : hp.ToString());
        SpText = sp < 0 ? "—" : (maxSp > 0 ? $"{sp}/{maxSp}" : sp.ToString());
        WeightText = wt < 0 ? "—" : (maxWt > 0 ? $"{wt}/{maxWt}" : wt.ToString());

        // EXP gain log
        int exp = _stat.Exp;
        if (exp >= 0)
        {
            if (_lastExp >= 0 && exp > _lastExp)
                ExpLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  +{exp - _lastExp:N0}  (total {_session.ExpGained:N0})");
            if (ExpLog.Count > 300) ExpLog.RemoveAt(ExpLog.Count - 1);
            _lastExp = exp;
        }
    }

    [RelayCommand] private void Reset() { _session.Reset(); _lastExp = -1; ExpLog.Clear(); }
}
