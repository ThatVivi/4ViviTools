namespace FourRVivi.Plugins.Abstractions;

/// <summary>Contract every 4rVivi plugin/module implements. The host discovers these from the
/// Plugins folder and initializes them at startup. Keeps modules decoupled from the main app.</summary>
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    Task InitializeAsync();
}
