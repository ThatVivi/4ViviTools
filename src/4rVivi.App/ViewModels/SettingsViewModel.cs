using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

/// <summary>App preferences (theme accent, language, opacity, game folder / GRF, divine-pride key).
/// Persists to settings.json and applies the icon source live.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    private readonly IconImageService _icons;

    public string[] Languages { get; } = { "en", "ar" };

    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _accentHex = "#7C6CF7";
    [ObservableProperty] private int _windowOpacity = 100;
    [ObservableProperty] private bool _humanizeTiming = true;
    [ObservableProperty] private bool _acrylicBackdrop = true;
    [ObservableProperty] private string _gameFolder = "";
    [ObservableProperty] private string _grfPath = "";
    [ObservableProperty] private string _divinePrideApiKey = "";
    [ObservableProperty] private string _status = "Edit your preferences, then Save.";

    public SettingsViewModel(SettingsStore settings, IconImageService icons)
    {
        _settings = settings; _icons = icons;
        var c = settings.Current;
        Language = c.Language;
        AccentHex = c.AccentHex;
        WindowOpacity = c.WindowOpacity;
        HumanizeTiming = c.HumanizeTiming;
        AcrylicBackdrop = c.AcrylicBackdrop;
        GameFolder = c.GameFolder;
        GrfPath = c.GrfPath;
        DivinePrideApiKey = c.DivinePrideApiKey;
    }

    [RelayCommand]
    private void Save()
    {
        var c = _settings.Current;
        c.Language = string.IsNullOrWhiteSpace(Language) ? "en" : Language;
        c.AccentHex = AccentHex;
        c.WindowOpacity = Math.Clamp(WindowOpacity, 70, 100);
        c.HumanizeTiming = HumanizeTiming;
        c.AcrylicBackdrop = AcrylicBackdrop;
        c.GameFolder = GameFolder.Trim();
        c.GrfPath = GrfPath.Trim();
        c.DivinePrideApiKey = DivinePrideApiKey.Trim();
        _settings.Save();

        try { _icons.SetGameFolder(c.GameFolder); _icons.SetGrf(c.GrfPath); } catch { }
        Status = "Saved. Some changes (theme, language) apply on next launch.";
    }
}
