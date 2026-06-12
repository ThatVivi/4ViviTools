namespace FourRVivi.App.Services;

/// <summary>Lets any view request navigation or a master toggle without a circular VM reference.</summary>
public sealed class NavigationService
{
    public event Action<string>? NavigationRequested;
    public event Action? MasterToggleRequested;
    public void GoTo(string key) => NavigationRequested?.Invoke(key);
    public void ToggleMaster() => MasterToggleRequested?.Invoke();
}
