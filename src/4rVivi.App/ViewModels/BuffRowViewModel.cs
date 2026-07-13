using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

public sealed class BuffRowViewModel : ObservableObject
{
    private readonly BuffRule _c;
    public BuffRule Model => _c;
    public BuffRowViewModel(BuffRule c) { _c = c; }

    public string Name { get => _c.Name; set { _c.Name = value; OnPropertyChanged(); } }
    public bool Enabled { get => _c.Enabled; set { _c.Enabled = value; OnPropertyChanged(); } }
    public string Key { get => _c.Key; set { _c.Key = value; OnPropertyChanged(); } }
    public int IntervalMs { get => _c.IntervalMs; set { _c.IntervalMs = value; OnPropertyChanged(); } }
}
