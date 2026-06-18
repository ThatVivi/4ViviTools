using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using FourRVivi.App.Overlay;
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
    public int Score { get; set; }
    public string ScoreText => Score > 0 ? Score + "%" : "";
}

public sealed partial class ScannerViewModel : ViewModelBase
{
    private readonly GameSession _session;
    private readonly SettingsStore _settings;
    private readonly SignatureBinder _binder;
    private MemoryScanner? _scanner;
    private List<ScanHit> _current = new();
    private readonly Dictionary<string, List<ScanHit>> _multi = new();
    private readonly Dictionary<string, int> _typed = new();
    private CaptureOverlayWindow? _captureOverlay;
    private DispatcherTimer? _captureTimer;
    private int _captureLeft;

    public string[] Types { get; } = Enum.GetNames<ScanType>();
    public string[] RoleList { get; } = CoreRoles.All;

    public ObservableCollection<ScanRow> Found { get; } = new();   // left table
    public ObservableCollection<ScanRow> Compare { get; } = new(); // middle table (frozen snapshot)
    public ObservableCollection<ScanRow> Saved { get; } = new();   // right table (ArtMoney-style)

    [ObservableProperty] private string _selectedType = "Int32";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _currentHp = "";
    [ObservableProperty] private string _inMaxHp = "";
    [ObservableProperty] private string _inSp = "";
    [ObservableProperty] private string _inMaxSp = "";
    [ObservableProperty] private string _inBaseLevel = "";
    [ObservableProperty] private string _inJobLevel = "";
    [ObservableProperty] private string _inWeight = "";
    [ObservableProperty] private string _inMaxWeight = "";
    [ObservableProperty] private string _inZeny = "";
    [ObservableProperty] private string _inBaseExp = "";
    [ObservableProperty] private string _inJobExp = "";
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

    // ---- Multi-value, change-based auto-bind (uses the game's logic) ----
    private static readonly HashSet<string> ConstantRoles = new(StringComparer.OrdinalIgnoreCase)
    { CoreRoles.MaxHp, CoreRoles.MaxSp, CoreRoles.MaxWeight, CoreRoles.BaseLevel, CoreRoles.JobLevel };

    private void CollectTyped()
    {
        _typed.Clear();
        void Add(string role, string box) { if (int.TryParse(box?.Trim(), out int v) && v != 0) _typed[role] = v; }
        Add(CoreRoles.Hp, CurrentHp);
        Add(CoreRoles.MaxHp, InMaxHp);
        Add(CoreRoles.Sp, InSp);
        Add(CoreRoles.MaxSp, InMaxSp);
        Add(CoreRoles.BaseLevel, InBaseLevel);
        Add(CoreRoles.JobLevel, InJobLevel);
        Add(CoreRoles.Weight, InWeight);
        Add(CoreRoles.MaxWeight, InMaxWeight);
        Add(CoreRoles.Zeny, InZeny);
        Add(CoreRoles.Exp, InBaseExp);
        Add(CoreRoles.JobExp, InJobExp);
    }

    /// <summary>Step 1: first scan every typed value into its own candidate set.</summary>
    [RelayCommand] private void ScanAll()
    {
        if (!_session.Reader.Attached) _session.Reattach();
        if (!_session.Reader.Attached) { Status = "Not attached. Pick your RO process in the top bar first."; return; }
        CollectTyped();
        if (_typed.Count < 1) { Status = "Type at least your current HP, then a few more values."; return; }

        var scanner = new MemoryScanner(_session.Reader);
        _multi.Clear();
        foreach (var kv in _typed)
        {
            var hits = scanner.FirstScan(ScanType.Int32, kv.Value);
            if (kv.Key == CoreRoles.Weight || kv.Key == CoreRoles.MaxWeight)
                hits = hits.Concat(scanner.FirstScan(ScanType.Int32, kv.Value * 10)).ToList();   // RO stores weight x10
            _multi[kv.Key] = hits;
        }

        BindUniques();
        Status = $"Scanned ({Summary()}). Now play: take damage, spend SP, gain EXP — then press \u201cRefine\u201d. Repeat 2\u20134x until each binds.";
    }

    /// <summary>Step 2: narrow by the game's logic — HP/SP/EXP/Weight change, Max/levels stay.
    /// Press after you have actually changed those values in-game. Repeat until each is unique.</summary>
    [RelayCommand] private void RefineBind()
    {
        if (_multi.Count == 0) { Status = "Press \u201cFirst scan all\u201d first."; return; }
        RefineOnce();
        Status = _multi.Count == 0
            ? $"All values bound and saved ({Summary(true)}). Use \u201cMake permanent\u201d to keep them across restarts."
            : $"Refined ({Summary()}). Keep playing + Refine until each reaches 1.";
    }

    private void RefineOnce()
    {
        if (_multi.Count == 0) return;
        var scanner = new MemoryScanner(_session.Reader);
        foreach (var role in _multi.Keys.ToList())
        {
            var filter = ConstantRoles.Contains(role) ? ScanFilter.Unchanged : ScanFilter.Changed;
            var narrowed = scanner.NextScan(_multi[role], ScanType.Int32, filter, null);
            if (narrowed.Count > 0) _multi[role] = narrowed;   // keep previous if a "changed" role didn't actually change
        }
        BindUniques();
    }

    /// <summary>Hands-free capture: scan, then for 15s narrow by the game's logic while the player
    /// moves/fights, with an on-game countdown overlay. Binds values as they become unique.</summary>
    [RelayCommand] private void AutoCapture()
    {
        if (!_session.Reader.Attached) _session.Reattach();
        if (!_session.Reader.Attached) { Status = "Not attached. Pick your RO process first."; return; }
        CollectTyped();
        if (_typed.Count < 1) { Status = "Type at least your current HP before capturing."; return; }

        var scanner = new MemoryScanner(_session.Reader);
        _multi.Clear();
        foreach (var kv in _typed)
        {
            var hits = scanner.FirstScan(ScanType.Int32, kv.Value);
            if (kv.Key == CoreRoles.Weight || kv.Key == CoreRoles.MaxWeight)
                hits = hits.Concat(scanner.FirstScan(ScanType.Int32, kv.Value * 10)).ToList();
            _multi[kv.Key] = hits;
        }
        BindUniques();

        _captureLeft = 15;
        try { _captureOverlay = new CaptureOverlayWindow(_session); _captureOverlay.Show(); _captureOverlay.SetStatus(_captureLeft, CaptureLine()); } catch { _captureOverlay = null; }

        _captureTimer?.Stop();
        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _captureTimer.Tick += (_, _) => CaptureTick();
        _captureTimer.Start();
        Status = "Capturing 15s \u2014 move around, take damage, spend SP, gain EXP.";
    }

    private void CaptureTick()
    {
        RefineOnce();
        _captureLeft--;
        _captureOverlay?.SetStatus(_captureLeft, CaptureLine());
        if (_captureLeft > 0 && _multi.Count > 0) return;
        _captureTimer?.Stop();
        try { _captureOverlay?.Close(); } catch { }
        _captureOverlay = null;
        RankRemaining();
        Status = _multi.Count == 0
            ? $"Capture done \u2014 bound: {string.Join(", ", _session.AddressBook.Entries.Keys)}."
            : $"Capture done. Bound {_session.AddressBook.Entries.Count}. Remaining candidates ranked in Found \u2014 pick the top, move to Saved, assign role.";
    }

    private string CaptureLine()
    {
        string bound = string.Join(", ", _session.AddressBook.Entries.Keys);
        string rem = string.Join("  ", _multi.Select(kv => $"{kv.Key}:{kv.Value.Count}"));
        return $"Bound: {(bound.Length == 0 ? "-" : bound)}    Narrowing: {(rem.Length == 0 ? "-" : rem)}";
    }

    private void RankRemaining()
    {
        Found.Clear();
        var rows = new List<ScanRow>();
        foreach (var kv in _multi)
            foreach (var h in kv.Value.Take(300))
            {
                long a = (long)h.Address;
                rows.Add(new ScanRow { Address = a, Type = "Int32", Value = h.Display, Description = kv.Key, Score = ClusterScore(a) });
            }
        foreach (var r in rows.OrderByDescending(r => r.Score).ThenBy(r => r.Address).Take(500)) Found.Add(r);
    }

    /// <summary>Probability heuristic: an address next to already-bound roles is in the same struct.</summary>
    private int ClusterScore(long addr)
    {
        int near = 0;
        foreach (var e in _session.AddressBook.Entries.Values)
            if (Math.Abs((long)e.Resolve(_session.Reader.ModuleBase) - addr) <= 0x800) near++;
        return Math.Min(99, 35 + near * 20);
    }

    private string Summary(bool boundOnly = false)
    {
        if (boundOnly) return string.Join(", ", _session.AddressBook.Entries.Keys);
        return string.Join(", ", _multi.Select(kv => $"{kv.Key}:{kv.Value.Count}"));
    }

    private void BindUniques()
    {
        foreach (var role in _multi.Keys.ToList())
        {
            if (_multi[role].Count == 1)
            {
                var addr = _multi[role][0].Address;
                SaveRole(role, addr, "Int32");
                _multi.Remove(role);
                DeriveNeighbor(role, (long)addr);
            }
        }
        HydrateSaved();
    }

    /// <summary>Once HP/SP is pinned, its Max sits a few bytes away — read neighbors for the typed Max.</summary>
    private void DeriveNeighbor(string role, long addr)
    {
        string? maxRole = role == CoreRoles.Hp ? CoreRoles.MaxHp : role == CoreRoles.Sp ? CoreRoles.MaxSp : null;
        if (maxRole is null || _session.AddressBook.Has(maxRole)) return;
        if (!_typed.TryGetValue(maxRole, out int want)) return;
        foreach (int off in new[] { 4, -4, 8, 12, -8, 16 })
        {
            var a = (IntPtr)(addr + off);
            if (_session.Reader.ReadInt32(a) == want) { SaveRole(maxRole, a, "Int32"); _multi.Remove(maxRole); break; }
        }
    }

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
