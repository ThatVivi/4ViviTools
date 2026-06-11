// BotAdvanced.cs — production features layered on BotEngine for 4rVivi.
// Waypoint routes, session stats, anti-GM guard, Discord alerts, return-to-town,
// and a semi-automatic actor-table (EntityLayout) finder.
// .NET Framework 4.x. NuGet: Newtonsoft.Json. MIT. Private-server use only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using _4rVivi.Utils;

namespace _4rVivi.Bot
{
    // =====================================================================
    // 1) WAYPOINT ROUTES — record click-move points, replay them to roam a map.
    //    Lets the bot cover a farming circuit instead of wandering blindly.
    // =====================================================================
    public sealed class WaypointRoute
    {
        public List<System.Drawing.Point> Points = new List<System.Drawing.Point>();
        public bool Loop = true;
        private int _i;

        public System.Drawing.Point Next()
        {
            if (Points.Count == 0) return System.Drawing.Point.Empty;
            var p = Points[_i % Points.Count];
            _i++;
            if (!Loop && _i >= Points.Count) _i = Points.Count - 1;
            return p;
        }

        public void Save(string path) => File.WriteAllText(path,
            Newtonsoft.Json.JsonConvert.SerializeObject(this));
        public static WaypointRoute Load(string path) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<WaypointRoute>(File.ReadAllText(path));
    }

    // =====================================================================
    // 2) SESSION STATS — exp/hr, zeny/hr, kills, drops. Read deltas from memory.
    // =====================================================================
    public sealed class SessionStats
    {
        private readonly DateTime _start = DateTime.UtcNow;
        public long StartExp, CurrentExp, StartZeny, CurrentZeny;
        public int Kills;
        public readonly Dictionary<string, int> Drops = new Dictionary<string, int>();

        public double Hours => Math.Max(1e-6, (DateTime.UtcNow - _start).TotalHours);
        public long ExpPerHour => (long)((CurrentExp - StartExp) / Hours);
        public long ZenyPerHour => (long)((CurrentZeny - StartZeny) / Hours);
        public double KillsPerHour => Kills / Hours;

        public void AddDrop(string item) { Drops[item] = Drops.TryGetValue(item, out var n) ? n + 1 : 1; }
        public override string ToString() =>
            $"Time {Hours:F1}h | Kills {Kills} ({KillsPerHour:F0}/h) | EXP {ExpPerHour:N0}/h | Zeny {ZenyPerHour:N0}/h";
    }

    // =====================================================================
    // 3) ANTI-GM GUARD — the single most important safety feature for botting.
    //    Stops the bot if: a GM-class actor appears in the entity list, a private
    //    message/whisper arrives, or an unexpected dialog pops. Wire the detectors
    //    to your memory reads; defaults are conservative.
    // =====================================================================
    public sealed class AntiGmGuard
    {
        public Func<bool> GmNearby;        // e.g. scan entity list for GM job ids / names
        public Func<bool> WhisperReceived; // e.g. chat buffer changed with "[whisper]" marker
        public Func<bool> UnexpectedDialog;
        public Action OnTrip;              // stop bot + optional logout / alert

        private bool _tripped;
        public bool Tripped => _tripped;

        public void Check()
        {
            if (_tripped) return;
            bool danger = (GmNearby?.Invoke() ?? false)
                       || (WhisperReceived?.Invoke() ?? false)
                       || (UnexpectedDialog?.Invoke() ?? false);
            if (danger) { _tripped = true; OnTrip?.Invoke(); }
        }
        public void Reset() => _tripped = false;
    }

    // =====================================================================
    // 4) DISCORD WEBHOOK — alerts for rare drops, death, GM trip, session summary.
    // =====================================================================
    public sealed class DiscordWebhook
    {
        private readonly string _url;
        public DiscordWebhook(string webhookUrl) { _url = webhookUrl; ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
        public void Send(string content)
        {
            if (string.IsNullOrEmpty(_url)) return;
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { content });
                    wc.UploadString(_url, "POST", body);
                }
            }
            catch { /* alerts are best-effort, never crash the bot */ }
        }
    }

    // =====================================================================
    // 5) RETURN-TO-TOWN — emergency / weight-full action. Use butterfly wing key
    //    or a saved warp; fall back to @go / login-warp if configured.
    // =====================================================================
    public sealed class ReturnToTown
    {
        private readonly InputSender _input;
        private readonly HumanizedTiming _t;
        public int ButterflyWingVk;     // bound hotkey for the wing
        public Action CustomReturn;     // e.g. type "@go 0" sequence

        public ReturnToTown(InputSender input, HumanizedTiming t) { _input = input; _t = t; }
        public void Go()
        {
            if (CustomReturn != null) { CustomReturn(); return; }
            if (ButterflyWingVk != 0) { _input.PressVk(ButterflyWingVk, _t); }
        }
    }

    // =====================================================================
    // 6) ENTITY-LAYOUT FINDER — semi-automatic discovery of the RO actor table.
    //    Workflow (Cheat-Engine-style, but in-app):
    //      a) Use the Memory Scanner to find ONE mob's HP address (you know its hp).
    //      b) Pass that address here. The finder backtracks to the record start by
    //         testing candidate field offsets, then derives stride from a 2nd mob.
    //    This removes most of the manual offset work; you still confirm the result.
    // =====================================================================
    public sealed class EntityLayoutFinder
    {
        private readonly MemoryEngine _m;
        public EntityLayoutFinder(MemoryEngine engine) { _m = engine; }

        /// <summary>Given a known HP field address + the mob's id, find the record base by
        /// searching a small window before the HP address for the id value.</summary>
        public bool TryFindRecord(IntPtr hpFieldAddr, int knownMobId, int searchBackBytes,
                                  out IntPtr recordBase, out int idOffset, out int hpOffset)
        {
            recordBase = IntPtr.Zero; idOffset = -1; hpOffset = -1;
            for (int back = 0; back <= searchBackBytes; back += 4)
            {
                IntPtr cand = (IntPtr)((long)hpFieldAddr - back);
                // does the id live at the start of this candidate record?
                if (_m.ReadInt32(cand) == knownMobId)
                {
                    recordBase = cand;
                    idOffset = 0;
                    hpOffset = back;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Stride = distance in bytes between two adjacent actor records.
        /// Find a second mob's id address near the first; the delta is the stride.</summary>
        public int DeriveStride(IntPtr firstRecordBase, int secondMobId, int maxStrideScan)
        {
            for (int s = 4; s <= maxStrideScan; s += 4)
            {
                IntPtr cand = (IntPtr)((long)firstRecordBase + s);
                if (_m.ReadInt32(cand) == secondMobId) return s;
            }
            return 0; // not found — records may be pointer-indirected; fall back to manual
        }
    }

    // =====================================================================
    // 7) HOOKS to plug the above into BotEngine. Extend BotConfig with these and
    //    call them from the engine tick (Safety stage runs guard.Check() first).
    // =====================================================================
    public sealed class BotServices
    {
        public WaypointRoute Route;
        public SessionStats Stats = new SessionStats();
        public AntiGmGuard Guard = new AntiGmGuard();
        public DiscordWebhook Discord;
        public ReturnToTown Town;
    }
}
