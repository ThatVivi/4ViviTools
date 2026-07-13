using System;
using System.Diagnostics;

namespace FourRVivi.App.Services;

/// <summary>Builds divine-pride database URLs and opens them. Two modes:
///  - OpenExternal: the user's default browser (always available).
///  - An in-app browser window is provided separately (BrowserWindow) when a WebView is available.</summary>
public static class DivinePrideLinks
{
    public static string ItemPage(int id)    => $"https://www.divine-pride.net/database/item/{id}";
    public static string MonsterPage(int id) => $"https://www.divine-pride.net/database/monster/{id}";
    public static string SkillPage(int id)   => $"https://www.divine-pride.net/database/skill/{id}";
    public static string MapPage(string name)  => $"https://www.divine-pride.net/database/map/{name}";
    public static string MapImage(string name) => $"https://www.divine-pride.net/img/map/raw/{name}";

    public static string ItemImage(int id)    => $"https://www.divine-pride.net/img/items/item/jRO/{id}";
    public static string MonsterImage(int id) => $"https://static.divine-pride.net/images/mobs/png/{id}.png";
    public static string SkillImage(int id)   => $"https://static.divine-pride.net/images/skill/{id}.png";

    /// <summary>Open a URL in the user's default browser.</summary>
    public static void OpenExternal(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }

    public static void OpenItem(int id) => OpenExternal(ItemPage(id));
    public static void OpenMonster(int id) => OpenExternal(MonsterPage(id));
    public static void OpenSkill(int id) => OpenExternal(SkillPage(id));
}
