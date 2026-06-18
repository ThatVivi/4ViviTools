using DiscordRPC;
using FourRVivi.Core.Discord;

namespace FourRVivi.App.Services;

/// <summary>Optional Discord Rich Presence. Reads class/level, map + X/Y, party and a server
/// button. Every call is wrapped so a missing/closed Discord client never throws into the app.</summary>
public sealed class DiscordService : IDisposable
{
    private DiscordRpcClient? _client;
    private DateTime _startedUtc = DateTime.UtcNow;
    private string _last = "";

    public bool IsConnected => _client is { IsInitialized: true };

    public void Connect(string appId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            Dispose();
            _client = new DiscordRpcClient(appId);
            _client.Initialize();
            _startedUtc = DateTime.UtcNow;
        }
        catch { _client = null; }
    }

    public void Apply(RoPresence p)
    {
        try
        {
            if (_client is null || _client.IsDisposed || p is null) return;

            // Always present something (mirrors a known-good RPC mod): fall back when no game data yet.
            string details = string.IsNullOrWhiteSpace(p.DetailsLine) ? "Playing Ragnarok Online" : p.DetailsLine;
            string state = p.StateLine;
            if (string.IsNullOrWhiteSpace(state)) state = string.IsNullOrWhiteSpace(p.Activity) ? "Idle" : p.Activity;

            string sig = $"{details}|{state}|{p.HpPct}|{p.SpPct}|{p.PartySize}/{p.PartyMax}|{p.LargeImageKey}|{p.SmallImageKey}";
            if (sig == _last) return;
            _last = sig;

            string who = !string.IsNullOrWhiteSpace(p.CharName) && !string.IsNullOrWhiteSpace(p.ServerName)
                ? $"{p.CharName} - {p.ServerName}"
                : (p.CharName + p.ServerName);
            string vitals = (p.HpPct > 0 || p.SpPct > 0) ? $"HP {p.HpPct}% SP {p.SpPct}%" : "";
            string largeText = string.Join(" | ", new[] { who, vitals }.Where(x => !string.IsNullOrWhiteSpace(x)));

            var rp = new RichPresence
            {
                Details = Clamp(details, 128),
                State = Clamp(state, 128),
                Timestamps = new Timestamps(_startedUtc),
                Assets = new Assets
                {
                    LargeImageKey = NullIfEmpty(p.LargeImageKey),
                    LargeImageText = Clamp(NullIfEmpty(largeText) ?? "", 128),
                    SmallImageKey = NullIfEmpty(p.SmallImageKey),
                    SmallImageText = Clamp(p.ClassName, 128),
                },
            };

            if (p.PartySize > 0 && p.PartyMax > 0)
                rp.Party = new Party { ID = "ro-party", Size = p.PartySize, Max = Math.Max(p.PartySize, p.PartyMax) };

            if (!string.IsNullOrWhiteSpace(p.WebsiteUrl))
                rp.Buttons = new[] { new Button { Label = "Join Server", Url = p.WebsiteUrl } };

            _client.SetPresence(rp);
        }
        catch { }
    }

    public void Update(string details, string state)
    {
        try { _client?.SetPresence(new RichPresence { Details = details, State = state, Timestamps = new Timestamps(_startedUtc) }); }
        catch { }
    }

    public void Clear() { try { _client?.ClearPresence(); _last = ""; } catch { } }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string Clamp(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
}
