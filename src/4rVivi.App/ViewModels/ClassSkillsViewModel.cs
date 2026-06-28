using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Data;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class ClassSkillRow : ObservableObject
{
    public string Aegis { get; }
    public int Id { get; }
    public Bitmap? Icon { get; }
    private readonly Action _changed;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _key = "F1";

    public ClassSkillRow(string aegis, int id, Action changed)
    {
        Aegis = aegis; Id = id; _changed = changed;
        Icon = IconImageService.Instance?.GetSkill(aegis, id);
    }
    partial void OnEnabledChanged(bool value) => _changed();
    partial void OnKeyChanged(string value) => _changed();
}

/// <summary>Class-centric skill spammer: pick a class, enable its skills (icons from GRF), set a key each.</summary>
public sealed partial class ClassSkillsViewModel : ViewModelBase
{
    private readonly EngineHub _hub;
    private readonly ClassData _cd;

    public IReadOnlyList<string> Classes { get; }
    public ObservableCollection<ClassSkillRow> Skills { get; } = new();

    [ObservableProperty] private string _selectedClass;
    [ObservableProperty] private int _spamDelay = 300;
    [ObservableProperty] private string _status = "Pick your class, tick the skills to spam, set a key for each, then toggle Spam ON (top of this tab) or on the Dashboard.";

    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) => _hub.Spammer.Enabled = value;

    public ClassSkillsViewModel(EngineHub hub, ClassData cd)
    {
        _hub = hub; _cd = cd;
        Classes = cd.Jobs;
        _selectedClass = Classes.FirstOrDefault() ?? "";
        Rebuild();
    }

    partial void OnSelectedClassChanged(string value) => Rebuild();
    partial void OnSpamDelayChanged(int value) => _hub.Spammer.DelayMs = Math.Max(50, value);

    private void Rebuild()
    {
        Skills.Clear();
        foreach (var s in _cd.SkillsFor(SelectedClass)) Skills.Add(new ClassSkillRow(s.Aegis, s.Id, Sync));
        Sync();
    }

    private void Sync()
    {
        _hub.Spammer.Rotation.Clear();
        foreach (var r in Skills)
            if (r.Enabled && !string.IsNullOrWhiteSpace(r.Key)) _hub.Spammer.Rotation.Add(r.Key);
        _hub.Spammer.DelayMs = Math.Max(50, SpamDelay);
        Status = _hub.Spammer.Rotation.Count == 0
            ? "No skills queued. Tick skills + set keys, then toggle Spam ON (top of this tab) or on the Dashboard."
            : $"{_hub.Spammer.Rotation.Count} skill(s) queued — Spam ON to run them.";
    }
}
