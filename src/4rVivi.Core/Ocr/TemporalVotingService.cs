using System;
using System.Collections.Generic;
using System.Linq;

namespace FourRVivi.Core.Ocr;

/// <summary>Guide §8 Stage 9 — Temporal Voting. Never trust a single OCR frame. Keeps the last N reads
/// per region and returns the majority value, so one bad frame (Payon, Payon, Poyon, Payon -> Payon)
/// cannot flip the overlay. This is the text-stability counterpart to confidence smoothing: it votes on
/// the STRING, not the score. Thread-safe; one ring buffer per key (role).</summary>
public sealed class TemporalVotingService
{
    public int Window { get; set; } = 20;            // guide: store last 20 frames
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<string>> _hist = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Push a new read for <paramref name="key"/> and return the current majority value.
    /// Empty/whitespace reads are ignored (do not pollute the buffer) but still return the standing winner.</summary>
    public string Vote(string key, string value)
    {
        key ??= "";
        value = (value ?? "").Trim();
        lock (_gate)
        {
            if (!_hist.TryGetValue(key, out var q)) { q = new Queue<string>(); _hist[key] = q; }
            if (value.Length > 0)
            {
                q.Enqueue(value);
                while (q.Count > Math.Max(1, Window)) q.Dequeue();
            }
            return Majority(q, value);
        }
    }

    /// <summary>Majority value in the buffer. Ties break toward the most recent sample.</summary>
    private static string Majority(Queue<string> q, string fallback)
    {
        if (q.Count == 0) return fallback;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in q) counts[v] = counts.TryGetValue(v, out var c) ? c + 1 : 1;
        int best = counts.Values.Max();
        // among the winners, pick the one seen most recently
        string winner = fallback;
        foreach (var v in q) if (counts[v] == best) winner = v; // last pass wins -> most recent
        return winner;
    }

    /// <summary>Confidence of the current winner (winner count / window fill) in [0,1] — useful for UI tinting.</summary>
    public double Agreement(string key)
    {
        lock (_gate)
        {
            if (!_hist.TryGetValue(key ?? "", out var q) || q.Count == 0) return 0;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var v in q) counts[v] = counts.TryGetValue(v, out var c) ? c + 1 : 1;
            return counts.Values.Max() / (double)q.Count;
        }
    }

    public void Reset(string? key = null)
    {
        lock (_gate) { if (key == null) _hist.Clear(); else _hist.Remove(key); }
    }
}
