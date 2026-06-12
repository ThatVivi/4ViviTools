using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

public sealed partial class ServersViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _preferredProcessNames = "";
    [ObservableProperty] private string _status = "Profiles hold their own discovered addresses and pot rules.";

    public ServersViewModel(SettingsStore settings)
    {
        _settings = settings;
        foreach (var p in settings.Current.Profiles) Profiles.Add(p.Name);
        PreferredProcessNames = string.Join(", ", settings.Current.GetActiveProfile().PreferredProcessNames);
    }

    [RelayCommand] private void AddProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0 || _settings.Current.Profiles.Any(p => p.Name == name)) { Status = "Pick a unique profile name."; return; }
        _settings.Current.Profiles.Add(new ProfileConfig { Name = name });
        Profiles.Add(name); _settings.Save();
        Status = $"Profile '{name}' created.";
        NewProfileName = "";
    }

    [RelayCommand] private void SavePreferred()
    {
        var names = PreferredProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _settings.Current.GetActiveProfile().PreferredProcessNames = names;
        _settings.Save();
        Status = "Preferred process names saved.";
    }
}
