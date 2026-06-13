using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using Avalonia;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.ViewModels;

/// <summary>Bundles Dr. Azzy's homunculus/merc AI. Pick the game folder, review files, Apply → copies into &lt;game&gt;\AI\USER_AI.</summary>
public sealed partial class HomunAiViewModel : ViewModelBase
{
    private readonly SettingsStore _settings;
    public ObservableCollection<string> Files { get; } = new();

    [ObservableProperty] private string _gameFolder = "";
    [ObservableProperty] private string _status = "Paste your RO game folder (the one with the client .exe), review the AzzyAI files, then Apply.";

    public HomunAiViewModel(SettingsStore settings)
    {
        _settings = settings;
        _gameFolder = settings.Current.GameFolder;
        LoadFileList();
    }

    partial void OnGameFolderChanged(string value) { _settings.Current.GameFolder = value; _settings.Save(); }

    private void LoadFileList()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://4rVivi/Assets/azzyai.zip"));
            using var zip = new ZipArchive(s, ZipArchiveMode.Read);
            foreach (var e in zip.Entries.Where(e => e.Name.Length > 0)) Files.Add(e.FullName);
        }
        catch (Exception ex) { Status = "Could not read bundled AzzyAI: " + ex.Message; }
    }

    [RelayCommand] private void Apply()
    {
        if (string.IsNullOrWhiteSpace(GameFolder) || !Directory.Exists(GameFolder))
        { Status = "Set a valid game folder first."; return; }
        try
        {
            string aiDir = Path.Combine(GameFolder, "AI");
            Directory.CreateDirectory(aiDir);
            using var s = AssetLoader.Open(new Uri("avares://4rVivi/Assets/azzyai.zip"));
            using var zip = new ZipArchive(s, ZipArchiveMode.Read);
            int n = 0;
            foreach (var e in zip.Entries)
            {
                if (e.Name.Length == 0) continue;            // dir entry
                string dest = Path.Combine(aiDir, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var es = e.Open();
                using var fs = File.Create(dest);
                es.CopyTo(fs);
                n++;
            }
            Status = $"Applied {n} AzzyAI files to {aiDir}\\USER_AI. Enable it in-game with @aimode / restart your homunculus.";
        }
        catch (Exception ex) { Status = "Apply failed: " + ex.Message; }
    }
}
