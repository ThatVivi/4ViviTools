using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Data;
using FourRVivi.Core.Settings;
using FourRVivi.Core.Trackers;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class MvpRowViewModel : ObservableObject
{
    public MvpEntry Model { get; }
    public MvpRowViewModel(MvpEntry m) { Model = m; LoadIconIfCached(); }
    public int MobId { get => Model.MobId; set { Model.MobId = value; OnPropertyChanged(); } }
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); } }
    public string Map { get => Model.Map; set { Model.Map = value; OnPropertyChanged(); } }
    public int MinMinutes { get => Model.MinMinutes; set { Model.MinMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Times)); } }
    public int MaxMinutes { get => Model.MaxMinutes; set { Model.MaxMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Times)); } }
    public string Times => $"{MinMinutes} ~ {MaxMinutes} min";
    /// <summary>Best attack element vs this MVP's defence element (rAthena table).</summary>
    public string BestElement
    {
        get
        {
            var def = FourRVivi.Core.Calc.Elements.TryParse(Model.DefElement);
            return def is null ? "" : "Use " + FourRVivi.Core.Calc.Elements.BestAttackElement(def.Value);
        }
    }
    public string Status => Model.Status();
    public Avalonia.Media.IBrush StatusBrush
    {
        get { try { return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Model.StatusColor())); } catch { return Avalonia.Media.Brushes.Gray; } }
    }
    [ObservableProperty] private Bitmap? _icon;

    public void Tick() { OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusBrush)); }
    public void LoadIcon(string? path)
    {
        try { if (path is not null && System.IO.File.Exists(path)) Icon = new Bitmap(path); } catch { }
    }
    private void LoadIconIfCached()
    {
        var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "mvp_icons", Model.MobId + ".png");
        LoadIcon(p);
    }
}

public sealed partial class MvpTrackerViewModel : ViewModelBase
{
    private readonly MvpTracker _tracker;
    private readonly Lazy<GameDatabase> _db;
    private readonly MvpIconService _icons;
    private readonly SettingsStore _settings;

    public ObservableCollection<MvpRowViewModel> Entries { get; } = new();
    public ObservableCollection<MvpRowViewModel> Active { get; } = new();   // killed → ranked by soonest respawn
    public ObservableCollection<string> MvpChoices { get; } = new();
    [ObservableProperty] private string? _selectedChoice;

    // Custom MVP add
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newMap = "";
    [ObservableProperty] private int _newMin = 60;
    [ObservableProperty] private int _newMax = 70;

    [ObservableProperty] private string _status = "Pick an MVP and Add, or add a custom one. Killed → starts the respawn window.";

    public MvpTrackerViewModel(MvpTracker tracker, Lazy<GameDatabase> db, MvpIconService icons, SettingsStore settings)
    {
        _tracker = tracker; _db = db; _icons = icons; _settings = settings;
        _icons.UrlTemplate = settings.Current.DivinePrideImageUrl;
        _icons.ApiKey = settings.Current.DivinePrideApiKey;
        foreach (var e in tracker.Entries) Entries.Add(new MvpRowViewModel(e));
        LoadChoices();

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) =>
        {
            foreach (var r in Entries) r.Tick();
            RefreshActive();
            var due = _tracker.DueSoon().FirstOrDefault();
            if (due is not null) Status = $"⏰ {due.Name} is in its spawn window!";
        };
        t.Start();
        RefreshActive();
    }

    /// <summary>Right-hand panel: every killed MVP, spawned ones on top, then ranked by soonest respawn.</summary>
    private void RefreshActive()
    {
        var now = DateTime.Now;
        var ranked = Entries.Where(r => r.Model.KilledAt is not null)
            .OrderByDescending(r => r.Model.NextMax is { } mx && now > mx)        // spawned first
            .ThenBy(r => r.Model.NextMin ?? DateTime.MaxValue)                    // then soonest
            .ToList();
        if (ranked.Count == Active.Count && ranked.SequenceEqual(Active)) return; // avoid churn
        Active.Clear();
        foreach (var r in ranked) Active.Add(r);
    }

    private void LoadChoices()
    {
        MvpChoices.Clear();
        try
        {
            foreach (var name in _db.Value.MvpMobs().Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
                MvpChoices.Add(name);
        }
        catch { }
        if (MvpChoices.Count > 0 && SelectedChoice == null) SelectedChoice = MvpChoices[0];
    }

    [RelayCommand]
    private async Task AddSelected()
    {
        if (string.IsNullOrWhiteSpace(SelectedChoice)) return;
        var mob = _db.Value.MvpMobs().FirstOrDefault(m => string.Equals(m.Name, SelectedChoice, StringComparison.OrdinalIgnoreCase));
        if (mob is null) return;
        var e = new MvpEntry { MobId = mob.Id, Name = mob.Name, MinMinutes = 60, MaxMinutes = 70, DefElement = mob.Element };
        _tracker.Entries.Add(e); _tracker.Save();
        var row = new MvpRowViewModel(e); Entries.Add(row);
        Status = $"Added {mob.Name}. Set its map/timers, hit Killed when it dies.";
        try
        {
            var path = await _icons.EnsureIconAsync(mob.Id);
            if (path is not null) row.LoadIcon(path);
            var map = await _icons.FetchMapAsync(mob.Id);
            if (!string.IsNullOrWhiteSpace(map)) row.Map = map;
            _tracker.Save();
        }
        catch { }
    }

    [RelayCommand]
    private void AddCustom()
    {
        var e = new MvpEntry
        {
            Name = string.IsNullOrWhiteSpace(NewName) ? "Custom MVP" : NewName.Trim(),
            Map = NewMap.Trim(),
            MinMinutes = Math.Max(1, NewMin),
            MaxMinutes = Math.Max(1, NewMax),
        };
        _tracker.Entries.Add(e); Entries.Add(new MvpRowViewModel(e)); _tracker.Save();
        Status = $"Added custom {e.Name}.";
        NewName = ""; NewMap = "";
    }

    [RelayCommand]
    private void Clear()
    {
        _tracker.Entries.Clear(); Entries.Clear(); _tracker.Save();
        Status = "Cleared all tracked MVPs.";
    }

    [RelayCommand]
    private async Task DownloadIcons()
    {
        Status = "Downloading MVP icons from divine-pride…";
        int ok = 0;
        foreach (var r in Entries)
        {
            var path = await _icons.EnsureIconAsync(r.MobId);
            if (path is not null) { r.LoadIcon(path); ok++; }
        }
        _tracker.Save();
        Status = $"Downloaded/cached {ok} icons.";
    }

    [RelayCommand] private void RegisterKill(MvpRowViewModel row) { _tracker.RegisterKill(row.Model); row.Tick(); RefreshActive(); Status = $"{row.Name} killed — window started."; }
    [RelayCommand] private void Remove(MvpRowViewModel row) { _tracker.Entries.Remove(row.Model); Entries.Remove(row); _tracker.Save(); }
    [RelayCommand] private void OpenDp(MvpRowViewModel row) { if (row.MobId > 0) DivinePrideLinks.OpenMonster(row.MobId); }
}
