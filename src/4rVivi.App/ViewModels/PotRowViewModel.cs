using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

/// <summary>Observable wrapper over a shared PotConfig (the engine reads the same instance).</summary>
public sealed class PotRowViewModel : ObservableObject
{
    private readonly PotConfig _c;
    private readonly Action _onChange;
    public PotConfig Model => _c;

    public PotRowViewModel(PotConfig c, Action onChange) { _c = c; _onChange = onChange; }

    public bool Enabled { get => _c.Enabled; set { _c.Enabled = value; OnPropertyChanged(); _onChange(); } }
    public string Key { get => _c.Key; set { _c.Key = value; OnPropertyChanged(); _onChange(); } }
    public int Percent { get => _c.Percent; set { _c.Percent = value; OnPropertyChanged(); _onChange(); } }
    public int Flat { get => _c.Flat; set { _c.Flat = value; OnPropertyChanged(); _onChange(); } }
    public bool UseSp { get => _c.UseSp; set { _c.UseSp = value; OnPropertyChanged(); _onChange(); } }
    public int ReactionMs { get => _c.ReactionMs; set { _c.ReactionMs = value; OnPropertyChanged(); _onChange(); } }
    public int UseDelayMs { get => _c.UseDelayMs; set { _c.UseDelayMs = value; OnPropertyChanged(); _onChange(); } }
}
