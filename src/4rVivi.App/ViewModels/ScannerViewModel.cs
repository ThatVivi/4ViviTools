using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Game;
using FourRVivi.Core.Memory;
using FourRVivi.Core.Settings;
using CoreRoles = FourRVivi.Core.Game.Roles;

namespace FourRVivi.App.ViewModels;

public sealed partial class ScannerViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly SettingsStore _settings;
    private MemoryScanner? _scanner;
    private List<ScanHit> _current = new();

    public string[] Types { get; } = { "Int32", "Int16", "Float" };
    public ObservableCollection<ScanHit> Results { get; } = new();

    [ObservableProperty] private string _selectedType = "Int32";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _status = "Pick your process in the top bar, type your current value, then First scan.";
    [ObservableProperty] private bool _canRefine;
    [ObservableProperty] private ScanHit? _selected;
    [ObservableProperty] private string _selectedRole = "BaseEXP";
    public string[] RoleList { get; } = CoreRoles.All;

    public ScannerViewModel(GameSession session, SettingsStore settings) { _session = session; _settings = settings; }

    private ScanType T() => SelectedType switch { "Int16" => ScanType.Int16, "Float" => ScanType.Float, _ => ScanType.Int32 };

    [RelayCommand] private void FirstScan()
    {
        if (!_session.Reader.Attached) { Status = "Not attached. Pick your RO process in the top bar."; return; }
        try
        {
            _scanner = new MemoryScanner(_session.Reader);
            _current = _scanner.FirstScan(T(), MemoryScanner.ParseValue(T(), Value));
            Publish();
            var d = _scanner.LastDiagnostics;
            if (_current.Count == 0)
                Status = d.RegionsRead == 0
                    ? "0 found: couldn't read memory. Run as Administrator and pick the game window."
                    : $"0 found in {d.RegionsRead} regions ({d.BytesRead / (1024 * 1024)} MB). Check the value/type and scan again.";
            else
                Status = $"{_current.Count} candidates ({d.BytesRead / (1024 * 1024)} MB). Change HP/SP in-game, type the new value, Next scan.";
            CanRefine = _current.Count > 0;
        }
        catch (FormatException) { Status = "Type a valid number first."; }
        catch (Exception ex) { Status = "Scan error: " + ex.Message; }
    }

    private void Refine(ScanFilter f)
    {
        if (_scanner is null) return;
        object? exact = f == ScanFilter.Exact ? SafeParse() : null;
        _current = _scanner.NextScan(_current, T(), f, exact);
        Publish();
        Status = $"{_current.Count} candidates left.";
    }

    private object? SafeParse() { try { return MemoryScanner.ParseValue(T(), Value); } catch { return null; } }

    [RelayCommand] private void NextExact() => Refine(ScanFilter.Exact);
    [RelayCommand] private void Decreased() => Refine(ScanFilter.Decreased);
    [RelayCommand] private void Increased() => Refine(ScanFilter.Increased);
    [RelayCommand] private void Changed() => Refine(ScanFilter.Changed);
    [RelayCommand] private void Unchanged() => Refine(ScanFilter.Unchanged);
    [RelayCommand] private void Reset() { _scanner = null; _current = new(); Results.Clear(); CanRefine = false; Status = "Scan reset."; }

    [RelayCommand] private void UseAsHp() => Save("HP");
    [RelayCommand] private void UseAsMaxHp() => Save("MaxHP");
    [RelayCommand] private void UseAsSp() => Save("SP");
    [RelayCommand] private void UseAsMaxSp() => Save("MaxSP");
    [RelayCommand] private void UseAsRole() => Save(SelectedRole);

    private void Save(string role)
    {
        if (Selected is null) { Status = "Select an address row first."; return; }
        long addr = (long)Selected.Address;
        int? off = null;
        var mb = (long)_session.Reader.ModuleBase;
        if (mb != 0 && _session.Reader.ModuleSize > 0)
        { long d = addr - mb; if (d >= 0 && d < _session.Reader.ModuleSize) off = (int)d; }

        var prof = _settings.Current.GetActiveProfile();
        prof.Addresses.Set(role, new SavedAddress { Runtime = addr, ModuleOffset = off, Type = SelectedType });
        _session.UseProfile(prof.Name, prof.Addresses);
        _settings.Save();
        Status = off is int o ? $"{role} saved as module+0x{o:X} (stable)." : $"{role} saved at 0x{addr:X}.";
    }

    private void Publish()
    {
        Results.Clear();
        foreach (var h in _current.Take(2000)) Results.Add(h);
    }
}
