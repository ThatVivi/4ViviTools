using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Grf;

namespace FourRVivi.App.ViewModels;

public sealed partial class GrfViewModel : ViewModelBase
{
    private GrfArchive? _grf;
    private List<GrfEntry> _all = new();
    public ObservableCollection<GrfEntry> Entries { get; } = new();

    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private GrfEntry? _selected;
    [ObservableProperty] private string _status = "Paste a path to a .grf, then Open. Then filter, select a file, and Extract.";

    partial void OnFilterChanged(string value) => Publish();

    [RelayCommand] private void Open()
    {
        try
        {
            _grf?.Dispose();
            _grf = new GrfArchive(Path);
            _all = _grf.Entries;
            Publish();
            Status = $"Opened: {_all.Count} files.";
        }
        catch (Exception e) { Status = "Open failed: " + e.Message; }
    }

    private void Publish()
    {
        Entries.Clear();
        foreach (var e in _all.Where(e => Filter.Length == 0 || e.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)).Take(3000))
            Entries.Add(e);
    }

    [RelayCommand] private void Extract()
    {
        if (_grf is null || Selected is null) { Status = "Open a GRF and select a file first."; return; }
        try
        {
            var bytes = _grf.Extract(Selected);
            if (bytes is null) { Status = "Cannot extract (DES-encrypted entry, unsupported)."; return; }
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? ".", "_extracted");
            string outPath = System.IO.Path.Combine(dir, Selected.Name.Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
            System.IO.File.WriteAllBytes(outPath, bytes);
            Status = $"Extracted to {outPath}";
        }
        catch (Exception e) { Status = "Extract failed: " + e.Message; }
    }
}
