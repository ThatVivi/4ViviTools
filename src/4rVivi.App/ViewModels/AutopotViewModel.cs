using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

public sealed partial class AutopotViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly SettingsStore _settings;
    public ObservableCollection<PotRowViewModel> Pots { get; } = new();

    public AutopotViewModel(EngineHub hub, SettingsStore settings)
    {
        _hub = hub; _settings = settings;

        var prof = settings.Current.GetActiveProfile();
        if (prof.Pots.Count == 0)
            prof.Pots.Add(new PotConfig { Enabled = true, Key = "F1", Percent = 50, UseSp = false });

        // engine + UI share the same PotConfig instances
        _hub.Autopot.Rules.Clear();
        foreach (var c in prof.Pots) { _hub.Autopot.Rules.Add(c); Pots.Add(new PotRowViewModel(c, Save)); }
    }

    [RelayCommand] private void AddPot()
    {
        var c = new PotConfig { Enabled = true, Key = "F2", Percent = 40 };
        _settings.Current.GetActiveProfile().Pots.Add(c);
        _hub.Autopot.Rules.Add(c);
        Pots.Add(new PotRowViewModel(c, Save));
        Save();
    }

    [RelayCommand] private void RemovePot(PotRowViewModel row)
    {
        _settings.Current.GetActiveProfile().Pots.Remove(row.Model);
        _hub.Autopot.Rules.Remove(row.Model);
        Pots.Remove(row);
        Save();
    }

    private void Save() => _settings.Save();
}
