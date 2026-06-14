using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Data;
using FourRVivi.Core.Settings;
using FourRVivi.Core.Trackers;

namespace FourRVivi.App.ViewModels;

public sealed partial class MvpRowViewModel : ObservableObject
{
    public MvpEntry Model { get; }
    public MvpRowViewModel(MvpEntry m) { Model = m; LoadIconIfCached(); }
    public int MobId { get => Model.MobId; set { Model.MobId = value; OnPropertyChanged(); } }
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); } }
    public string Map { get => Model.Map; set { Model.Map = value; OnPropertyChanged(); } }
    public int MinMinutes { get => Model.MinMinutes; set { Model.MinMinutes = value; OnPropertyChanged(); } }
    public int MaxMinutes { get => Model.MaxMinutes; set { Model.MaxMinutes = value; OnPropertyChanged(); } }
    public string Status => Model.Status();
    [ObservableProperty] private Bitmap? _icon;
    public void Tick() => OnPropertyChanged(nameof(Status));
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
    [ObservableProperty] private string _status = "Register a kill to start a timer. Seed from DB to import MVPs, then Download icons.";

    public MvpTrackerViewModel(MvpTracker tracker, Lazy<GameDatabase> db, MvpIconService icons, SettingsStore settings)
    {
        _tracker = tracker; _db = db; _icons = icons; _settings = settings;
        _icons.UrlTemplate = settings.Current.DivinePrideImageUrl;
        _icons.ApiKey = settings.Current.DivinePrideApiKey;
        foreach (var e in tracker.Entries) Entries.Add(new MvpRowViewModel(e));

        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) =>
        {
            foreach (var r in Entries) r.Tick();
            var due = _tracker.DueSoon().FirstOrDefault();
            if (due is not null) Status = $"⏰ {due.Name} is in its spawn window!";
        };
        t.Start();
    }

    [RelayCommand] private void SeedFromDb()
    {
        var names = Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _db.Value.MvpMobs())
        {
            if (names.Contains(m.Name)) continue;
            var e = new MvpEntry { MobId = m.Id, Name = m.Name, MinMinutes = 60, MaxMinutes = 70 };
            _tracker.Entries.Add(e); Entries.Add(new MvpRowViewModel(e));
        }
        _tracker.Save();
        Status = $"Seeded {Entries.Count} MVPs. Set map + respawn minutes, register kills, and Download icons.";
    }

    [RelayCommand] private async Task DownloadIcons()
    {
        Status = "Downloading MVP icons from divine-pride…";
        int ok = 0;
        foreach (var r in Entries)
        {
            var path = await _icons.EnsureIconAsync(r.MobId);
            if (path is not null) { r.LoadIcon(path); ok++; }
            if (string.IsNullOrWhiteSpace(r.Map))
            {
                var map = await _icons.FetchMapAsync(r.MobId);
                if (!string.IsNullOrWhiteSpace(map)) r.Map = map;
            }
        }
        _tracker.Save();
        Status = $"Downloaded/cached {ok} icons + maps. (Set the divine-pride API key in Settings if none appear.)";
    }

    [RelayCommand] private void Add()
    {
        var e = new MvpEntry { Name = "New MVP" };
        _tracker.Entries.Add(e); Entries.Add(new MvpRowViewModel(e)); _tracker.Save();
    }
    [RelayCommand] private void RegisterKill(MvpRowViewModel row) { _tracker.RegisterKill(row.Model); row.Tick(); Status = $"{row.Name} killed — timer started."; }
    [RelayCommand] private void Remove(MvpRowViewModel row) { _tracker.Entries.Remove(row.Model); Entries.Remove(row); _tracker.Save(); }
}
