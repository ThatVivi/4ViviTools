using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Data;
using FourRVivi.Core.Trackers;

namespace FourRVivi.App.ViewModels;

public sealed partial class MvpRowViewModel : ObservableObject
{
    public MvpEntry Model { get; }
    public MvpRowViewModel(MvpEntry m) => Model = m;
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); } }
    public string Map { get => Model.Map; set { Model.Map = value; OnPropertyChanged(); } }
    public int MinMinutes { get => Model.MinMinutes; set { Model.MinMinutes = value; OnPropertyChanged(); } }
    public int MaxMinutes { get => Model.MaxMinutes; set { Model.MaxMinutes = value; OnPropertyChanged(); } }
    public string Status => Model.Status();
    public void Tick() => OnPropertyChanged(nameof(Status));
}

public sealed partial class MvpTrackerViewModel : ViewModelBase
{
    private readonly MvpTracker _tracker;
    private readonly Lazy<GameDatabase> _db;
    public ObservableCollection<MvpRowViewModel> Entries { get; } = new();
    [ObservableProperty] private string _status = "Register a kill to start a respawn timer. Seed from DB to import MVP names.";

    public MvpTrackerViewModel(MvpTracker tracker, Lazy<GameDatabase> db)
    {
        _tracker = tracker; _db = db;
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
            var e = new MvpEntry { Name = m.Name, MinMinutes = 60, MaxMinutes = 70 };
            _tracker.Entries.Add(e); Entries.Add(new MvpRowViewModel(e));
        }
        _tracker.Save();
        Status = $"Seeded {Entries.Count} MVPs. Set each one's map and respawn minutes, then register kills.";
    }

    [RelayCommand] private void Add()
    {
        var e = new MvpEntry { Name = "New MVP" };
        _tracker.Entries.Add(e); Entries.Add(new MvpRowViewModel(e)); _tracker.Save();
    }
    [RelayCommand] private void RegisterKill(MvpRowViewModel row) { _tracker.RegisterKill(row.Model); row.Tick(); Status = $"{row.Name} killed — timer started."; }
    [RelayCommand] private void Remove(MvpRowViewModel row) { _tracker.Entries.Remove(row.Model); Entries.Remove(row); _tracker.Save(); }
}
