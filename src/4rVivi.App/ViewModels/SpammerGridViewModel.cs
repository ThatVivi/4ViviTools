using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

/// <summary>One key cell in the 4RTools Skill Spammer grid.</summary>
public sealed partial class SpamKey : ObservableObject
{
    public string Key { get; }
    private readonly System.Action _changed;
    [ObservableProperty] private bool _enabled;
    public SpamKey(string key, System.Action changed) { Key = key; _changed = changed; }
    partial void OnEnabledChanged(bool value) => _changed();
}

/// <summary>Faithful 4RTools "Skill Spammer" tab: a grid of toggleable keys (F1–F9, 1–9, QWERTYUIO,
/// ASDFGHJKL, ZXCVBNM) + AHK config. Enabled keys feed our SkillSpamEngine rotation. OCR/attach-gated.</summary>
public sealed partial class SpammerGridViewModel : ViewModelBase
{
    private readonly EngineHub _hub;

    public ObservableCollection<SpamKey> Keys { get; } = new();

    // AHK Configuration (faithful to 4RTools/Model/AHK.cs)
    public string[] ClickModes { get; } = { "With mouse click", "No mouse click", "Deactivated" };
    [ObservableProperty] private string _clickMode = "No mouse click";
    public string[] AhkModes { get; } = { "Compatibility", "Speed boost" };
    [ObservableProperty] private string _ahkMode = "Compatibility";
    [ObservableProperty] private bool _mouseFlick;
    [ObservableProperty] private bool _noShift;
    [ObservableProperty] private int _spammerDelay = 10;
    [ObservableProperty] private bool _enabled;

    public SpammerGridViewModel(EngineHub hub)
    {
        _hub = hub;
        foreach (var row in new[] { "F1 F2 F3 F4 F5 F6 F7 F8 F9", "1 2 3 4 5 6 7 8 9",
                                    "Q W E R T Y U I O", "A S D F G H J K L", "Z X C V B N M" })
            foreach (var k in row.Split(' ')) Keys.Add(new SpamKey(k, Sync));
    }

    partial void OnEnabledChanged(bool value) => _hub.Spammer.Enabled = value;
    partial void OnSpammerDelayChanged(int value) => _hub.Spammer.DelayMs = System.Math.Max(10, value);

    private void Sync()
    {
        _hub.Spammer.Rotation.Clear();
        foreach (var k in Keys.Where(k => k.Enabled)) _hub.Spammer.Rotation.Add(k.Key);
        _hub.Spammer.DelayMs = System.Math.Max(10, SpammerDelay);
    }
}
