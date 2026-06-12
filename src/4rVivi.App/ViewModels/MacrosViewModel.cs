using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using FourRVivi.Core.Macros;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

public sealed partial class MacrosViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly EngineHub _hub;
    private readonly SettingsStore _settings;
    private readonly MacroPlayer _player = new();
    private readonly Credentials _creds = new();

    public ObservableCollection<MacroRowViewModel> Chains { get; } = new();

    [ObservableProperty] private string _sequence = "ENTER, ENTER";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private int _gapMs = 120;
    [ObservableProperty] private string _status = "Chain macros: equip/skill switch, auto-vend, auto-storage. Loop runs while master is ON.";

    public MacrosViewModel(GameSession session, EngineHub hub, SettingsStore settings)
    {
        _session = session; _hub = hub; _settings = settings;

        // share the same ChainMacro instances between settings + engine
        _hub.Macros.Macros.Clear();
        if (_settings.Current.Macros.Count == 0)
            _settings.Current.Macros.Add(new ChainMacro { Name = "Open Storage", Steps = { new ChainStep { Key = "F9" } } });
        foreach (var m in _settings.Current.Macros)
        {
            _hub.Macros.Macros.Add(m);
            Chains.Add(new MacroRowViewModel(m, Persist));
        }
    }

    private void Persist() => _settings.Save();

    [RelayCommand] private void AddChain()
    {
        var m = new ChainMacro { Name = "New macro", Steps = { new ChainStep { Key = "F1" } } };
        _settings.Current.Macros.Add(m); _hub.Macros.Macros.Add(m);
        Chains.Add(new MacroRowViewModel(m, Persist)); Persist();
    }
    [RelayCommand] private async Task PlayChain(MacroRowViewModel row)
    {
        if (_session.WindowHandle == IntPtr.Zero) { Status = "Attach to your RO process first."; return; }
        Status = $"Playing '{row.Name}'…"; await _hub.Macros.PlayAsync(row.Model); Status = $"Played '{row.Name}'.";
    }
    [RelayCommand] private void RemoveChain(MacroRowViewModel row)
    {
        _settings.Current.Macros.Remove(row.Model); _hub.Macros.Macros.Remove(row.Model);
        Chains.Remove(row); Persist();
    }

    [RelayCommand] private void SaveLogin() { _creds.Set(Username, Password); Status = "Login stored DPAPI-encrypted."; }

    [RelayCommand] private async Task PlayLogin()
    {
        if (_session.WindowHandle == IntPtr.Zero) { Status = "Attach to your RO process first."; return; }
        var rec = new MacroRecording { Name = "login" };
        foreach (var part in Sequence.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int vk = KeyName.ToVk(part);
            if (vk != 0) rec.Steps.Add(new MacroStep { Vk = vk, GapMs = GapMs });
        }
        Status = $"Playing {rec.Steps.Count} steps…";
        await _player.PlayAsync(_session.WindowHandle, rec);
        Status = "Login macro finished.";
    }
}
