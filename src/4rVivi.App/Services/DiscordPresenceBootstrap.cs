using FourRVivi.Core.Discord;
using FourRVivi.Core.Game;
using FourRVivi.Core.Settings;

namespace FourRVivi.App.Services;

/// <summary>Wires the Discord presence updater to the live game state, using the runtime
/// settings (enable + app id + site). Called at startup and again when the user hits
/// "Apply" on the Settings page, so toggling Discord takes effect without a restart.</summary>
public static class DiscordPresenceBootstrap
{
    /// <summary>Built-in default Discord Application ID so RPC works with zero setup.</summary>
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
                // Not attached yet — still show a presence so the user sees it working.
                return new RoPresence
                {
                    ServerName = s.DiscordServerName,
                    WebsiteUrl = s.DiscordWebsiteUrl,
                    Activity = "In menus",
                    LargeImageKey = "logo",
                };
            }
            return new RoPresence
            {
                CharName = cs.Name,
                ClassName = cs.ClassName,
                BaseLevel = cs.BaseLevel,
                JobLevel = cs.JobLevel,
                MapName = cs.MapName,
                X = cs.X,
                Y = cs.Y,
                HpPct = cs.HpPct,
                SpPct = cs.SpPct,
                Hp = cs.Hp, MaxHp = cs.MaxHp, Sp = cs.Sp, MaxSp = cs.MaxSp,
                BaseExpPct = cs.BaseExpPct, JobExpPct = cs.JobExpPct,
                Activity = cs.Activity,
                ServerName = s.DiscordServerName,
                WebsiteUrl = s.DiscordWebsiteUrl,
                LargeImageKey = "logo",
            };
        }, 2);
    }
}
