// BotEngine.cs — farming/combat bot core for 4rVivi.
// Memory-first hybrid (recommended), with a screen-detection fallback path.
// .NET Framework 4.x, x86. Drop into 4rVivi/Bot/.
//
// ARCHITECTURE (read this before wiring):
//   ITargetProvider  -> gives the bot a list of attackable targets each tick.
//        * MemoryTargetProvider  : reads the client's actor/entity table (ROBUST, preferred).
//                                  Needs per-client offsets for the actor list (found via the
//                                  Memory Scanner the same way HP was found).
//        * ScreenTargetProvider  : BitBlt the game window + template/colour match mob sprites
//                                  or minimap blips (fragile, but works with zero offsets).
//   InputSender      -> humanised key/mouse output (jitter via HumanizedTiming).
//   BotEngine        -> the state machine that ties HP-safety + target loop together.
//
// WHY memory-first: OCR / sprite matching drifts (camera zoom, weather, mob density, GPU
// scaling) and misclicks. The entity table gives exact cell coords + mob id + hp, so pathing
// and target selection are deterministic. Use screen detection only when offsets are unknown.
//
// SCOPE: this is a working skeleton + working primitives (capture, input, state machine).
// The two TODO blocks (entity-table layout, sprite templates) are server/client specific and
// must be filled via the scanner / your own template captures. It will not farm out-of-the-box.
//
// SAFETY: full pathing+combat automation is the most ban-prone feature here. Private servers
// only. Keep movement humanised and add idle/anti-AFK randomisation before any real use.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using _4rVivi.Utils;

namespace _4rVivi.Bot
{
    // ---------- shared model ----------
    public struct Target
    {
        public int Id;            // mob/actor id from memory (0 if screen-detected)
        public int CellX, CellY;  // map cell coords (memory mode)
        public int ScreenX, ScreenY; // pixel coords inside the game window (both modes)
        public int Hp;            // -1 if unknown
        public double DistanceTo(int px, int py)
            => Math.Sqrt(Math.Pow(ScreenX - px, 2) + Math.Pow(ScreenY - py, 2));
    }

    public interface ITargetProvider
    {
        // Player position in window pixels (for distance sorting / click-to-move).
        void Update(IntPtr gameWindow);
        IReadOnlyList<Target> Targets { get; }
        Point PlayerScreen { get; }   // usually window centre, but provider may refine it
    }

    // ====================================================================
    // MEMORY PROVIDER (preferred). Fill the entity-table layout for your client.
    // ====================================================================
    public sealed class MemoryTargetProvider : ITargetProvider
    {
        private readonly MemoryEngine _m;
        private readonly EntityLayout _layout;
        private readonly List<Target> _targets = new List<Target>();
        public IReadOnlyList<Target> Targets => _targets;
        public Point PlayerScreen { get; private set; }

        public MemoryTargetProvider(MemoryEngine engine, EntityLayout layout)
        { _m = engine; _layout = layout; }

        // The RO actor list is an array/linked set of actor structs. Layout differs per client;
        // discover base + struct stride + field offsets with the Memory Scanner, then fill this.
        public void Update(IntPtr gameWindow)
        {
            _targets.Clear();
            if (_layout == null || _layout.ListBase == IntPtr.Zero) return;

            int count = _m.ReadInt32((IntPtr)((long)_layout.ListBase + _layout.CountOffset));
            count = Math.Max(0, Math.Min(count, _layout.MaxActors));

            for (int i = 0; i < count; i++)
            {
                IntPtr rec = (IntPtr)((long)_layout.ListBase + _layout.FirstOffset + (long)i * _layout.Stride);
                int type = _m.ReadInt32((IntPtr)((long)rec + _layout.TypeOffset));
                if (type != _layout.MonsterTypeValue) continue;   // only mobs

                var t = new Target
                {
                    Id = _m.ReadInt32((IntPtr)((long)rec + _layout.IdOffset)),
                    CellX = _m.ReadInt32((IntPtr)((long)rec + _layout.PosXOffset)),
                    CellY = _m.ReadInt32((IntPtr)((long)rec + _layout.PosYOffset)),
                    Hp = _layout.HpOffset >= 0 ? _m.ReadInt32((IntPtr)((long)rec + _layout.HpOffset)) : -1
                };
                if (t.Hp == 0) continue; // dead/looted
                _targets.Add(t);
            }
            PlayerScreen = WindowCentre(gameWindow);
            // NOTE: convert CellX/CellY -> ScreenX/ScreenY requires the camera transform.
            // Simplest reliable approach: read the player's own cell, compute delta cells,
            // multiply by on-screen cell pixel size (calibrated once per zoom level).
        }

        private static Point WindowCentre(IntPtr hwnd)
        {
            if (NativeWin.GetClientRect(hwnd, out var r))
                return new Point(r.Width / 2, r.Height / 2);
            return new Point(0, 0);
        }
    }

    public sealed class EntityLayout   // <-- fill via scanner per client
    {
        public IntPtr ListBase = IntPtr.Zero;
        public int CountOffset, FirstOffset, Stride;
        public int TypeOffset, IdOffset, PosXOffset, PosYOffset;
        public int HpOffset = -1;
        public int MonsterTypeValue = 5; // RO: blocktype for mobs (verify per client)
        public int MaxActors = 256;
    }

    // ====================================================================
    // SCREEN PROVIDER (fallback). Zero offsets, but fragile. Baseline = colour-blob
    // detection on the minimap; upgrade to template matching with OpenCvSharp.
    // ====================================================================
    public sealed class ScreenTargetProvider : ITargetProvider
    {
        private readonly List<Target> _targets = new List<Target>();
        public IReadOnlyList<Target> Targets => _targets;
        public Point PlayerScreen { get; private set; }

        // Colour key for mob blips on the minimap (calibrate per server UI).
        public Color MobBlipColor = Color.FromArgb(255, 0, 0);
        public int ColorTolerance = 40;

        public void Update(IntPtr gameWindow)
        {
            _targets.Clear();
            using (var bmp = ScreenCapture.CaptureWindow(gameWindow))
            {
                if (bmp == null) return;
                PlayerScreen = new Point(bmp.Width / 2, bmp.Height / 2);
                // BASELINE detector: scan for blip-coloured pixels, cluster them.
                // (Replace with OpenCvSharp MatchTemplate against mob sprite crops for accuracy.)
                foreach (var p in ScreenCapture.FindColorBlobs(bmp, MobBlipColor, ColorTolerance, minBlob: 6))
                    _targets.Add(new Target { Id = 0, ScreenX = p.X, ScreenY = p.Y, Hp = -1 });
            }
        }
    }

    // ====================================================================
    // BOT STATE MACHINE
    // ====================================================================
    public enum BotState { Idle, Safety, FindTarget, Approach, Attack, Loot, ReturnHome }

    public sealed class BotConfig
    {
        public IntPtr GameWindow;
        // safety
        public Func<int> ReadHpPercent;     // wire to your autopot HP read
        public int FleeHpPercent = 25;
        public Action OnEmergency;          // e.g. teleport / fly-wing macro
        // combat keys
        public int AttackVk = 0x20;         // space = attack nearest by default (server dependent)
        public int LootVk;                  // 0 = use auto-loot key; set if manual
        public int[] SkillRotationVk = new int[0];
        public int MaxApproachPx = 60;      // close enough to attack
        public int TickMs = 120;
    }

    public sealed class BotEngine
    {
        private readonly ITargetProvider _provider;
        private readonly BotConfig _cfg;
        private readonly InputSender _input;
        private readonly HumanizedTiming _timing;
        private Thread _thread;
        private volatile bool _running;
        private int _rotationIdx;

        public BotState State { get; private set; } = BotState.Idle;
        public event Action<BotState> StateChanged;

        public BotEngine(ITargetProvider provider, BotConfig cfg)
        {
            _provider = provider;
            _cfg = cfg;
            _input = new InputSender();
            _timing = new HumanizedTiming(baseDelayMs: cfg.TickMs, jitterSigmaMs: 35, floorMs: 50, microPauseChance: 0.03);
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public void Stop() { _running = false; Set(BotState.Idle); }

        private void Loop()
        {
            while (_running)
            {
                try { Tick(); }
                catch { /* never let the bot thread die on a transient read failure */ }
                Thread.Sleep(_timing.NextDelay());
            }
        }

        private void Tick()
        {
            // 1) safety always wins
            if (_cfg.ReadHpPercent != null && _cfg.ReadHpPercent() <= _cfg.FleeHpPercent)
            {
                Set(BotState.Safety);
                _cfg.OnEmergency?.Invoke();
                Thread.Sleep(_timing.ReactionDelay(200, 500));
                return;
            }

            _provider.Update(_cfg.GameWindow);
            var targets = _provider.Targets;
            if (targets.Count == 0) { Set(BotState.FindTarget); WanderStep(); return; }

            // 2) pick nearest live target
            var player = _provider.PlayerScreen;
            Target best = default; double bestD = double.MaxValue;
            foreach (var t in targets)
            {
                double d = t.DistanceTo(player.X, player.Y);
                if (d < bestD) { bestD = d; best = t; }
            }

            // 3) approach or attack
            if (bestD > _cfg.MaxApproachPx)
            {
                Set(BotState.Approach);
                _input.ClickMove(_cfg.GameWindow, best.ScreenX, best.ScreenY, _timing);
                return;
            }

            Set(BotState.Attack);
            AttackStep(best);
        }

        private void AttackStep(Target t)
        {
            // click target then run skill rotation (SP-gated rotation should be added upstream)
            _input.LeftClick(_cfg.GameWindow, t.ScreenX, t.ScreenY, _timing);
            if (_cfg.SkillRotationVk.Length > 0)
            {
                int vk = _cfg.SkillRotationVk[_rotationIdx % _cfg.SkillRotationVk.Length];
                _rotationIdx++;
                _input.PressVk(vk, _timing);
            }
            else
            {
                _input.PressVk(_cfg.AttackVk, _timing);
            }
            // loot pass
            if (_cfg.LootVk != 0)
            {
                Set(BotState.Loot);
                Thread.Sleep(_timing.ReactionDelay(120, 260));
                _input.PressVk(_cfg.LootVk, _timing);
            }
        }

        private readonly Random _rng = new Random();
        private void WanderStep()
        {
            // humanised wander: click a random nearby point to find new mobs.
            if (!NativeWin.GetClientRect(_cfg.GameWindow, out var r)) return;
            int cx = r.Width / 2, cy = r.Height / 2;
            int x = cx + _rng.Next(-180, 181);
            int y = cy + _rng.Next(-140, 141);
            _input.ClickMove(_cfg.GameWindow, x, y, _timing);
        }

        private void Set(BotState s)
        {
            if (State == s) return;
            State = s; StateChanged?.Invoke(s);
        }
    }
}
