// Fallback.cs — "a fallback for everything" framework for 4rVivi.
// Generic provider-chain: try the best source, fall through to alternatives until one works.
// Used for HP/SP reading (multiple memory vectors -> pixel), target detection, capture, etc.
// .NET Framework 4.x. MIT. Private-server use only.

using System;
using System.Collections.Generic;
using System.Drawing;
using _4rVivi.Utils;

namespace _4rVivi.Core
{
    /// <summary>A source that may or may not currently work. Returns false if it can't produce a value.</summary>
    public interface ISource<T>
    {
        string Name { get; }
        bool TryGet(out T value);
    }

    /// <summary>Tries sources in priority order; remembers the last good one to avoid re-probing.</summary>
    public sealed class Chain<T>
    {
        private readonly List<ISource<T>> _sources = new List<ISource<T>>();
        private int _lastGood = -1;
        public string ActiveSource { get; private set; } = "(none)";

        public Chain<T> Add(ISource<T> s) { _sources.Add(s); return this; }

        public bool TryGet(out T value)
        {
            // prefer the source that worked last tick (cheap, stable)
            if (_lastGood >= 0 && _sources[_lastGood].TryGet(out value))
            { ActiveSource = _sources[_lastGood].Name; return true; }

            for (int i = 0; i < _sources.Count; i++)
            {
                if (i == _lastGood) continue;
                if (_sources[i].TryGet(out value))
                { _lastGood = i; ActiveSource = _sources[i].Name; return true; }
            }
            value = default(T);
            ActiveSource = "(all failed)";
            return false;
        }
    }

    // ===================================================================
    // HP / SP READING — "MULTIPLE HP READ VECTOR" + MEMORY + PIXEL fallback.
    // ===================================================================

    /// <summary>Reads an HP% (0..100) from one memory address (static or pointer-chain).</summary>
    public sealed class MemoryPercentSource : ISource<int>
    {
        private readonly MemoryEngine _m;
        private readonly Func<IntPtr> _resolveAddr;   // re-resolve each call (pointer-safe)
        private readonly bool _isPercent;             // some clients store %, others current/max
        private readonly Func<IntPtr> _resolveMaxAddr; // optional, when storing current+max
        public string Name { get; }

        public MemoryPercentSource(string name, MemoryEngine m, Func<IntPtr> resolveAddr,
                                   bool isPercent = true, Func<IntPtr> resolveMaxAddr = null)
        { Name = name; _m = m; _resolveAddr = resolveAddr; _isPercent = isPercent; _resolveMaxAddr = resolveMaxAddr; }

        public bool TryGet(out int pct)
        {
            pct = -1;
            try
            {
                IntPtr a = _resolveAddr();
                if (a == IntPtr.Zero) return false;
                int cur = _m.ReadInt32(a);
                if (_isPercent) { if (cur < 0 || cur > 100) return false; pct = cur; return true; }
                if (_resolveMaxAddr == null) return false;
                int max = _m.ReadInt32(_resolveMaxAddr());
                if (max <= 0 || cur < 0 || cur > max) return false;
                pct = (int)(100.0 * cur / max);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Reads HP% by measuring the fill ratio of a coloured bar on screen (pixel fallback).
    /// Calibrate BarRect (client coords) + the fill colour once per UI theme.</summary>
    public sealed class PixelBarSource : ISource<int>
    {
        private readonly Func<Bitmap> _capture;   // window capture provider
        public Rectangle BarRect;                 // where the HP bar lives in the client
        public Color FillColor = Color.FromArgb(0xC8, 0x30, 0x30);
        public int Tolerance = 60;
        public string Name { get; }

        public PixelBarSource(string name, Func<Bitmap> capture) { Name = name; _capture = capture; }

        public bool TryGet(out int pct)
        {
            pct = -1;
            Bitmap bmp = null;
            try
            {
                bmp = _capture();
                if (bmp == null) return false;
                if (BarRect.Width <= 0 || BarRect.Right > bmp.Width || BarRect.Bottom > bmp.Height) return false;
                int y = BarRect.Y + BarRect.Height / 2;        // sample the bar's mid-line
                int filled = 0;
                for (int x = BarRect.X; x < BarRect.Right; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (Near(c, FillColor, Tolerance)) filled++;
                }
                pct = (int)(100.0 * filled / BarRect.Width);
                return pct >= 0 && pct <= 100;
            }
            catch { return false; }
            finally { bmp?.Dispose(); }
        }

        private static bool Near(Color a, Color b, int t)
            => Math.Abs(a.R - b.R) <= t && Math.Abs(a.G - b.G) <= t && Math.Abs(a.B - b.B) <= t;
    }

    /// <summary>Convenience: build an HP read chain = N memory vectors then a pixel fallback.
    /// "MULTIPLE HP READ VECTOR" — try several known offsets/pointer-chains before pixel.</summary>
    public static class HpReadChainFactory
    {
        public static Chain<int> Build(IEnumerable<ISource<int>> memoryVectors, PixelBarSource pixelFallback)
        {
            var chain = new Chain<int>();
            foreach (var v in memoryVectors) chain.Add(v);
            if (pixelFallback != null) chain.Add(pixelFallback);
            return chain;
        }
    }
}
