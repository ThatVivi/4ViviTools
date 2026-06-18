using FourRVivi.Core.Discord;

namespace FourRVivi.App.Services;

/// <summary>Polls a presence provider and pushes it to Discord on an interval.</summary>
public sealed class DiscordPresenceUpdater : IDisposable
{
    private readonly DiscordService _discord;
    private System.Threading.Timer? _timer;
    private Func<RoPresence?>? _provider;

    public DiscordPresenceUpdater(DiscordService discord) => _discord = discord;

    public void Start(string appId, Func<RoPresence?> provider, int intervalSeconds = 5)
    {
        _provider = provider;
        _discord.Connect(appId);
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => Tick(), null, 250, Math.Max(1, intervalSeconds) * 1000);
    }

    private void Tick()
    {
        try
        {
            var p = _provider?.Invoke();
            if (p != null) _discord.Apply(p);
        }
        catch { }
    }

    /// <summary>Stop polling and clear any shown presence (used when the user disables Discord).</summary>
    public void StopAndClear()
    {
        _timer?.Dispose(); _timer = null;
        _discord.Clear();
        _discord.Dispose();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _discord.Dispose();
    }
}
