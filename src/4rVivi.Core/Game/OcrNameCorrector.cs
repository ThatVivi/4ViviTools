using System;
using System.Collections.Generic;
using System.Linq;

namespace FourRVivi.Core.Game;

/// <summary>Snaps a fuzzy OCR text read to the nearest valid game string (class / map / monster / item /
/// skill) from our embedded data. This is the "dictionary correction" layer — the highest-leverage OCR
/// accuracy win (see docs/rathena/ocr.md). Per-role dictionaries; best normalized-edit-distance match
/// above a confidence threshold, else the raw text is returned unchanged.</summary>
public sealed class OcrNameCorrector
{
    // role (lowercased) -> candidate strings
    private readonly Dictionary<string, List<(string norm, string orig)>> _dict = new();

    public void SetDictionary(string role, IEnumerable<string> candidates)
    {
        var list = candidates.Where(c => !string.IsNullOrWhiteSpace(c))
                             .Select(c => (Normalize(c), c)).Where(t => t.Item1.Length > 0)
                             .GroupBy(t => t.Item1).Select(g => g.First()).ToList();
        _dict[role.ToLowerInvariant()] = list;
    }

    /// <summary>Correct a raw OCR read for a role. Returns the best dictionary match if close enough,
    /// otherwise the trimmed raw text.</summary>
    public string Correct(string role, string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return raw;
        if (!_dict.TryGetValue(role.ToLowerInvariant(), out var cands) || cands.Count == 0) return raw;

        var rn = Normalize(raw);
        if (rn.Length == 0) return raw;

        string best = raw; int bestDist = int.MaxValue;
        foreach (var (norm, orig) in cands)
        {
            // quick reject on length gap
            if (Math.Abs(norm.Length - rn.Length) > Math.Max(3, rn.Length / 2)) continue;
            int d = Levenshtein(rn, norm, bestDist);
            if (d < bestDist) { bestDist = d; best = orig; if (d == 0) break; }
        }
        // accept only if the match is close (≤ 35% of the longer length)
        double tol = Math.Max(rn.Length, Normalize(best).Length) * 0.35;
        return bestDist <= tol ? best : raw;
    }

    public bool HasRole(string role) => _dict.ContainsKey(role.ToLowerInvariant());

    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static int Levenshtein(string a, string b, int cap)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m; if (m == 0) return n;
        var prev = new int[m + 1]; var cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i; int rowMin = cur[0];
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > cap) return cap + 1;   // early-out
            (prev, cur) = (cur, prev);
        }
        return prev[m];
    }
}
