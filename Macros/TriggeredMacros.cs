// TriggeredMacros.cs — condition->recording bindings for 4rVivi.
// Generalizes reconnect-login, auto-sell (overweight), auto-storage, auto-buy as
// "play this recording when this condition becomes true". One engine, many uses.
// .NET Framework 4.x. MIT. Private-server use only.

using System;
using System.Collections.Generic;
using System.Threading;

namespace _4rVivi.Macros
{
    /// <summary>A recording that fires when Condition() turns true (edge-triggered, with cooldown).</summary>
    public sealed class TriggeredMacro
    {
        public string Name;
        public MacroRecording Recording;
        public Func<bool> Condition;       // e.g. () => disconnected, () => weight >= 90
        public int CooldownMs = 5000;       // don't re-fire instantly
        public bool Enabled = true;

        private DateTime _lastFire = DateTime.MinValue;
        private bool _wasTrue;

        public bool ShouldFire()
        {
            bool now = Enabled && (Condition?.Invoke() ?? false);
            bool edge = now && !_wasTrue;                       // rising edge only
            _wasTrue = now;
            if (!edge) return false;
            if ((DateTime.UtcNow - _lastFire).TotalMilliseconds < CooldownMs) return false;
            _lastFire = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>Watches all triggers on a background thread; plays the matching recording.
    /// Reconnect, auto-sell, auto-storage and auto-buy are just registered triggers.</summary>
    public sealed class TriggeredMacroEngine
    {
        private readonly MacroPlayer _player;
        private readonly Credentials _creds;
        private readonly List<TriggeredMacro> _triggers = new List<TriggeredMacro>();
        private Thread _thread; private volatile bool _running;
        public int PollMs = 500;
        public event Action<string> Fired;   // name -> for UI/log

        public TriggeredMacroEngine(MacroPlayer player, Credentials creds)
        { _player = player; _creds = creds; }

        public void Add(TriggeredMacro t) => _triggers.Add(t);

        // ---- convenience registrations for the common cases ----

        /// <summary>Reconnect: when disconnected, replay the login recording. The recording should
        /// contain TypeUsername / TypePassword events where the credentials go.</summary>
        public void RegisterReconnect(Func<bool> isDisconnected, MacroRecording loginMacro, int cooldownMs = 15000)
            => Add(new TriggeredMacro { Name = "reconnect", Recording = loginMacro, Condition = isDisconnected, CooldownMs = cooldownMs });

        /// <summary>Auto-sell when overweight: replay a recording that opens the NPC sell flow.</summary>
        public void RegisterAutoSell(Func<int> readWeightPercent, int threshold, MacroRecording sellMacro, int cooldownMs = 30000)
            => Add(new TriggeredMacro { Name = "auto-sell", Recording = sellMacro,
                Condition = () => readWeightPercent() >= threshold, CooldownMs = cooldownMs });

        /// <summary>Auto-storage: replay a recording that deposits loot (e.g. @storage / Kafra flow).</summary>
        public void RegisterAutoStorage(Func<int> readWeightPercent, int threshold, MacroRecording storageMacro, int cooldownMs = 30000)
            => Add(new TriggeredMacro { Name = "auto-storage", Recording = storageMacro,
                Condition = () => readWeightPercent() >= threshold, CooldownMs = cooldownMs });

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }
        public void Stop() => _running = false;

        private void Loop()
        {
            while (_running)
            {
                foreach (var t in _triggers)
                {
                    try
                    {
                        if (t.ShouldFire())
                        {
                            Fired?.Invoke(t.Name);
                            // play synchronously so two triggers never fight over the keyboard
                            _player.Play(t.Recording, () => _creds.Username, () => _creds.GetPassword());
                        }
                    }
                    catch { /* a bad recording must not kill the watcher */ }
                }
                Thread.Sleep(PollMs);
            }
        }
    }

    // -------------------------------------------------------------------
    // Disconnect detection helper: the simplest reliable signal is that the
    // character-name memory address goes null/empty after a DC. Wire this to
    // your MemoryEngine name read (the nameAddress 4RTools already tracks).
    // -------------------------------------------------------------------
    public sealed class DisconnectDetector
    {
        private readonly Func<string> _readCharName;   // returns "" when logged out / at login screen
        private int _emptyStreak;
        public int ConfirmTicks = 4;   // require N consecutive empties to avoid false positives

        public DisconnectDetector(Func<string> readCharName) { _readCharName = readCharName; }

        public bool IsDisconnected()
        {
            string n = _readCharName?.Invoke() ?? "";
            if (string.IsNullOrWhiteSpace(n)) _emptyStreak++;
            else _emptyStreak = 0;
            return _emptyStreak >= ConfirmTicks;
        }
    }
}
