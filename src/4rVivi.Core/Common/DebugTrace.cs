namespace FourRVivi.Core.Common;

public static class DebugTrace
{
    private static readonly object Lock = new();
    private static long _sequence;
    private static int _writesSinceTrimCheck;
    private static readonly DateTime StartedUtc = DateTime.UtcNow;
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "4rVivi",
        "Logs");

    public static string LogPath => Path.Combine(LogDir, "DebugTrace.log");

    public static bool Enabled { get; set; } = true;

    public static void Write(string area, string message, Exception? ex = null)
    {
        if (!Enabled) return;

        try
        {
            Directory.CreateDirectory(LogDir);
            long seq = Interlocked.Increment(ref _sequence);
            long upMs = (long)(DateTime.UtcNow - StartedUtc).TotalMilliseconds;
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [#{seq:000000} T{Environment.CurrentManagedThreadId} +{upMs}ms] [{area}] {message}";
            if (ex != null) line += $"{Environment.NewLine}{ex}";
            lock (Lock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
                if (++_writesSinceTrimCheck >= 100)
                {
                    _writesSinceTrimCheck = 0;
                    TrimIfLarge();
                }
            }
        }
        catch
        {
        }
    }

    private static void TrimIfLarge()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < 8_000_000) return;

        var lines = File.ReadLines(LogPath).TakeLast(12000).ToArray();
        File.WriteAllLines(LogPath, lines);
    }
}
