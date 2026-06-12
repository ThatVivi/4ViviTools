using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FourRVivi.App.ViewModels;

public sealed class NavSection
{
    public string Title { get; }
    public NavSection(string title) => Title = title;
}

public sealed partial class NavPage : ObservableObject
{
    public string Title { get; }
    public string Key { get; }
    public ViewModelBase ViewModel { get; }
    private readonly Action<NavPage> _onSelect;

    [ObservableProperty] private bool _isActive;

    public NavPage(string title, string key, ViewModelBase vm, Action<NavPage> onSelect)
    { Title = title; Key = key; ViewModel = vm; _onSelect = onSelect; }

    [RelayCommand] private void Select() => _onSelect(this);
}
