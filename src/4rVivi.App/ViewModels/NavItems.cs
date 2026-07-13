using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
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
    public string Icon { get; }
    public ViewModelBase ViewModel { get; }
    private readonly Action<NavPage> _onSelect;

    [ObservableProperty] private bool _isActive;

    public NavPage(string title, string key, ViewModelBase vm, Action<NavPage> onSelect, string icon = "")
    { Title = title; Key = key; Icon = icon; ViewModel = vm; _onSelect = onSelect; }

    [RelayCommand] private void Select() => _onSelect(this);
}

public sealed partial class NavCategory : ObservableObject
{
    public string Title { get; }
    public string Key { get; }
    public string Icon { get; }
    public ObservableCollection<NavPage> Pages { get; } = new();
    private readonly Action<NavCategory> _onSelect;

    /// <summary>Clone categories (4rTools / ro-tools) show a master ON switch that flips every sub-tab engine.</summary>
    public bool Togglable { get; set; }
    public System.Collections.Generic.List<Action<bool>> Toggles { get; } = new();

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) { foreach (var t in Toggles) t(value); }

    public NavCategory(string title, string key, Action<NavCategory> onSelect, string icon = "")
    { Title = title; Key = key; Icon = icon; _onSelect = onSelect; }

    [RelayCommand] private void Select() => _onSelect(this);
}
