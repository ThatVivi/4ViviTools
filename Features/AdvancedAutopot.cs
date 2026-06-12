// AdvancedAutopot.cs — sickpot-style autopot for 4rVivi. ADDITIVE (does not touch the
// original 4RTools autopot). Reads live HP/SP from the selected client and sends pot keys
// via the same PostMessage path 4RTools uses. namespace _4rVivi.Features. MIT.
//
// Per rule: percent OR flat threshold, key, reaction time (ms before potting), use delay
// (min ms between pots). HP1/HP2/SP1/SP2/YGG tiers supported from the UI.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Utils;

namespace _4rVivi.Features
{
    public class PotRule
    {
        public string Name;
        public Keys Key = Keys.None;
        public int Percent;      // 0..100, 0 = ignore
        public int Flat;         // raw value, 0 = ignore
        public bool IsSp;
        public int ReactionMs;   // wait before potting after threshold crossed
        public int UseDelayMs = 100; // min ms between pots (item cooldown)
        public bool Enabled = true;
        private DateTime _lastPot = DateTime.MinValue;

        public bool ShouldFire(uint curHp, uint maxHp, uint curSp, uint maxSp)
        {
            if (!Enabled || Key == Keys.None) return false;
            uint cur = IsSp ? curSp : curHp;
            uint max = IsSp ? maxSp : maxHp;
            bool below = false;
            if (Percent > 0 && max > 0) below |= ((ulong)cur * 100UL < (ulong)Percent * max);
            if (Flat > 0) below |= (cur < (uint)Flat);
            if (!below) return false;
            if ((DateTime.UtcNow - _lastPot).TotalMilliseconds < UseDelayMs) return false;
            return true;
        }
        public void MarkFired() { _lastPot = DateTime.UtcNow; }
    }

    public class AdvancedAutopot
    {
        public readonly List<PotRule> Rules = new List<PotRule>();
        public int PollMs = 20;
        public Action<string> Status;   // plain field so the UI can assign it

        private Thread _t;
        private volatile bool _run;
        public bool Running { get { return _run; } }

        public void Start()
        {
            if (_run) return;
            _run = true;
            _t = new Thread(Loop) { IsBackground = true };
            _t.Start();
        }
        public void Stop() { _run = false; }

        private void Loop()
        {
            while (_run)
            {
                try
                {
                    Client c = ClientSingleton.GetClient();
                    if (c != null && c.process != null && c.currentHPBaseAddress != 0)
                    {
                        uint chp = c.ReadCurrentHp(), mhp = c.ReadMaxHp(), csp = c.ReadCurrentSp(), msp = c.ReadMaxSp();
                        foreach (PotRule r in Rules)
                        {
                            if (r.ShouldFire(chp, mhp, csp, msp))
                            {
                                if (r.ReactionMs > 0) Thread.Sleep(r.ReactionMs);
                                SendKey(c, r.Key);
                                r.MarkFired();
                            }
                        }
                    }
                }
                catch { }
                Thread.Sleep(PollMs);
            }
        }

        private void SendKey(Client c, Keys k)
        {
            try
            {
                if (k == Keys.None) return;
                if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) return;
                Interop.PostMessage(c.process.MainWindowHandle, Constants.WM_KEYDOWN_MSG_ID, k, 0);
                Interop.PostMessage(c.process.MainWindowHandle, Constants.WM_KEYUP_MSG_ID, k, 0);
            }
            catch { }
        }
    }
}
