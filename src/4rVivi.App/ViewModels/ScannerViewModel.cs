using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Game;
using FourRVivi.Core.Memory;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;
using CoreRoles = FourRVivi.Core.Game.Roles;

namespace FourRVivi.App.ViewModels;

public sealed partial class ScanRow : ObservableObject
{
    public long Address { get; set; }
    public string AddressHex => "0x" + Address.ToString("X8");
    public string Type { get; set; } = "Int32";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _role = "";
}

public sealed partial class ScannerViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly SettingsStore _settings;
    private readonly OcrService _ocr;
    private Dictionary<string,int> _ocrValues = new();
    private MemoryScanner? _scanner;
    private List<ScanHit> _current = new();

    public string[] Types { get; } = Enum.GetNames<ScanType>();
    public string[] RoleList { get; } = CoreRoles.All;

    public ObservableCollection<ScanRow> Found { get; } = new();   // left table
    public ObservableCollection<ScanRow> Saved { get; } = new();   // right table (ArtMoney-style)

    [ObservableProperty] private string _selectedType = "Int32";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _currentHp = "";
    [ObservableProperty] private string _selectedRole = "HP";
    [ObservableProperty] private ScanRow? _selectedFound;
    [ObservableProperty] private ScanRow? _selectedSaved;
    [ObservableProperty] private bool _canRefine;
    [ObservableProperty] private string _status = "Pick your process in the top bar. Then use Auto-setup, or scan manually.";
    [ObservableProperty] private string _tip = TipFor("Int32");

    public ObservableCollection<string> Detected { get; } = new();

    public ScannerViewModel(GameSession session, SettingsStore settings, OcrService ocr)
    {
        _session = session; _settings = settings; _ocr = ocr;
        // hydrate saved table from the active profile's address book
        foreach (var kv in settings.Current.GetActiveProfile().Addresses.Entries)
            Saved.Add(new ScanRow { Address = kv.Value.Runtime, Type = kv.Value.Type, Role = kv.Key, Description = kv.Key });
    }

    partial void OnSelectedTypeChanged(string value) => Tip = TipFor(value);

    private static string TipFor(string t) => t switch
    {
        "String" => "Name → String. Type your exact character name and scan.",
        "Float" or "Double" => "Some clients store HP/SP as Float. Try Int32 first.",
        _ => "HP, MaxHP, SP, MaxSP, EXP, Zeny, Weight, levels → Integer (Int32). Position → Int16/Int32."
    };

    private ScanType T() => Enum.Parse<ScanType>(SelectedType);

    // ---- Auto-setup: find Name (string) + HP (int) quickly ----
    [RelayCommand] private void AutoSetup()
    {
        if (!_session.Reader.Attached) _session.Reattach();
        if (!_session.Reader.Attached) { Status = "Not attached. Pick your RO process in the top bar first."; return; }
        _scanner = new MemoryScanner(_session.Reader);
        Found.Clear();

        if (!string.IsNullOrWhiteSpace(CharacterName))
        {
            var nameHits = _scanner.FirstScan(ScanType.String, CharacterName);
            foreach (var h in nameHits.Take(50)) Add(Found, h, "String", "name?");
            if (nameHits.Count is > 0 and <= 3)
            { SaveRole("Name", nameHits[0].Address, "String"); }
        }
        if (int.TryParse(CurrentHp, out int hp))
        {
            _current = _scanner.FirstScan(ScanType.Int32, hp);
            foreach (var h in _current.Take(500)) Add(Found, h, "Int32", "hp?");
            CanRefine = _current.Count > 0;
        }
        Status = $"Auto-setup: {Found.Count} candidates. Name auto-saved if unique. " +
                 "For HP: change it in-game, set the new value, hit Next, then move the survivor to Saved and assign role HP.";
    }

    // ---- manual scan ----
    [RelayCommand] private void FirstScan()
    {
        if (!_session.Reader.Attached) _session.Reattach();
        if (!_session.Reader.Attached) { Status = "Not attached. Pick your RO process in the top bar first."; return; }
        try
        {
            _scanner = new MemoryScanner(_session.Reader);
            _current = _scanner.FirstScan(T(), MemoryScanner.ParseValue(T(), Value));
            PublishFound();
            var d = _scanner.LastDiagnostics;
            Status = _current.Count == 0
                ? (d.RegionsRead == 0 ? "0 found: run as Administrator and pick the game window."
                                      : $"0 found in {d.BytesRead / (1024 * 1024)} MB. Check value/type.")
                : $"{_current.Count} candidates ({d.BytesRead / (1024 * 1024)} MB, {d.ElapsedMs} ms). Change the value, set it, Next.";
            CanRefine = _current.Count > 0;
        }
        catch (FormatException) { Status = "Type a valid value for the chosen type."; }
        catch (Exception ex) { Status = "Scan error: " + ex.Message; }
    }

    private void Refine(ScanFilter f)
    {
        if (_scanner is null) return;
        object? exact = f == ScanFilter.Exact ? (object?)SafeParse() : null;
        _current = _scanner.NextScan(_current, T(), f, exact);
        PublishFound();
        Status = $"{_current.Count} candidates left.";
    }
    private object? SafeParse() { try { return MemoryScanner.ParseValue(T(), Value); } catch { return null; } }

    [RelayCommand] private void NextExact() => Refine(ScanFilter.Exact);
    [RelayCommand] private void Decreased() => Refine(ScanFilter.Decreased);
    [RelayCommand] private void Increased() => Refine(ScanFilter.Increased);
    [RelayCommand] private void Changed() => Refine(ScanFilter.Changed);
    [RelayCommand] private void Unchanged() => Refine(ScanFilter.Unchanged);
    [RelayCommand] private void Reset() { _scanner = null; _current = new(); Found.Clear(); CanRefine = false; Status = "Scan reset."; }

    // ---- two-table moves (ArtMoney-style) ----
    [RelayCommand] private void MoveToSaved()
    {
        if (SelectedFound is null) { Status = "Select a row on the left first."; return; }
        var row = new ScanRow { Address = SelectedFound.Address, Type = SelectedFound.Type, Value = SelectedFound.Value, Description = SelectedFound.Description };
        Saved.Add(row);
        SelectedSaved = row;
        Status = "Moved to saved list (selected). Pick a role and Apply to use it in the bot/autopot.";
    }
    [RelayCommand] private void RemoveSaved() => RemoveSavedRow(SelectedSaved);

    [RelayCommand] private void RemoveSavedRow(ScanRow? row)
    {
        if (row is null) return;
        Saved.Remove(row);
        if (!string.IsNullOrEmpty(row.Role))
        {
            var prof = _settings.Current.GetActiveProfile();
            prof.Addresses.Entries.Remove(row.Role);
            _session.UseProfile(prof.Name, prof.Addresses);
            _settings.Save();
        }
        Status = "Removed from saved list.";
    }

    [RelayCommand] private void Reattach()
    {
        var r = _session.Reattach();
        Status = r.Ok ? "Re-attached. Now scan." : r.Error!;
    }

    [RelayCommand] private async Task ReadScreen()
    {
        if (_session.WindowHandle == IntPtr.Zero) { Status = "Attach to your RO process first."; return; }
        Status = "Reading the game's Basic Info box (downloading OCR data on first run)…";
        if (!await _ocr.EnsureDataAsync()) { Status = "Could not get OCR data (no internet?)."; return; }
        try
        {
            string text = await Task.Run(() => _ocr.Read(_session.WindowHandle, 0, 0, 0, 0));
            _ocrValues = _ocr.Parse(text);
            Detected.Clear();
            foreach (var kv in _ocrValues) Detected.Add($"{kv.Key} = {kv.Value}");
            if (_ocrValues.TryGetValue("HP", out int hp)) { Value = hp.ToString(); SelectedType = "Int32"; }
            Status = _ocrValues.Count > 0
                ? $"Read {_ocrValues.Count} values. HP pre-filled — hit First scan, change HP in-game, then Next to pin the address."
                : "OCR found no values. Move the Basic Info box to the top-left, or set a region.";
        }
        catch (Exception ex) { Status = "OCR error: " + ex.Message; }
    }

    [RelayCommand] private void UseDetected(string entry)
    {
        var key = entry.Split('=')[0].Trim();
        if (_ocrValues.TryGetValue(key, out int v)) { Value = v.ToString(); SelectedType = "Int32"; SelectedRole = key; Status = $"Value set to {key}={v}. First scan, then refine."; }
    }

    [RelayCommand] private void ApplyRole()
    {
        if (SelectedSaved is null) { Status = "Select a saved row first."; return; }
        SaveRole(SelectedRole, (IntPtr)SelectedSaved.Address, SelectedSaved.Type);
        SelectedSaved.Role = SelectedRole; SelectedSaved.Description = SelectedRole;
        Status = $"{SelectedRole} assigned (0x{SelectedSaved.Address:X}).";
    }

    private void SaveRole(string role, IntPtr addr, string type)
    {
        long a = (long)addr; int? off = null;
        var mb = (long)_session.Reader.ModuleBase;
        if (mb != 0 && _session.Reader.ModuleSize > 0) { long d = a - mb; if (d >= 0 && d < _session.Reader.ModuleSize) off = (int)d; }
        var prof = _settings.Current.GetActiveProfile();
        prof.Addresses.Set(role, new SavedAddress { Runtime = a, ModuleOffset = off, Type = type });
        _session.UseProfile(prof.Name, prof.Addresses);
        _settings.Save();
        foreach (var dup in Saved.Where(r => r.Role == role && r.Address != a).ToList()) Saved.Remove(dup);
        if (!Saved.Any(r => r.Address == a && r.Role == role))
            Saved.Add(new ScanRow { Address = a, Type = type, Role = role, Description = role });
    }

    private void Add(ObservableCollection<ScanRow> list, ScanHit h, string type, string desc)
        => list.Add(new ScanRow { Address = (long)h.Address, Type = type, Value = h.Display, Description = desc });

    private void PublishFound()
    {
        Found.Clear();
        foreach (var h in _current.Take(2000)) Add(Found, h, SelectedType, "");
    }
}
