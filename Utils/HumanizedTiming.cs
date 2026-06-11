// HumanizedTiming.cs — anti-detection timing helper for 4RTools loops.
// Replaces fixed Thread.Sleep(delay) with gaussian-jittered, non-periodic delays.
// Drop into 4RTools/Utils/. MIT. Private-server use only.
//
// Usage in a spam/autopot loop:
//   var t = new HumanizedTiming(baseDelayMs: 250, jitterSigmaMs: 30);
//   while (running) { DoAction(); Thread.Sleep(t.NextDelay()); }
//
// For key presses use HoldMs() to vary key-down -> key-up time.

using System;

namespace _4rVivi.Utils
{
    public sealed class HumanizedTiming
    {
        private readonly Random _rng = new Random(Guid.NewGuid().GetHashCode());
        private readonly int _baseDelay;
        private readonly double _sigma;
        private readonly int _floor;
        private readonly double _microPauseChance; // 0..1
        private int _lastDelay = -1;

        public HumanizedTiming(int baseDelayMs, double jitterSigmaMs = 25, int floorMs = 40,
                               double microPauseChance = 0.02)
        {
            _baseDelay = baseDelayMs;
            _sigma = jitterSigmaMs;
            _floor = floorMs;
            _microPauseChance = microPauseChance;
        }

        /// <summary>Gaussian-jittered delay; never repeats the previous value; rare longer pauses.</summary>
        public int NextDelay()
        {
            int delay;
            do
            {
                double g = _baseDelay + Gaussian() * _sigma;

                // Rare "human distraction" pause (1-3% of the time).
                if (_rng.NextDouble() < _microPauseChance)
                    g += _baseDelay * (1.5 + _rng.NextDouble() * 2.0);

                delay = (int)Math.Round(g);
                if (delay < _floor) delay = _floor;
            }
            while (delay == _lastDelay); // avoid perfectly periodic input

            _lastDelay = delay;
            return delay;
        }

        /// <summary>Reaction lag before acting on a threshold cross (e.g. autopot). 30-120 ms.</summary>
        public int ReactionDelay(int minMs = 30, int maxMs = 120)
            => minMs + _rng.Next(maxMs - minMs + 1);

        /// <summary>Key-down -> key-up hold time so presses aren't instantaneous. 25-70 ms.</summary>
        public int HoldMs(int minMs = 25, int maxMs = 70)
            => minMs + _rng.Next(maxMs - minMs + 1);

        // Box-Muller standard normal.
        private double Gaussian()
        {
            double u1 = 1.0 - _rng.NextDouble();
            double u2 = 1.0 - _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}
