using FourRVivi.Core.Data;
using FourRVivi.Core.Ocr;

namespace FourRVivi.Core.Game;

/// <summary>One thing the vision pipeline found on screen: a box + a label (entity) or string (text).
/// Coords are in the captured frame's pixels; when <see cref="LiveScene.ClientCoords"/> is true they
/// equal the game window's client coords, so the bot can click them directly.</summary>
public enum SceneTrackState
{
    Visible,
    LostGrace,
    Removed
}

public readonly record struct SceneItem(int X, int Y, int W, int H, string Label, float Score, int TrackId = 0, float HpRatio = -1, int Hits = 0, int Misses = 0, SceneTrackState State = SceneTrackState.Visible, bool Confirmed = false)
{
    public int Cx => X + W / 2;
    public int Cy => Y + H / 2;
    public bool HasHp => HpRatio >= 0;
    public bool IsAttackable => State == SceneTrackState.Visible && Confirmed && Misses == 0 && Score >= VisionConfig.DefaultAttackConfidence;
    public bool IsLostGrace => State == SceneTrackState.LostGrace;
}

public readonly record struct LiveSceneSnapshot(
    int FrameId,
    int CaptureWidth,
    int CaptureHeight,
    long PublishedAtMs,
    int FilterVersion,
    bool Active,
    bool ClientCoords,
    DateTime EntityUpdatedUtc,
    IReadOnlyList<SceneItem> Entities,
    IReadOnlyList<string> Statuses);

/// <summary>Shared latest VISION scene, written by the auto-scan OCR loop and read by the engines
/// (Smart Bot targeting, Auto Debuff). Mirrors <see cref="LiveStats"/> but for spatial detections.
/// Pure data so Core engines can consume it without depending on the App/OCR layer.</summary>
public sealed class LiveScene
{
    public static LiveScene Instance { get; } = new();
    public const int TrackMinHits = 2;
    public const int TrackMaxMisses = 2;

    private readonly object _lock = new();
    private SceneItem[] _entities = System.Array.Empty<SceneItem>();
    private string[] _statuses = System.Array.Empty<string>();
    private HashSet<string> _focusedMonsterKeys = new(StringComparer.OrdinalIgnoreCase);
    private string[] _focusedMonsterNames = System.Array.Empty<string>();
    private readonly ByteTrackLite _entityTracker = new(
        trackThreshold: VisionConfig.DefaultTrackConfidence,
        lowThreshold: VisionConfig.TrackerLowConfidence,
        matchThreshold: VisionConfig.TrackerMatchThreshold,
        trackBuffer: VisionConfig.TrackerMaxMisses,
        minHits: VisionConfig.TrackerMinHits);
    private bool? _lastEntityClientCoords;
    private int _frameId;
    private int _captureWidth;
    private int _captureHeight;
    private int _filterVersion;
    private long _publishedAtMs;

    public bool Active { get; set; }
    /// <summary>True when entity coords equal the game window client area (captured via the window,
    /// not a monitor). The bot only auto-targets when this is true.</summary>
    public bool ClientCoords { get; private set; }
    public DateTime UpdatedUtc { get; private set; } = DateTime.MinValue;
    public DateTime EntityUpdatedUtc { get; private set; } = DateTime.MinValue;
    public DateTime StatusUpdatedUtc { get; private set; } = DateTime.MinValue;
    public bool IsFresh => Active && (DateTime.UtcNow - UpdatedUtc).TotalSeconds < 3.0;
    public bool EntitiesFresh => Active && (DateTime.UtcNow - EntityUpdatedUtc).TotalMilliseconds < 2500.0;
    public bool StatusesFresh => Active && (DateTime.UtcNow - StatusUpdatedUtc).TotalSeconds < 3.0;
    public IReadOnlyList<string> FocusedMonsterNames { get { lock (_lock) return _focusedMonsterNames; } }
    public int FilterVersion { get { lock (_lock) return _filterVersion; } }
    public ByteTrackLite.Diagnostics TrackerDiagnostics { get { lock (_lock) return _entityTracker.LastDiagnostics; } }

    public void SetMonsterFocus(IEnumerable<string>? names)
    {
        lock (_lock)
        {
            var list = (names ?? System.Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToArray();
            _focusedMonsterNames = list;
            _focusedMonsterKeys = list.Select(GameDatabase.NormalizeKey)
                .Where(k => k.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ClearEntitiesOnlyLocked();
            _filterVersion++;
        }
    }

    public void SetEntities(IEnumerable<SceneItem> items, bool clientCoords)
        => SetEntities(items, System.Array.Empty<SceneItem>(), clientCoords);

    public void SetEntities(IEnumerable<SceneItem> items, IEnumerable<SceneItem> hpBars, bool clientCoords)
        => SetEntities(items, hpBars, clientCoords, 0, 0);

    public void SetEntities(IEnumerable<SceneItem> items, IEnumerable<SceneItem> hpBars, bool clientCoords, int captureWidth, int captureHeight)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var current = (items ?? System.Array.Empty<SceneItem>())
                .Where(i => i.W > 2 && i.H > 2 && !string.IsNullOrWhiteSpace(i.Label))
                .Select(i => new SceneItem(i.X, i.Y, i.W, i.H, FocusLabel(i.Label), i.Score, i.TrackId, i.HpRatio, i.Hits, i.Misses, i.State, i.Confirmed))
                .ToArray();
            var bars = (hpBars ?? System.Array.Empty<SceneItem>())
                .Where(i => i.W > 2 && i.H > 2)
                .ToArray();

            if (_lastEntityClientCoords.HasValue && _lastEntityClientCoords.Value != clientCoords)
            {
                ClearEntitiesOnlyLocked();
                _filterVersion++;
            }

            _entities = AttachHpBars(_entityTracker.Update(current), bars);
            ClientCoords = clientCoords;
            _lastEntityClientCoords = clientCoords;
            _captureWidth = captureWidth;
            _captureHeight = captureHeight;
            _frameId++;
            _publishedAtMs = Environment.TickCount64;
            EntityUpdatedUtc = now;
            UpdatedUtc = now;
        }
    }

    public void SetAuthoritativeEntities(IEnumerable<SceneItem> items, bool clientCoords, int captureWidth, int captureHeight, string source)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_lastEntityClientCoords.HasValue && _lastEntityClientCoords.Value != clientCoords)
            {
                ClearEntitiesOnlyLocked();
                _filterVersion++;
            }

            _entityTracker.Clear();
            int nextTrack = 1;
            _entities = (items ?? System.Array.Empty<SceneItem>())
                .Where(i => i.W > 2 && i.H > 2 && !string.IsNullOrWhiteSpace(i.Label))
                .Select(i =>
                {
                    var trackId = i.TrackId > 0 ? i.TrackId : nextTrack++;
                    return new SceneItem(
                        i.X, i.Y, i.W, i.H, FocusLabel(i.Label),
                        Math.Max(i.Score, VisionConfig.DefaultAttackConfidence),
                        trackId,
                        i.HpRatio,
                        Math.Max(i.Hits, TrackMinHits),
                        0,
                        SceneTrackState.Visible,
                        true);
                })
                .ToArray();

            ClientCoords = clientCoords;
            _lastEntityClientCoords = clientCoords;
            _captureWidth = captureWidth;
            _captureHeight = captureHeight;
            _frameId++;
            _publishedAtMs = Environment.TickCount64;
            EntityUpdatedUtc = now;
            UpdatedUtc = now;
        }
    }

    public void SetStatuses(IEnumerable<string> texts)
    {
        lock (_lock)
        {
            _statuses = texts?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? System.Array.Empty<string>();
            StatusUpdatedUtc = DateTime.UtcNow;
            UpdatedUtc = StatusUpdatedUtc;
        }
    }

    public IReadOnlyList<SceneItem> Entities { get { lock (_lock) return EntitiesFresh ? _entities : System.Array.Empty<SceneItem>(); } }
    public IReadOnlyList<string> Statuses { get { lock (_lock) return StatusesFresh ? _statuses : System.Array.Empty<string>(); } }

    public LiveSceneSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new LiveSceneSnapshot(
                _frameId,
                _captureWidth,
                _captureHeight,
                _publishedAtMs,
                _filterVersion,
                Active,
                ClientCoords,
                EntityUpdatedUtc,
                EntitiesFresh ? _entities.ToArray() : System.Array.Empty<SceneItem>(),
                StatusesFresh ? _statuses.ToArray() : System.Array.Empty<string>());
        }
    }

    /// <summary>Nearest entity to (px,py) whose label matches <paramref name="labelMatch"/>, or null.</summary>
    public SceneItem? Nearest(int px, int py, Func<string, bool> labelMatch)
    {
        lock (_lock)
        {
            if (!EntitiesFresh) return null;
            SceneItem? best = null; long bestD = long.MaxValue;
            foreach (var e in _entities)
            {
                if (!labelMatch(e.Label)) continue;
                long dx = e.Cx - px, dy = e.Cy - py, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }
    }

    /// <summary>True if any current status text contains the keyword (case-insensitive).</summary>
    public bool HasStatus(string keyword)
    {
        lock (_lock)
        {
            if (!StatusesFresh || string.IsNullOrEmpty(keyword)) return false;
            foreach (var t in _statuses) if (t.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }

    public void Clear() { lock (_lock) { ClearEntitiesOnlyLocked(); _statuses = System.Array.Empty<string>(); Active = false; StatusUpdatedUtc = DateTime.MinValue; _filterVersion++; } }

    public void ClearEntityTracks(string reason = "")
    {
        lock (_lock)
        {
            ClearEntitiesOnlyLocked();
            _filterVersion++;
        }
    }

    private void ClearEntitiesOnlyLocked()
    {
        _entities = System.Array.Empty<SceneItem>();
        _entityTracker.Clear();
        _lastEntityClientCoords = null;
        EntityUpdatedUtc = DateTime.MinValue;
        _captureWidth = 0;
        _captureHeight = 0;
        _frameId++;
        _publishedAtMs = Environment.TickCount64;
    }

    private string FocusLabel(string label)
    {
        if (_focusedMonsterKeys.Count == 0 || IsGenericMonster(label) || IsFocusedMonsterName(label))
            return label;
        return "Monster";
    }

    private bool IsFocusedMonsterName(string label)
        => _focusedMonsterKeys.Contains(GameDatabase.NormalizeKey(label));

    private static bool IsGenericMonster(string label)
        => label.Equals("Monster", StringComparison.OrdinalIgnoreCase)
        || label.Equals("Mob", StringComparison.OrdinalIgnoreCase)
        || label.Equals("Entity", StringComparison.OrdinalIgnoreCase);

    private static SceneItem[] AttachHpBars(IReadOnlyList<SceneItem> entities, IReadOnlyList<SceneItem> bars)
    {
        if (entities.Count == 0)
            return System.Array.Empty<SceneItem>();
        var result = entities.ToArray();
        if (bars.Count == 0)
            return result;

        for (int b = 0; b < bars.Count; b++)
        {
            var bar = bars[b];
            int bestIndex = -1;
            long bestD = long.MaxValue;
            for (int i = 0; i < result.Length; i++)
            {
                var ent = result[i];
                bool verticalBand = bar.Cy >= ent.Y - 10 && bar.Cy <= ent.Y + ent.H + 42;
                long dx = bar.Cx - ent.Cx;
                double xTolerance = Math.Max(16.0, Math.Max(ent.W * 0.60, bar.W * 0.80));
                if (!verticalBand || Math.Abs(dx) > xTolerance) continue;
                long dy = bar.Cy - (ent.Y + ent.H);
                long d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                continue;

            var best = result[bestIndex];
            float ratio = Math.Clamp(bar.W / (float)Math.Max(12, best.W), 0.03f, 1.0f);
            float hp = best.HpRatio < 0 ? ratio : best.HpRatio * 0.55f + ratio * 0.45f;
            result[bestIndex] = new SceneItem(best.X, best.Y, best.W, best.H, best.Label, best.Score, best.TrackId, hp, best.Hits, best.Misses, best.State, best.Confirmed);
        }

        return result;
    }
}
