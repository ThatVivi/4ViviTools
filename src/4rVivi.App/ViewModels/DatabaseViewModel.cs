using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Data;

namespace FourRVivi.App.ViewModels;

public sealed record DbRow(string Id, string Name, string Info);

public sealed partial class DatabaseViewModel : ViewModelBase
{
    private readonly Lazy<GameDatabase> _db;
    public string[] Kinds { get; } = { "Mobs", "Skills", "Items", "Maps" };
    public ObservableCollection<DbRow> Results { get; } = new();

    [ObservableProperty] private string _selectedKind = "Mobs";
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _diagnostics = "Type a search and hit Search (the database loads on first use).";

    public DatabaseViewModel(Lazy<GameDatabase> db) => _db = db;

    [RelayCommand] private void Search()
    {
        Results.Clear();
        var db = _db.Value;
        if (!db.IsLoaded) { Diagnostics = "Database not loaded — " + db.Diagnostics(); return; }
        Diagnostics = db.Diagnostics();
        string q = Query.Trim(); if (q.Length == 0) return;
        switch (SelectedKind)
        {
            case "Mobs": foreach (var m in db.SearchMobs(q)) Results.Add(new(m.Id.ToString(), m.Name, $"Lv{m.Level} | HP {m.Hp} | {m.Race}/{m.Element} | EXP {m.BaseExp}{(m.Mvp ? " | MVP" : "")}")); break;
            case "Skills": foreach (var s in db.SearchSkills(q)) Results.Add(new(s.Id.ToString(), s.Name, $"cast {s.CastTimeMs}ms | delay {s.AfterCastDelayMs}ms | cd {s.CooldownMs}ms")); break;
            case "Items": foreach (var i in db.SearchItems(q)) Results.Add(new(i.Id.ToString(), i.Name, $"{i.Type} | slots {i.Slots} | wt {i.Weight}")); break;
            case "Maps": foreach (var mp in db.SearchMaps(q)) Results.Add(new("", mp, "")); break;
        }
        Diagnostics = $"{Results.Count} result(s). " + db.Diagnostics();
    }

    [RelayCommand] private void Reload() { Search(); }
}
