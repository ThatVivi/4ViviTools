using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FourRVivi.App.ViewModels;

/// <summary>Focused text editor for item_db YAML: open a file, jump to an entry, edit, save.</summary>
public sealed partial class ItemDbEditorViewModel : ViewModelBase
{
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _find = "";
    [ObservableProperty] private string _status = "Paste a path to item_db.yml (or any text file), Load, edit, Save.";

    [RelayCommand] private void Load()
    {
        try { Content = System.IO.File.ReadAllText(Path); Status = $"Loaded {Content.Length} chars."; }
        catch (Exception e) { Status = "Load failed: " + e.Message; }
    }

    [RelayCommand] private void Save()
    {
        try { System.IO.File.WriteAllText(Path, Content); Status = "Saved."; }
        catch (Exception e) { Status = "Save failed: " + e.Message; }
    }

    [RelayCommand] private void FindNext()
    {
        if (string.IsNullOrEmpty(Find)) return;
        int idx = Content.IndexOf(Find, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) { Status = $"'{Find}' not found."; return; }
        int line = Content.Take(idx).Count(c => c == '\n') + 1;
        Status = $"Found '{Find}' at line {line}.";
    }
}
