using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FourRVivi.Core.Data;
using FourRVivi.Core.Trackers;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class DbRow : ObservableObject
{
    public string Kind { get; }
    public int IdNum { get; }
    public string Id { get; }
    public string Name { get; }
    public string Info { get; }
    [ObservableProperty] private Bitmap? _icon;

    public DbRow(string kind, int idNum, string id, string name, string info)
    { Kind = kind; IdNum = idNum; Id = id; Name = name; Info = info; }
}

public sealed partial class DatabaseViewModel : ViewModelBase
{
    private readonly Lazy<GameDatabase> _db;
    private readonly MvpIconService _mob;
    private readonly ItemIconService _item;
    private readonly SkillIconService _skill;

    public string[] Kinds { get; } = { "Mobs", "Skills", "Items", "Maps" };
    public ObservableCollection<DbRow> Results { get; } = new();

    [ObservableProperty] private string _selectedKind = "Mobs";
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _diagnostics = "Type a search and hit Search (the database loads on first use).";

    public DatabaseViewModel(Lazy<GameDatabase> db, MvpIconService mob, ItemIconService item, SkillIconService skill)
    { _db = db; _mob = mob; _item = item; _skill = skill; }

    [RelayCommand] private void Search()
    {
        Results.Clear();
        var db = _db.Value;
        if (!db.IsLoaded) { Diagnostics = "Database not loaded — " + db.Diagnostics(); return; }
        Diagnostics = db.Diagnostics();
        string q = Query.Trim(); if (q.Length == 0) return;
        switch (SelectedKind)
        {
            case "Mobs": foreach (var m in db.SearchMobs(q)) Results.Add(new("Mobs", m.Id, m.Id.ToString(), m.Name, $"Lv{m.Level} | HP {m.Hp} | {m.Race}/{m.Element} | EXP {m.BaseExp}{(m.Mvp ? " | MVP" : "")}")); break;
            case "Skills": foreach (var s in db.SearchSkills(q)) Results.Add(new("Skills", s.Id, s.Id.ToString(), s.Name, $"cast {s.CastTimeMs}ms | delay {s.AfterCastDelayMs}ms | cd {s.CooldownMs}ms")); break;
            case "Items": foreach (var i in db.SearchItems(q)) Results.Add(new("Items", i.Id, i.Id.ToString(), i.Name, $"{i.Type} | slots {i.Slots} | wt {i.Weight}")); break;
            case "Maps": foreach (var mp in db.SearchMaps(q)) Results.Add(new("Maps", 0, "", mp, "")); break;
        }
        Diagnostics = $"{Results.Count} result(s). " + db.Diagnostics();
        LoadIcons(new List<DbRow>(Results));
    }

    /// <summary>Loads each row's icon from the right divine-pride source for its kind.</summary>
    private async void LoadIcons(List<DbRow> rows)
    {
        foreach (var r in rows)
        {
            try
            {
                string? path = r.Kind switch
                {
                    "Mobs"   => await _mob.EnsureIconAsync(r.IdNum),
                    "Items"  => await _item.EnsureIconAsync(r.IdNum),
                    "Skills" => await _skill.EnsureIconAsync(r.IdNum),
                    _ => null
                };
                if (path != null)
                {
                    var bmp = new Bitmap(path);
                    Dispatcher.UIThread.Post(() => r.Icon = bmp);
                }
            }
            catch { }
        }
    }

    [RelayCommand]
    private void OpenDp(DbRow? row)
    {
        if (row == null) return;
        switch (row.Kind)
        {
            case "Mobs": DivinePrideLinks.OpenMonster(row.IdNum); break;
            case "Items": DivinePrideLinks.OpenItem(row.IdNum); break;
            case "Skills": DivinePrideLinks.OpenSkill(row.IdNum); break;
            case "Maps": DivinePrideLinks.OpenExternal(DivinePrideLinks.MapPage(row.Name)); break;
        }
    }

    [RelayCommand] private void Reload() { Search(); }
}
