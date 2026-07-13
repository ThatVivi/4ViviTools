using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using FourRVivi.Core.Settings;
using FourRVivi.App.Services;
using FourRVivi.Core.Game;

namespace FourRVivi.App.ViewModels;

/// <summary>App preferences (theme accent, language, opacity, game folder / GRF, divine-pride key).
/// Persists to settings.json and applies the icon source live.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    private readonly IconImageService _icons;
    private readonly DiscordPresenceUpdater _discord;
    private readonly GameSession _game;

    public string[] Languages { get; } = { "en", "ar" };

    public string[] Themes { get; } = { "Red", "Black" };
    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _theme = "Red";
    [ObservableProperty] private string _accentHex = "#7C6CF7";

    partial void OnThemeChanged(string value) => ApplyTheme(value);

    public static void ApplyTheme(string theme)
    {
        var app = Avalonia.Application.Current;
        if (app == null) return;

        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        var black = string.Equals(theme, "Black", StringComparison.OrdinalIgnoreCase);
        var red = !black;
        var light = false;

        SetBrush(app, "BgBrush", light ? "#F4F5F7" : red ? "#0B0B0D" : "#070708");
        SetBrush(app, "SurfaceBrush", light ? "#FFFFFF" : red ? "#131316" : "#101013");
        SetBrush(app, "Surface2Brush", light ? "#EEF0F4" : red ? "#1C1C21" : "#17181D");
        SetBrush(app, "BorderBrush", light ? "#D5D9E2" : red ? "#3C2228" : "#2A2D34");
        SetBrush(app, "TextBrush", light ? "#101218" : "#ECEEF5");
        SetBrush(app, "TextMutedBrush", light ? "#667085" : red ? "#B8AEB5" : "#A4A8B3");
        SetBrush(app, "AccentBrush", red ? "#C41E3A" : light ? "#B91C1C" : "#D8DEE9");
        SetBrush(app, "Accent2Brush", red ? "#7A1020" : light ? "#E5E7EB" : "#2A2D34");
        SetBrush(app, "OkBrush", "#34C78E");
        SetBrush(app, "DangerBrush", "#D11A2A");
    }

    private static void SetBrush(Avalonia.Application app, string key, string hex) =>
        app.Resources[key] = new SolidColorBrush(Color.Parse(hex));

    private static string NormalizeTheme(string? theme) =>
        string.Equals(theme, "Black", StringComparison.OrdinalIgnoreCase) ? "Black" : "Red";
    [ObservableProperty] private int _windowOpacity = 100;
    [ObservableProperty] private bool _humanizeTiming = true;
    [ObservableProperty] private bool _acrylicBackdrop = true;
    [ObservableProperty] private string _gameFolder = "";
    [ObservableProperty] private string _grfPath = "";
    [ObservableProperty] private string _divinePrideApiKey = "";
    [ObservableProperty] private bool _discordEnabled;
    [ObservableProperty] private string _discordAppId = "";
    [ObservableProperty] private string _discordWebsiteUrl = "";
    [ObservableProperty] private string _discordServerName = "Eldrynn RO";
    [ObservableProperty] private string _status = "Edit your preferences, then Save.";

    public SettingsViewModel(SettingsStore settings, IconImageService icons, DiscordPresenceUpdater discord, GameSession game)
    {
        _settings = settings; _icons = icons; _discord = discord; _game = game;
        var c = settings.Current;
        Language = c.Language;
        Theme = NormalizeTheme(c.Theme);
        AccentHex = c.AccentHex;
        WindowOpacity = c.WindowOpacity;
        HumanizeTiming = c.HumanizeTiming;
        AcrylicBackdrop = c.AcrylicBackdrop;
        GameFolder = c.GameFolder;
        GrfPath = c.GrfPath;
        DivinePrideApiKey = c.DivinePrideApiKey;
        DiscordEnabled = c.DiscordEnabled;
        DiscordAppId = c.DiscordAppId;
        DiscordWebsiteUrl = c.DiscordWebsiteUrl;
        DiscordServerName = c.DiscordServerName;
    }

    [RelayCommand]
    private void Save()
    {
        var c = _settings.Current;
        c.Language = string.IsNullOrWhiteSpace(Language) ? "en" : Language;
        c.Theme = NormalizeTheme(Theme);
        c.AccentHex = AccentHex;
        c.WindowOpacity = Math.Clamp(WindowOpacity, 15, 100);
        c.HumanizeTiming = HumanizeTiming;
        c.AcrylicBackdrop = AcrylicBackdrop;
        c.GameFolder = GameFolder.Trim();
        c.GrfPath = GrfPath.Trim();
        c.DivinePrideApiKey = DivinePrideApiKey.Trim();
        c.DiscordEnabled = DiscordEnabled;
        c.DiscordAppId = DiscordAppId.Trim();
        c.DiscordWebsiteUrl = DiscordWebsiteUrl.Trim();
        c.DiscordServerName = string.IsNullOrWhiteSpace(DiscordServerName) ? "Eldrynn RO" : DiscordServerName.Trim();
        _settings.Save();

        try { _icons.SetGameFolder(c.GameFolder); _icons.SetGrf(c.GrfPath); } catch { }
        try { DiscordPresenceBootstrap.Apply(_discord, _game, c); } catch { }
        Status = DiscordEnabled
            ? "Saved. Discord presence (re)connected. Make sure Discord desktop is running."
            : "Saved. Discord presence off. Some changes (theme, language) apply on next launch.";
    }
}
