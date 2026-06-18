using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Game;
using FourRVivi.Core.Memory;
using FourRVivi.Core.Signatures;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;
using System.Security.Principal;
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
    private readonly SignatureBinder _binder;
    private MemoryScanner? _scanner;
    private List<ScanHit> _current = new();

    public string[] Types { get; } = Enum.GetNames<ScanType>();
    public string[] RoleList { get; } = CoreRoles.All;

    public ObservableCollection<ScanRow> Found { get; } = new();   // left table
    public ObservableCollection<ScanRow> Compare { get; } = new(); // middle table (frozen snapshot)
    public ObservableCollection<ScanRow> Saved { get; } = new();   // right table (ArtMoney-style)

    [ObservableProperty] private string _selectedType = "Int32";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _currentHp = "";
    [ObservableProperty] private string _selectedRole = "HP";
    [ObservableProperty] private ScanRow? _selectedFound;
    [ObservableProperty] private ScanRow? _selectedSaved;
    [ObservableProperty] private ScanRow? _selectedCompare;
    [ObservableProperty] private bool _canRefine;
    [ObservableProperty] private string _status = "Pick your process in the top bar. Then use Auto-setup, or scan manually.";
    [ObservableProperty] private string _tip = TipFor("Int32");
    [ObservableProperty] private string _bindBanner = "Attach to auto-bind from a saved client profile.";
    [ObservableProperty] private string _adminStatus = IsElevated() ? "Admin: yes" : "Admin: NO \u2014 close and Run as administrator";

    private static bool IsElevated()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }




    // OCR region + language (doc: dynamic region selection + multi-language)

    public ScannerViewModel(GameSession session, SettingsStore settings, SignatureBinder binder)
    {
        _session = session; _settings = settings; _binder = binder;
        HydrateSaved();
        AutoBind();
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
                ? (d.RegionsRead == 0 ? (IsElevated() ? "0 found: couldn\u0027t read memory. Pick the RO CLIENT window (not the launcher)." : "0 found: not elevated. Run 4rVivi as administrator.")
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

    /// <summary>Snapshot-free diff: after a First scan (e.g. exact 91), change the value in-game
    /// (e.g. HP to 80) and press this. Every candidate whose value changed is listed in the
    /// Compare table as "old -> new", and the live set is narrowed to those. The real address is
    /// the row that went from your old value to your new value.</summary>
    [RelayCommand] private void WhatChanged()
    {
        if (!_session.Reader.Attached) { Status = "Not attached. Pick your RO process first."; return; }
        if (Found.Count == 0) { Status = "First scan a value (e.g. exact 91). Then change it in-game and press What changed."; return; }
        Compare.Clear();
        int changed = 0;
        foreach (var r in Found)
        {
            string now = ReadValueAt(r.Address, r.Type);
            if (now.Length > 0 && now != r.Value)
            {
                Compare.Add(new ScanRow { Address = r.Address, Type = r.Type, Value = now, Description = $"{r.Value} \u2192 {now}" });
                changed++;
            }
        }
        if (_scanner is not null) { _current = _scanner.NextScan(_current, T(), ScanFilter.Changed, null); PublishFound(); }
        Status = changed == 0
            ? "Nothing changed. Lower/raise the value in-game first, then press What changed."
            : $"{changed} address(es) changed \u2014 see Compare (old \u2192 new). The real one matches your old\u2192new value.";
    }

    private string ReadValueAt(long addr, string type)
    {
        var p = (IntPtr)addr; var rd = _session.Reader;
        try
        {
            return type switch
            {
                "Int16" => rd.ReadInt16(p).ToString(),
                "Int64" => rd.ReadInt64(p).ToString(),
                "Float" => rd.ReadFloat(p).ToString(),
                "Double" => rd.ReadDouble(p).ToString(),
                "String" => rd.ReadString(p, 24),
                _ => rd.ReadInt32(p).ToString(),
            };
        }
        catch { return ""; }
    }

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
    [RelayCommand] private void Snapshot()
    {
        Compare.Clear();
        foreach (var r in Found) Compare.Add(new ScanRow { Address = r.Address, Type = r.Type, Value = r.Value, Description = r.Description });
        Status = $"Snapshot: {Compare.Count} rows frozen in the middle table.";
    }
    [RelayCommand] private void MoveFoundToCompare()
    {
        if (SelectedFound is null) { Status = "Select a row in Found first."; return; }
        Compare.Add(new ScanRow { Address = SelectedFound.Address, Type = SelectedFound.Type, Value = SelectedFound.Value, Description = SelectedFound.Description });
    }
    [RelayCommand] private void CompareToSaved()
    {
        if (SelectedCompare is null) { Status = "Select a row in Compare first."; return; }
        var row = new ScanRow { Address = SelectedCompare.Address, Type = SelectedCompare.Type, Value = SelectedCompare.Value, Description = SelectedCompare.Description };
        Saved.Add(row); SelectedSaved = row;
        Status = "Moved to Saved (selected). Pick a role and Apply.";
    }
    [RelayCommand] private void RemoveCompare() { if (SelectedCompare is not null) Compare.Remove(SelectedCompare); }

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
        if (r.Ok) { AutoBind(); Status = "Re-attached. " + BindBanner; }
        else Status = r.Error!;
    }

    [RelayCommand] private async Task MakePermanent()
    {
        var row = SelectedSaved;
        if (row is null || string.IsNullOrEmpty(row.Role)) { Status = "Select a Saved row that has a role assigned, then Make permanent."; return; }
        if (!_session.Reader.Attached) { Status = "Not attached."; return; }
        Status = $"Pointer-scanning for a stable path to {row.Role}\u2026 this can take a moment.";
        var path = await Task.Run(() =>
        {
            var sc = new PointerScanner(_session.Reader);
            return sc.Find((IntPtr)row.Address, new PointerScanOptions()).FirstOrDefault();
        });
        if (path is null) { Status = $"No stable pointer found for {row.Role}. It still works this session; retry after a relaunch."; return; }
        _binder.SaveBinding(_session, row.Role, path, row.Type);
        BindBanner = $"{row.Role} pinned to a pointer \u2014 auto-binds next launch.";
        Status = $"Saved pointer for {row.Role}: {path}";
    }

    private void AutoBind()
    {
        if (!_session.Reader.Attached) { BindBanner = "Attach to auto-bind from a saved client profile."; return; }
        var r = _binder.TryAutoBind(_session);
        BindBanner = r.Message;
        if (r.Bound.Count > 0) HydrateSaved();
    }

    private void HydrateSaved()
    {
        Saved.Clear();
        foreach (var kv in _session.AddressBook.Entries)
            Saved.Add(new ScanRow { Address = (long)kv.Value.Resolve(_session.Reader.ModuleBase), Type = kv.Value.Type, Role = kv.Key, Description = kv.Key });
        if (Saved.Count == 0)
            foreach (var kv in _settings.Current.GetActiveProfile().Addresses.Entries)
                Saved.Add(new ScanRow { Address = kv.Value.Runtime, Type = kv.Value.Type, Role = kv.Key, Description = kv.Key });
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
