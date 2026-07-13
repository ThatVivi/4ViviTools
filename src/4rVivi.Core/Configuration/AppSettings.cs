namespace FourRVivi.Core.Configuration;

/// <summary>appsettings.json model (theme/language/feature flags + update settings).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Red";
    public string Language { get; set; } = "en";
    public bool EnableOCR { get; set; } = true;
    public bool EnablePlugins { get; set; } = true;
    public UpdateSettings Update { get; set; } = new();
    public DiscordSettings Discord { get; set; } = new();
}

public sealed class DiscordSettings
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public int IntervalSeconds { get; set; } = 5;
    public string LargeImageKey { get; set; } = "logo";
    public string ServerName { get; set; } = "Eldrynn RO";
}

public sealed class UpdateSettings
{
    public bool AutoCheck { get; set; } = true;
    public string Repository { get; set; } = "ThatVivi/4ViviTools";
}
