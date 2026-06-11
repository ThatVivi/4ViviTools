// FarmModules.cs — anti-stuck, autoloot, mob-search, cruise-control + feature flags for 4rVivi.
// Builds on BotEngine / InputSender / MemoryEngine / DataService. Read-only memory; input only.
// .NET Framework 4.x. MIT. Private-server use only (botting must be permitted by the server).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using _4rVivi.Bot;
using _4rVivi.Utils;

namespace _4rVivi.Farm
{
    // ===================================================================
    // FEATURE FLAGS — the buildable subset of the requested feature matrix.
    // (Anticheat-bypass, captcha/antibot solvers, wallhack-vision, and write-based
    //  nodelay are intentionally NOT here — see the feature-map doc.)
    // ===================================================================
    [Flags]
    public enum Mode { Normal = 0, Bot = 1, AutoAssist = 2 }

    public enum NavMode { TeleportSearch, WalkWaypoints, RecordedPath }
    public enum AtkMode { NearestFirst, ByElementWeakness, LowestHpFirst, ByMobIdPriority }
    public enum HpSource { MemoryThenPixel, MemoryOnly, PixelOnly }

    public sealed class FarmConfig
    {
        public Mode Mode = Mode.Bot;
        public NavMode Nav = NavMode.TeleportSearch;
        public AtkMode Atk = AtkMode.NearestFirst;
        public HpSource Hp = HpSource.MemoryThenPixel;

        public List<int> TargetMobIds = new List<int>();   // farm only these (empty = any)
        public List<int> AvoidMobIds = new List<int>();     // "AVOID DANGER MOBS"
        public bool AvoidPlayers = true;                     // "AVOID PLAYER"
        public string LockMap;                               // "GO BACK TO LOCK MAP"
        public int FallbackLeapSeconds = 6;                  // "FALLBACK LEAP": tp if no target seen
        public bool PhantomShiftOnHit = false;               // tp when damaged/hit
        public bool AutoSit = true;                          // sit to regen when safe
        public bool AutoRefresh = false;                     // periodic refresh (jitter de-sync)

        public int TeleportVk = 0x73;                        // F4 fly-wing/@tele bound key
        public int SitVk = 0x2D;                             // Insert (sit/stand) - server dependent
        public int AttackVk = 0x20;
        public int[] SkillRotationVk = new int[0];
        public int LootVk = 0x7A;
    }

    // ===================================================================
    // ANTI-STUCK — anti-sit, anti-stop, anti-cast-stuck, anti-crowd.
    // Detect "nothing changed for too long" and nudge the character.
    // ===================================================================
    public sealed class AntiStuck
    {
        private readonly Func<Point> _readPlayerCell;   // memory: player map cell
        private readonly Func<int> _readTargetCount;     // entity provider: nearby mobs
        private readonly InputSender _input;
        private readonly HumanizedTiming _t;

        private Point _lastCell;
        private DateTime _lastMove = DateTime.UtcNow;
        public int StuckSeconds = 4;
        public int CrowdThreshold = 6;
        public int TeleportVk;
        public int MoveNudgeRange = 120;
        public IntPtr GameWindow;
        public Func<Point> WindowCenter;   // supply window centre in client coords (e.g. () => new Point(w/2,h/2))

        public AntiStuck(Func<Point> readPlayerCell, Func<int> readTargetCount, InputSender input, HumanizedTiming t)
        { _readPlayerCell = readPlayerCell; _readTargetCount = readTargetCount; _input = input; _t = t; }

        /// <summary>Call each tick. Returns true if it took a corrective action this tick.</summary>
        public bool Check()
        {
            var cell = _readPlayerCell();
            if (cell != _lastCell) { _lastCell = cell; _lastMove = DateTime.UtcNow; }

            // anti-crowd: too many mobs piling on -> teleport out
            if (_readTargetCount() >= CrowdThreshold && TeleportVk != 0)
            { _input.PressVk(TeleportVk, _t); _lastMove = DateTime.UtcNow; return true; }

            // anti-stop / anti-cast-stuck / anti-sit: no movement for too long -> nudge or tp
            if ((DateTime.UtcNow - _lastMove).TotalSeconds >= StuckSeconds)
            {
                if (TeleportVk != 0) _input.PressVk(TeleportVk, _t);
                else Nudge();
                _lastMove = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        private readonly Random _rng = new Random();
        private void Nudge()
        {
            if (WindowCenter == null) return;   // no centre supplied -> teleport path handles it
            var c = WindowCenter();
            _input.ClickMove(GameWindow,
                c.X + _rng.Next(-MoveNudgeRange, MoveNudgeRange),
                c.Y + _rng.Next(-MoveNudgeRange, MoveNudgeRange), _t);
        }
    }

    // ===================================================================
    // AUTOLOOT — manual pick (loot key around player) or Greed skill.
    // ===================================================================
    public sealed class AutoLoot
    {
        private readonly InputSender _input;
        private readonly HumanizedTiming _t;
        public int LootVk;            // bound pick-up key (manual mode)
        public int GreedVk;           // bound Greed skill key (0 = disabled)
        public bool UseGreed;
        public int PickPasses = 3;    // how many loot presses per kill

        public AutoLoot(InputSender input, HumanizedTiming t) { _input = input; _t = t; }

        public void LootNow()
        {
            if (UseGreed && GreedVk != 0) { _input.PressVk(GreedVk, _t); return; }
            for (int i = 0; i < PickPasses; i++)
            {
                _input.PressVk(LootVk, _t);
                Thread.Sleep(_t.HoldMs(40, 90));
            }
        }
    }

    // ===================================================================
    // MOBSEARCH — teleport until a wanted mob is in range. Uses the entity
    // provider for "is it here?" and the rAthena DataService for name->id.
    // ===================================================================
    public sealed class MobSearch
    {
        private readonly ITargetProvider _provider;
        private readonly InputSender _input;
        private readonly HumanizedTiming _t;
        public IntPtr GameWindow;
        public int TeleportVk;
        public HashSet<int> WantedMobIds = new HashSet<int>();
        public int TeleportSettleMs = 700;   // wait for map/actors to load after tp

        public MobSearch(ITargetProvider provider, InputSender input, HumanizedTiming t)
        { _provider = provider; _input = input; _t = t; }

        /// <summary>Returns true when a wanted mob is present (stop searching, start attacking).</summary>
        public bool SearchStep()
        {
            _provider.Update(GameWindow);
            foreach (var tgt in _provider.Targets)
                if (WantedMobIds.Count == 0 || WantedMobIds.Contains(tgt.Id)) return true;

            // none here -> teleport and let the map settle
            if (TeleportVk != 0) _input.PressVk(TeleportVk, _t);
            Thread.Sleep(TeleportSettleMs + _t.ReactionDelay(0, 300));
            return false;
        }
    }

    // ===================================================================
    // CRUISE CONTROL — the farming loop that ties it together.
    // Search -> attack (rotation) -> loot -> safety/anti-stuck -> lock-map guard.
    // Runs on its own thread; HP-safety + anti-GM are enforced by the host BotEngine/guard.
    // ===================================================================
    public sealed class CruiseControl
    {
        private readonly FarmConfig _cfg;
        private readonly ITargetProvider _provider;
        private readonly InputSender _input;
        private readonly HumanizedTiming _t;
        private readonly AutoLoot _loot;
        private readonly AntiStuck _antiStuck;
        private readonly MobSearch _search;

        public Func<int> ReadHpPercent;          // from the fallback Chain<int>
        public Func<string> ReadCurrentMap;       // memory: current map name (for lock-map)
        public Func<bool> WasHitRecently;          // for PHANTOM SHIFT
        public Action OnReturnToLockMap;            // walk/tp back when off the locked map
        public IntPtr GameWindow;

        private Thread _thread; private volatile bool _running; private int _rot;
        private DateTime _lastTargetSeen = DateTime.UtcNow;

        public CruiseControl(FarmConfig cfg, ITargetProvider provider, InputSender input,
                             HumanizedTiming t, AutoLoot loot, AntiStuck antiStuck, MobSearch search)
        { _cfg = cfg; _provider = provider; _input = input; _t = t; _loot = loot; _antiStuck = antiStuck; _search = search; }

        public void Start() { if (_running) return; _running = true; _thread = new Thread(Loop) { IsBackground = true }; _thread.Start(); }
        public void Stop() => _running = false;

        private void Loop()
        {
            while (_running)
            {
                try { Tick(); } catch { }
                Thread.Sleep(_t.NextDelay());
            }
        }

        private void Tick()
        {
            // 1) lock-map guard: warped off the farm map -> go back
            if (!string.IsNullOrEmpty(_cfg.LockMap) && ReadCurrentMap != null &&
                !string.Equals(ReadCurrentMap(), _cfg.LockMap, StringComparison.OrdinalIgnoreCase))
            { OnReturnToLockMap?.Invoke(); return; }

            // 2) phantom shift: took damage -> bail
            if (_cfg.PhantomShiftOnHit && (WasHitRecently?.Invoke() ?? false) && _cfg.TeleportVk != 0)
            { _input.PressVk(_cfg.TeleportVk, _t); return; }

            // 3) anti-stuck (anti-sit/stop/cast/crowd)
            if (_antiStuck.Check()) return;

            // 4) find a target
            _provider.Update(GameWindow);
            var target = PickTarget();
            if (target == null)
            {
                // FALLBACK LEAP: no target for X seconds -> teleport search
                if ((DateTime.UtcNow - _lastTargetSeen).TotalSeconds >= _cfg.FallbackLeapSeconds)
                { _search.SearchStep(); }
                return;
            }
            _lastTargetSeen = DateTime.UtcNow;

            // 5) attack + rotation, then loot
            _input.LeftClick(GameWindow, target.Value.ScreenX, target.Value.ScreenY, _t);
            if (_cfg.SkillRotationVk.Length > 0)
            { _input.PressVk(_cfg.SkillRotationVk[_rot++ % _cfg.SkillRotationVk.Length], _t); }
            else { _input.PressVk(_cfg.AttackVk, _t); }
            Thread.Sleep(_t.ReactionDelay(120, 280));
            _loot.LootNow();
        }

        private Target? PickTarget()
        {
            Target? best = null; double bestScore = double.MaxValue;
            var player = _provider.PlayerScreen;
            foreach (var tg in _provider.Targets)
            {
                if (_cfg.AvoidMobIds.Contains(tg.Id)) continue;
                if (_cfg.TargetMobIds.Count > 0 && !_cfg.TargetMobIds.Contains(tg.Id)) continue;
                double d = tg.DistanceTo(player.X, player.Y);
                double score = _cfg.Atk == AtkMode.LowestHpFirst && tg.Hp >= 0 ? tg.Hp : d;
                if (score < bestScore) { bestScore = score; best = tg; }
            }
            return best;
        }
    }
}
