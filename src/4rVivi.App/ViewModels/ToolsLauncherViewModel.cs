using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

/// <summary>Launch the bundled standalone editors from configurable paths.</summary>
public sealed partial class ToolsLauncherViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    [ObservableProperty] private string _grfEditor = "";
    [ObservableProperty] private string _actEditor = "";
    [ObservableProperty] private string _nemo = "";
    [ObservableProperty] private string _status = "Point each to its .exe (or drop them in a /tools folder), then Launch.";

    public ToolsLauncherViewModel(SettingsStore settings)
    {
        _settings = settings;
        var p = settings.Current.ExternalToolPaths;
        _grfEditor = p.GetValueOrDefault("GRFEditor", "");
        _actEditor = p.GetValueOrDefault("ActEditor", "");
        _nemo = p.GetValueOrDefault("Nemo", "");
    }

    [RelayCommand] private void Save()
    {
        var p = _settings.Current.ExternalToolPaths;
        p["GRFEditor"] = GrfEditor; p["ActEditor"] = ActEditor; p["Nemo"] = Nemo;
        _settings.Save(); Status = "Paths saved.";
    }

    [RelayCommand] private void Launch(string which)
    {
        string path = which switch { "GRFEditor" => GrfEditor, "ActEditor" => ActEditor, "Nemo" => Nemo, _ => "" };
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) { Status = $"{which}: set a valid .exe path first."; return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Status = $"Launched {which}."; }
        catch (Exception e) { Status = $"Launch failed: {e.Message}"; }
    }
}
