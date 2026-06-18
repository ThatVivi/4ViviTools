using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FourRVivi.App.ViewModels;

public sealed record PluginFeedItem(string Name, string Version, string Description, string Url);

/// <summary>Plugin marketplace: loads a JSON feed and installs plugin .dlls into the Plugins folder.</summary>
public sealed partial class MarketplaceViewModel : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNameCaseInsensitive = true };

    public ObservableCollection<PluginFeedItem> Items { get; } = new();
    [ObservableProperty] private string _feedUrl = "https://raw.githubusercontent.com/ThatVivi/4ViviTools/main/plugins.json";
    [ObservableProperty] private PluginFeedItem? _selected;
    [ObservableProperty] private string _status = "Load the plugin feed, pick one, then Install. Restart to activate.";

    [RelayCommand]
    private async Task Load()
    {
        try
        {
            Items.Clear();
            using var resp = await Http.GetAsync(FeedUrl);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            { Status = "No plugin feed published yet (404). This is normal until plugins are added."; return; }
            if (!resp.IsSuccessStatusCode)
            { Status = $"Feed returned {(int)resp.StatusCode}. Check the URL."; return; }
            var json = await resp.Content.ReadAsStringAsync();
            var list = JsonSerializer.Deserialize<List<PluginFeedItem>>(json, JsonOpt);
            if (list != null) foreach (var i in list) Items.Add(i);
            Status = Items.Count == 0 ? "Feed loaded — no plugins listed yet." : $"{Items.Count} plugin(s) in the feed.";
        }
        catch (Exception e) { Status = "Load failed: " + e.Message; }
    }

    [RelayCommand]
    private async Task Install()
    {
        if (Selected is null) { Status = "Select a plugin first."; return; }
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "Plugins");
            Directory.CreateDirectory(dir);
            var bytes = await Http.GetByteArrayAsync(Selected.Url);
            await File.WriteAllBytesAsync(Path.Combine(dir, Selected.Name + ".dll"), bytes);
            Status = $"Installed {Selected.Name} v{Selected.Version}. Restart 4rVivi to load it.";
        }
        catch (Exception e) { Status = "Install failed: " + e.Message; }
    }
}
