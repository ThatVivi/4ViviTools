using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Game;
using FourRVivi.Core.Servers;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

/// <summary>Method 1 (proven, 4RTools model): match the client by name to a fixed-address profile and
/// bind HP/MaxHP/SP/MaxSP/Name directly — no scanning. Feeds the top bar, Stats tab and Discord.</summary>
public sealed partial class AutoDetectViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly ServerProfileDb _db;
    private readonly ServerBinder _binder;
    private readonly SettingsStore _settings;

    public ObservableCollection<ServerProfile> Servers { get; } = new();
    [ObservableProperty] private ServerProfile? _selectedServer;
    [ObservableProperty] private string _hpAddress = "";
    [ObservableProperty] private string _nameAddress = "";
    [ObservableProperty] private string _banner = "Attach your client — I'll try to auto-detect it.";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _hpText = "—";
    [ObservableProperty] private string _spText = "—";
    [ObservableProperty] private string _nameText = "—";
    [ObservableProperty] private bool _bound;

    public AutoDetectViewModel(GameSession session, ServerProfileDb db, ServerBinder binder, SettingsStore settings)
    {
        _session = session; _db = db; _binder = binder; _settings = settings;
        foreach (var p in db.All) Servers.Add(p);
        AutoDetect();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        t.Tick += (_, _) => ReadLive();
        t.Start();
    }

    [RelayCommand] private void AutoDetect() => Apply(_binder.TryResolve(_session, null));

    [RelayCommand] private void ApplySelected()
    {
        if (SelectedServer is null) { Status = "Pick a server from the list first."; return; }
        Apply(_binder.TryResolve(_session, SelectedServer));
    }

    [RelayCommand] private void ApplyManual()
    {
        if (ServerProfile.ParseHex(HpAddress) <= 0) { Status = "Enter a valid HP address like 0x010DCE10."; return; }
        var p = new ServerProfile
        {
            Name = _session.Reader.Target?.ProcessName ?? "manual",
            Description = "Manual",
            HpAddress = HpAddress,
            NameAddress = NameAddress,
        };
        Apply(_binder.TryResolve(_session, p));
    }

    private void Apply(ServerBindResult r)
    {
        Banner = r.Message;
        if (!r.Ok) { Bound = false; Status = r.Message; return; }

        var prof = _settings.Current.GetActiveProfile();
        foreach (var kv in r.Roles) prof.Addresses.Set(kv.Key, new SavedAddress { Runtime = kv.Value.addr, Type = kv.Value.type });
        _session.UseProfile(prof.Name, prof.Addresses);
        _settings.Save();
        Bound = true;
        Status = $"{r.ServerName} — bound to your profile. Top bar, Stats and Discord now read live values.";
        ReadLive();
    }

    private void ReadLive()
    {
        if (!_session.Reader.Attached) { HpText = SpText = NameText = "—"; return; }
        var h = _session.Health;
        HpText = h.Hp < 0 ? "—" : (h.MaxHp > 0 ? $"{h.Hp} / {h.MaxHp}" : h.Hp.ToString());
        SpText = h.Sp < 0 ? "—" : (h.MaxSp > 0 ? $"{h.Sp} / {h.MaxSp}" : h.Sp.ToString());
        string n = _session.ReadRoleString("CharName");
        NameText = string.IsNullOrWhiteSpace(n) ? "—" : n;
    }
}
