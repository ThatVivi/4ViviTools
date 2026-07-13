using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FourRVivi.Core.Discord;
using FourRVivi.Core.Game;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.Services;

/// <summary>Wires Discord Rich Presence to live character state from memory and OCR.</summary>
public static class DiscordPresenceBootstrap
{
    public const string DefaultAppId = "1517200569486413954";

    public static void Apply(DiscordPresenceUpdater updater, GameSession gs, AppSettings s)
    {
        if (!s.DiscordEnabled)
        {
            updater.StopAndClear();
            return;
        }
        string appId = string.IsNullOrWhiteSpace(s.DiscordAppId) ? DefaultAppId : s.DiscordAppId.Trim();

        var reader = new CharacterStateReader(gs);
        updater.Start(appId, () =>
        {
            var cs = reader.Snapshot();
            if (cs is null)
            {
                return new RoPresence
                {
                    ServerName = s.DiscordServerName,
                    WebsiteUrl = s.DiscordWebsiteUrl,
                    Activity = "In menus",
                    LargeImageKey = "logo",
                };
            }

            string rawMap = !string.IsNullOrWhiteSpace(cs.MapName) ? cs.MapName : LiveStats.Instance.GetText(Roles.MapName);
            var (mapId, mapDisp) = ResolveMap(rawMap);
            bool knownMap = mapId.Length > 0;

            return new RoPresence
            {
                CharName = cs.Name,
                ClassName = cs.ClassName,
                BaseLevel = cs.BaseLevel,
                JobLevel = cs.JobLevel,
                MapName = knownMap ? mapId : "",
                MapDisplay = knownMap ? mapDisp : "",
                X = knownMap ? cs.X : 0,
                Y = knownMap ? cs.Y : 0,
                HpPct = cs.HpPct,
                SpPct = cs.SpPct,
                BaseExpPct = cs.BaseExpPct, JobExpPct = cs.JobExpPct,
                Activity = TranslateActivity(cs.Activity),
                ServerName = s.DiscordServerName,
                WebsiteUrl = s.DiscordWebsiteUrl,
                LargeImageKey = knownMap ? MapArtKey(mapId) : "logo",
            };
        }, 2);
    }

    private static string TranslateActivity(string a) => a switch
    {
        "Grinding" => "Attacking",
        "Walking" => "Moving",
        "AFK" => "Idle",
        _ => string.IsNullOrWhiteSpace(a) ? "Idle" : a,
    };

    private static string MapArtKey(string mapId)
    {
        var sb = new StringBuilder("map_");
        foreach (var c in mapId.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    private static Dictionary<string, string>? _id2disp;
    private static Dictionary<string, (string id, string disp)>? _norm;

    private static (string id, string disp) ResolveMap(string raw)
    {
        EnsureMaps();
        raw = (raw ?? "").Trim();
        if (raw.Length == 0 || _id2disp == null || _norm == null) return ("", "");
        if (_id2disp.Count == 0)
        {
            string n0 = Norm(raw);
            return (n0.Length >= 3 && n0.Length <= 24) ? (raw, raw) : ("", "");
        }
        if (_id2disp.TryGetValue(raw, out var d0)) return (raw, d0.Length > 0 ? d0 : raw);
        if (_norm.TryGetValue(Norm(raw), out var hit)) return (hit.id, hit.disp.Length > 0 ? hit.disp : hit.id);
        return ("", "");
    }

    private static void EnsureMaps()
    {
        if (_id2disp != null) return;
        _id2disp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _norm = new Dictionary<string, (string id, string disp)>(StringComparer.Ordinal);
        try
        {
            string bd = AppContext.BaseDirectory;
            foreach (var dir in new[] { Path.Combine(bd, "OcrServer", "models", "icons"), Path.Combine(bd, "models", "icons") })
            {
                var jf = Path.Combine(dir, "map_names.json");
                if (!File.Exists(jf)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(jf));
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    string id = p.Name, disp = p.Value.GetString() ?? "";
                    _id2disp[id] = disp;
                    _norm[Norm(id)] = (id, disp);
                    if (disp.Length > 0) _norm[Norm(disp)] = (id, disp);
                }
                break;
            }
        }
        catch { }
    }

    private static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
