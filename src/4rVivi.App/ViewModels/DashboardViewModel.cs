using CommunityToolkit.Mvvm.Input;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly NavigationService _nav;
    public DashboardViewModel(NavigationService nav) => _nav = nav;

    public string[] QuickKeys { get; } = { "Autopot", "Skills", "Bot / Farm", "RCX Overlay", "Database", "Scanner", "Settings" };

    [RelayCommand] private void Go(string key) => _nav.GoTo(key);
    [RelayCommand] private void ToggleMaster() => _nav.ToggleMaster();
}
