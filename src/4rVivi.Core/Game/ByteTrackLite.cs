namespace FourRVivi.Core.Game;

/// <summary>
/// Small ByteTrack-style tracker for RO entities. High-confidence detections start tracks;
/// low-confidence detections can only extend existing tracks, which keeps monster boxes pinned
/// through weak YOLO frames without creating duplicate targets.
/// </summary>
public sealed class ByteTrackLite
{
    public readonly record struct Diagnostics(
        int Input,
        int High,
        int Low,
        int NewTracks,
        int MatchedHigh,
        int MatchedLow,
        int Missed,
        int Removed,
        int Active,
        int Visible,
        int LostGrace,
        int Confirmed);

    private sealed class Track
    {
        public int Id;
        public SceneItem Item;
        public int Hits;
        public int Misses;
        public double Vx;
        public double Vy;
        public readonly Dictionary<string, int> LabelVotes = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly List<Track> _tracks = new();
    private readonly float _trackThreshold;
    private readonly float _lowThreshold;
    private readonly float _matchThreshold;
    private readonly int _trackBuffer;
    private readonly int _minHits;
    private int _nextId = 1;
    public Diagnostics LastDiagnostics { get; private set; }

    public ByteTrackLite(float trackThreshold = 0.35f, float lowThreshold = 0.15f, float matchThreshold = 0.25f, int trackBuffer = 4, int minHits = 2)
    {
        _trackThreshold = Math.Clamp(trackThreshold, 0.01f, 0.99f);
        _lowThreshold = Math.Clamp(lowThreshold, 0.01f, _trackThreshold);
        _matchThreshold = Math.Clamp(matchThreshold, 0.01f, 0.99f);
        _trackBuffer = Math.Max(1, trackBuffer);
        _minHits = Math.Max(1, minHits);
    }

    public IReadOnlyList<SceneItem> Update(IEnumerable<SceneItem> detections)
    {
        var all = (detections ?? Array.Empty<SceneItem>())
            .Where(d => d.W > 2 && d.H > 2 && d.Score >= _lowThreshold)
            .OrderByDescending(d => d.Score)
            .ToList();
        ApplyGlobalSceneMotion(all);
        var high = all.Where(d => d.Score >= _trackThreshold).ToList();
        var low = all.Where(d => d.Score < _trackThreshold).ToList();
        var matchedTracks = new HashSet<Track>();

        var highStats = MatchIntoExisting(high, matchedTracks, allowNewTracks: true);
        var lowStats = MatchIntoExisting(low, matchedTracks, allowNewTracks: false);
        int missed = 0;
        int removed = 0;

        for (int i = _tracks.Count - 1; i >= 0; i--)
        {
            var track = _tracks[i];
            if (!matchedTracks.Contains(track))
            {
                track.Misses++;
                missed++;
                if (track.Misses > _trackBuffer)
                {
                    _tracks.RemoveAt(i);
                    removed++;
                    continue;
                }
                track.Item = new SceneItem(track.Item.X, track.Item.Y, track.Item.W, track.Item.H,
                    BestLabel(track), track.Item.Score * 0.92f, track.Item.TrackId, track.Item.HpRatio,
                    track.Item.Hits, track.Item.Misses, SceneTrackState.LostGrace, track.Hits >= _minHits);
            }
        }

        LastDiagnostics = new Diagnostics(
            all.Count,
            high.Count,
            low.Count,
            highStats.NewTracks + lowStats.NewTracks,
            highStats.Matched,
            lowStats.Matched,
            missed,
            removed,
            _tracks.Count,
            _tracks.Count(t => t.Misses == 0),
            _tracks.Count(t => t.Misses > 0),
            _tracks.Count(t => t.Hits >= _minHits));

        MergeDuplicateTracks();

        return _tracks
            .Where(t => t.Hits > 0 && t.Misses <= _trackBuffer)
            .OrderBy(t => t.Misses)
            .ThenByDescending(t => t.Item.Score)
            .Select(t => ToSceneItem(t))
            .ToArray();
    }

    public void Clear()
    {
        _tracks.Clear();
        _nextId = 1;
    }

    private void ApplyGlobalSceneMotion(IReadOnlyList<SceneItem> detections)
    {
        if (_tracks.Count < 2 || detections.Count < 2)
            return;

        var shifts = new List<(double Dx, double Dy)>();
        foreach (var track in _tracks.Where(t => t.Misses <= _trackBuffer))
        {
            SceneItem? best = null;
            double bestDistance = double.MaxValue;
            foreach (var detection in detections)
            {
                if (!LabelsCompatible(track.Item.Label, detection.Label))
                    continue;

                double candidateDx = detection.Cx - track.Item.Cx;
                double candidateDy = detection.Cy - track.Item.Cy;
                double distance = Math.Sqrt(candidateDx * candidateDx + candidateDy * candidateDy);
                double gate = Math.Max(96.0, Math.Max(track.Item.W + detection.W, track.Item.H + detection.H) * 3.0);
                if (distance < bestDistance && distance <= gate)
                {
                    bestDistance = distance;
                    best = detection;
                }
            }

            if (best is { } match)
                shifts.Add((match.Cx - track.Item.Cx, match.Cy - track.Item.Cy));
        }

        if (shifts.Count < 1)
            return;

        double dxMedian = Median(shifts.Select(s => s.Dx));
        double dyMedian = Median(shifts.Select(s => s.Dy));
        if (Math.Abs(dxMedian) < 0.5 && Math.Abs(dyMedian) < 0.5)
            return;

        int dx = (int)Math.Round(Math.Clamp(dxMedian, -160.0, 160.0));
        int dy = (int)Math.Round(Math.Clamp(dyMedian, -160.0, 160.0));
        foreach (var track in _tracks)
        {
            track.Item = new SceneItem(track.Item.X + dx, track.Item.Y + dy, track.Item.W, track.Item.H,
                track.Item.Label, track.Item.Score, track.Item.TrackId, track.Item.HpRatio, track.Item.Hits, track.Item.Misses, track.Item.State, track.Item.Confirmed);
        }
    }

    private (int Matched, int NewTracks) MatchIntoExisting(IReadOnlyList<SceneItem> detections, HashSet<Track> matchedTracks, bool allowNewTracks)
    {
        var usedDetections = new HashSet<int>();
        int matched = 0;
        int created = 0;

        while (true)
        {
            Track? bestTrack = null;
            int bestDetection = -1;
            float bestScore = _matchThreshold;

            for (int ti = 0; ti < _tracks.Count; ti++)
            {
                var track = _tracks[ti];
                if (matchedTracks.Contains(track))
                    continue;

                for (int di = 0; di < detections.Count; di++)
                {
                    if (usedDetections.Contains(di))
                        continue;

                    var detection = detections[di];
                    if (!LabelsCompatible(track.Item.Label, detection.Label))
                        continue;

                    float score = MatchScore(track.Item, detection);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTrack = track;
                        bestDetection = di;
                    }
                }
            }

            if (bestTrack == null || bestDetection < 0)
                break;

            UpdateTrack(bestTrack, detections[bestDetection]);
            matchedTracks.Add(bestTrack);
            usedDetections.Add(bestDetection);
            matched++;
        }

        if (!allowNewTracks)
            return (matched, created);

        for (int di = 0; di < detections.Count; di++)
        {
            if (usedDetections.Contains(di))
                continue;
            var track = NewTrack(detections[di]);
            _tracks.Add(track);
            matchedTracks.Add(track);
            created++;
        }

        return (matched, created);
    }

    private Track NewTrack(SceneItem item)
    {
        int hits = item.Confirmed ? _minHits : 1;
        var track = new Track
        {
            Id = _nextId++,
            Item = item,
            Hits = hits,
            Misses = 0
        };
        VoteLabel(track, item.Label);
        return track;
    }

    private void UpdateTrack(Track track, SceneItem item)
    {
        var previous = track.Item;
        track.Vx = previous.Cx == item.Cx ? track.Vx * 0.5 : (track.Vx * 0.35) + ((item.Cx - previous.Cx) * 0.65);
        track.Vy = previous.Cy == item.Cy ? track.Vy * 0.5 : (track.Vy * 0.35) + ((item.Cy - previous.Cy) * 0.65);
        VoteLabel(track, item.Label);
        string label = item.Label.Equals("Monster", StringComparison.OrdinalIgnoreCase) ? BestLabel(track) : item.Label;
        float score = item.Score >= _trackThreshold
            ? Math.Max(item.Score, previous.Score * 0.90f)
            : Math.Max(previous.Score * 0.86f, item.Score);
        track.Hits++;
        track.Misses = 0;
        track.Item = new SceneItem(item.X, item.Y, item.W, item.H, label, score, State: SceneTrackState.Visible, Confirmed: item.Confirmed || track.Hits >= _minHits);
    }

    private SceneItem ToSceneItem(Track track)
        => new(track.Item.X, track.Item.Y, track.Item.W, track.Item.H, BestLabel(track),
            track.Item.Score, track.Id, track.Item.HpRatio, track.Hits, track.Misses,
            track.Misses == 0 ? SceneTrackState.Visible : SceneTrackState.LostGrace,
            track.Hits >= _minHits);

    private void VoteLabel(Track track, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;
        if (!track.LabelVotes.TryAdd(label, 1))
            track.LabelVotes[label]++;
    }

    private static string BestLabel(Track track)
    {
        if (track.LabelVotes.Count == 0)
            return string.IsNullOrWhiteSpace(track.Item.Label) ? "Monster" : track.Item.Label;

        return track.LabelVotes
            .OrderBy(kv => kv.Key.Equals("Monster", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(kv => kv.Value)
            .ThenByDescending(kv => kv.Key.Length)
            .First().Key;
    }

    private static float MatchScore(SceneItem a, SceneItem b)
    {
        float iou = Iou(a, b);
        double dx = a.Cx - b.Cx;
        double dy = a.Cy - b.Cy;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double radius = Math.Max(36.0, Math.Max(a.W + b.W, a.H + b.H) * 1.2);
        float distanceScore = (float)Math.Clamp(1.0 - (distance / radius), 0.0, 1.0);
        return Math.Max(iou, distanceScore * 0.92f);
    }

    private static float Iou(SceneItem a, SceneItem b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.W, b.X + b.W);
        int y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        int iw = Math.Max(0, x2 - x1);
        int ih = Math.Max(0, y2 - y1);
        float inter = iw * ih;
        float area = (a.W * a.H) + (b.W * b.H) - inter;
        return area <= 0 ? 0f : inter / area;
    }

    private void MergeDuplicateTracks()
    {
        for (int i = _tracks.Count - 1; i > 0; i--)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                var a = _tracks[i];
                var b = _tracks[j];
                if (a.Misses > 0 || b.Misses > 0)
                    continue;

                float iou = Iou(a.Item, b.Item);
                double dx = a.Item.Cx - b.Item.Cx;
                double dy = a.Item.Cy - b.Item.Cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double near = Math.Max(18.0, Math.Min(a.Item.W + b.Item.W, a.Item.H + b.Item.H) * 0.65);
                if (iou < 0.60f && dist > near)
                    continue;

                int dropIndex;
                if (a.Hits != b.Hits)
                    dropIndex = a.Hits < b.Hits ? i : j;
                else if (a.Item.Score != b.Item.Score)
                    dropIndex = a.Item.Score < b.Item.Score ? i : j;
                else
                    dropIndex = i;

                _tracks.RemoveAt(dropIndex);
                if (dropIndex == j)
                {
                    i--;
                    break;
                }
            }
        }
    }

    private static bool LabelsCompatible(string a, string b) => true;

    private static bool IsGeneric(string label)
        => string.IsNullOrWhiteSpace(label)
        || label.Equals("Monster", StringComparison.OrdinalIgnoreCase)
        || label.Equals("Mob", StringComparison.OrdinalIgnoreCase)
        || label.Equals("Entity", StringComparison.OrdinalIgnoreCase);

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(v => v).ToArray();
        if (ordered.Length == 0)
            return 0.0;
        int mid = ordered.Length / 2;
        return (ordered.Length & 1) == 1
            ? ordered[mid]
            : (ordered[mid - 1] + ordered[mid]) * 0.5;
    }
}
