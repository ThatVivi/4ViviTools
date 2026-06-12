using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Automation;

namespace FourRVivi.App.ViewModels;

public sealed partial class MacroRowViewModel : ObservableObject
{
    public ChainMacro Model { get; }
    private readonly Action _onChange;
    public MacroRowViewModel(ChainMacro m, Action onChange) { Model = m; _onChange = onChange; }

    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); _onChange(); } }
    public bool LoopWhileOn { get => Model.LoopWhileOn; set { Model.LoopWhileOn = value; OnPropertyChanged(); _onChange(); } }
    public int IntervalMs { get => Model.IntervalMs; set { Model.IntervalMs = value; OnPropertyChanged(); _onChange(); } }

    public string KeysCsv
    {
        get => string.Join(", ", Model.Steps.Select(s => s.Key));
        set
        {
            Model.Steps.Clear();
            foreach (var k in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Model.Steps.Add(new ChainStep { Key = k, GapMs = 120 });
            OnPropertyChanged(); _onChange();
        }
    }
}
