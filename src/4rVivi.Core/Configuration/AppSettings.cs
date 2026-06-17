namespace FourRVivi.Core.Configuration;

/// <summary>appsettings.json model (theme/language/feature flags + update settings).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en";
    public bool EnableOCR { get; set; } = true;
    public bool EnablePlugins { get; set; } = true;
    public UpdateSettings Update { get; set; } = new();
}

public sealed class UpdateSettings
{
    public bool AutoCheck { get; set; } = true;
    public string Repository { get; set; } = "ThatVivi/4ViviTools";
}
