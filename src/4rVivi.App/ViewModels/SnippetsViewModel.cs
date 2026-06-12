using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FourRVivi.Core.Tools;

namespace FourRVivi.App.ViewModels;

public sealed partial class SnippetsViewModel : ViewModelBase
{
    public ObservableCollection<Snippet> Items { get; } = new();
    [ObservableProperty] private Snippet? _selected;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _code = "";

    public SnippetsViewModel() => Reload();

    partial void OnQueryChanged(string value) => Reload();
    partial void OnSelectedChanged(Snippet? value) => Code = value?.Code ?? "";

    private void Reload()
    {
        Items.Clear();
        foreach (var s in NpcSnippets.All)
            if (Query.Length == 0 || s.Title.Contains(Query, StringComparison.OrdinalIgnoreCase) || s.Category.Contains(Query, StringComparison.OrdinalIgnoreCase))
                Items.Add(s);
        if (Selected is null && Items.Count > 0) Selected = Items[0];
    }
}
