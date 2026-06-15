using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

public sealed partial class OverlayViewModel : ViewModelBase
{
    private readonly OverlayController _overlay;
    [ObservableProperty] private int _castRange;
    [ObservableProperty] private int _aoe;
    [ObservableProperty] private bool _gutter;

    public OverlayViewModel(OverlayController overlay) => _overlay = overlay;

    partial void OnCastRangeChanged(int value) { _overlay.CastRange = value; _overlay.Apply(); }
    partial void OnAoeChanged(int value) { _overlay.Aoe = value; _overlay.Apply(); }
    partial void OnGutterChanged(bool value) { _overlay.Gutter = value; _overlay.Apply(); }

    [RelayCommand] private void Show() => _overlay.Show();
    [RelayCommand] private void Hide() => _overlay.Hide();
}
