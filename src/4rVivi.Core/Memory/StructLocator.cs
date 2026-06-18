namespace FourRVivi.Core.Memory;

/// <summary>Given candidate address lists for several known values, finds the cluster (character
/// struct) where the most values sit close together, and maps each role to its address there.
/// This is what makes "type my values -> auto-bind" unique: one common value (HP=91) is ambiguous,
/// but several known values appearing together pins the struct.</summary>
public static class StructLocator
{
    public static Dictionary<string, long> Locate(Dictionary<string, List<long>> candidates, int window = 0x800)
    {
        var cands = new Dictionary<string, List<long>>();
        foreach (var kv in candidates)
        {
            if (kv.Value.Count == 0) continue;
            var l = new List<long>(new HashSet<long>(kv.Value));
            l.Sort();
            cands[kv.Key] = l;
        }
        if (cands.Count == 0) return new();

        // anchor = role with the fewest candidates (most unique)
        string anchor = cands.OrderBy(k => k.Value.Count).First().Key;

        var best = new Dictionary<string, long>();
        foreach (long a in cands[anchor])
        {
            long lo = a - window, hi = a + window;
            var taken = new HashSet<long>();
            var map = new Dictionary<string, long>();
            foreach (var role in cands.Keys)
            {
                foreach (long c in cands[role])  // ascending; lower addresses bind first
                {
                    if (c >= lo && c <= hi && taken.Add(c)) { map[role] = c; break; }
                }
            }
            if (map.Count > best.Count) best = map;
            if (best.Count == cands.Count) break;
        }
        return best;
    }
}
