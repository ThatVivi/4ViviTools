using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Automation;
using FourRVivi.Core.Localization;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    private readonly Loc _loc;
    private readonly EngineHub _hub;

    public string[] Languages { get; } = { "en", "ar" };

    [ObservableProperty] private string _language;
    [ObservableProperty] private string _accentHex;
    [ObservableProperty] private int _opacity;
    [ObservableProperty] private bool _humanize;
    [ObservableProperty] private bool _acrylic;
    [ObservableProperty] private string _status = "";

    public SettingsViewModel(SettingsStore settings, Loc loc, EngineHub hub)
    {
        _settings = settings; _loc = loc; _hub = hub;
        var s = settings.Current;
        _language = s.Language; _accentHex = s.AccentHex; _opacity = s.WindowOpacity;
        _humanize = s.HumanizeTiming; _acrylic = s.AcrylicBackdrop;
    }

    [RelayCommand] private void Save()
    {
        var s = _settings.Current;
        s.Language = Language; s.AccentHex = AccentHex; s.WindowOpacity = Math.Clamp(Opacity, 70, 100);
        s.HumanizeTiming = Humanize; s.AcrylicBackdrop = Acrylic;
        _settings.Save();
        _loc.SetLang(Language);
        _hub.Timing.Enabled = Humanize;
        Status = "Saved. Language/theme changes fully apply after a restart.";
    }
}
