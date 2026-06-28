namespace FourRVivi.Core.Game;

/// <summary>One thing the vision pipeline found on screen: a box + a label (entity) or string (text).
/// Coords are in the captured frame's pixels; when <see cref="LiveScene.ClientCoords"/> is true they
/// equal the game window's client coords, so the bot can click them directly.</summary>
public readonly record struct SceneItem(int X, int Y, int W, int H, string Label, float Score)
{
    public int Cx => X + W / 2;
    public int Cy => Y + H / 2;
}

/// <summary>Shared latest VISION scene, written by the auto-scan OCR loop and read by the engines
/// (Smart Bot targeting, Auto Debuff). Mirrors <see cref="LiveStats"/> but for spatial detections.
/// Pure data so Core engines can consume it without depending on the App/OCR layer.</summary>
public sealed class LiveScene
{
    public static LiveScene Instance { get; } = new();

    private readonly object _lock = new();
    private SceneItem[] _entities = System.Array.Empty<SceneItem>();
    private string[] _statuses = System.Array.Empty<string>();

    public bool Active { get; set; }
    /// <summary>True when entity coords equal the game window client area (captured via the window,
    /// not a monitor). The bot only auto-targets when this is true.</summary>
    public bool ClientCoords { get; private set; }
    public DateTime UpdatedUtc { get; private set; } = DateTime.MinValue;
    public bool IsFresh => Active && (DateTime.UtcNow - UpdatedUtc).TotalSeconds < 3.0;

    public void SetEntities(IEnumerable<SceneItem> items, bool clientCoords)
    {
        lock (_lock) { _entities = items?.ToArray() ?? System.Array.Empty<SceneItem>(); ClientCoords = clientCoords; UpdatedUtc = DateTime.UtcNow; }
    }
    public void SetStatuses(IEnumerable<string> texts)
    {
        lock (_lock) { _statuses = texts?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? System.Array.Empty<string>(); UpdatedUtc = DateTime.UtcNow; }
    }

    public IReadOnlyList<SceneItem> Entities { get { lock (_lock) return IsFresh ? _entities : System.Array.Empty<SceneItem>(); } }
    public IReadOnlyList<string> Statuses { get { lock (_lock) return IsFresh ? _statuses : System.Array.Empty<string>(); } }

    /// <summary>Nearest entity to (px,py) whose label matches <paramref name="labelMatch"/>, or null.</summary>
    public SceneItem? Nearest(int px, int py, Func<string, bool> labelMatch)
    {
        lock (_lock)
        {
            if (!IsFresh) return null;
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
            if (!IsFresh || string.IsNullOrEmpty(keyword)) return false;
            foreach (var t in _statuses) if (t.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }

    public void Clear() { lock (_lock) { _entities = System.Array.Empty<SceneItem>(); _statuses = System.Array.Empty<string>(); Active = false; } }
}
