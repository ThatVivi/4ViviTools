using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FourRVivi.Core.Common;

namespace FourRVivi.Core.Input;

public sealed class ViiperInput : IDisposable
{
    private readonly object _lock = new();
    private TcpClient? _keyboardClient;
    private NetworkStream? _keyboardStream;
    private TcpClient? _mouseClient;
    private NetworkStream? _mouseStream;
    private Process? _serverProcess;
    private int _busId;
    private string _keyboardDevId = "";
    private string _mouseDevId = "";
    private long _nextRetryTick;
    private string? _lastError;

    private const string Host = "127.0.0.1";
    private const int Port = 3242;
    private const byte MouseLeft = 0x01;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public bool IsInstalled => FindViiperExe() != null;
    public bool IsReady => _keyboardStream != null && _mouseStream != null && Ping();

    public bool EnsureConnected()
    {
        lock (_lock)
        {
            if (HasHealthyStreams())
                return true;
            if (_keyboardStream != null || _mouseStream != null || _keyboardClient != null || _mouseClient != null)
            {
                DebugTrace.Write("VIIPER", "Existing VIIPER streams are stale; recreating virtual USB devices.");
                DisposeStreams(removeBus: true);
            }

            var now = Environment.TickCount64;
            if (now < _nextRetryTick)
                return false;

            try
            {
                if (!Ping())
                {
                    if (!StartServer())
                    {
                        RetryLater("VIIPER server is not running and viiper.exe was not found.");
                        return false;
                    }

                    var deadline = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < deadline && !Ping())
                        Thread.Sleep(100);
                }

                _busId = CreateBus();
                _keyboardDevId = AddDevice(_busId, "keyboard");
                _mouseDevId = AddDevice(_busId, "mouse");
                _keyboardClient = ConnectDeviceStream(_busId, _keyboardDevId, out _keyboardStream);
                _mouseClient = ConnectDeviceStream(_busId, _mouseDevId, out _mouseStream);
                DebugTrace.Write("VIIPER", $"Connected bus={_busId} keyboard={_keyboardDevId} mouse={_mouseDevId}.");
                InputRuntimeStatus.SetLastMouse("VIIPER ready");
                InputRuntimeStatus.SetLastKeyboard("VIIPER ready");
                return true;
            }
            catch (Exception ex)
            {
                RetryLater("VIIPER connect failed: " + ex.Message, ex);
                DisposeStreams(removeBus: true);
                return false;
            }
        }
    }

    public bool TapKey(string key, int holdMs)
    {
        var usage = KeyToHidUsage(key);
        if (usage == 0)
        {
            DebugTrace.Write("VIIPER", $"Unsupported keyboard key '{key}'.");
            return false;
        }

        lock (_lock)
        {
            if (!EnsureConnected() || _keyboardStream == null)
                return false;

            try
            {
                WriteKeyboard(usage);
                Thread.Sleep(Math.Max(30, holdMs));
                WriteKeyboard(0);
                DebugTrace.Write("VIIPER", $"Keyboard tap key={key} usage=0x{usage:X2}.");
                InputRuntimeStatus.SetLastKeyboard($"VIIPER {key}");
                return true;
            }
            catch (Exception ex)
            {
                RetryLater("VIIPER keyboard write failed: " + ex.Message, ex);
                DisposeStreams(removeBus: true);
                return false;
            }
        }
    }

    public bool ClickAtScreen(int x, int y, int holdMs, out int moveMs)
    {
        moveMs = 0;
        lock (_lock)
        {
            if (!EnsureConnected() || _mouseStream == null)
                return false;

            try
            {
                var move = MoveToScreenLocked(x, y);
                moveMs = move.ElapsedMs;
                DebugTrace.Write("VIIPER", $"Mouse move start={move.StartX},{move.StartY} target={x},{y} final={move.FinalX},{move.FinalY} reached={move.Reached} iterations={move.Iterations} moveMs={move.ElapsedMs}.");
                if (!move.Reached)
                {
                    InputRuntimeStatus.SetLastMouse($"VIIPER move failed {move.FinalX},{move.FinalY}");
                    return false;
                }
                WriteMouse(MouseLeft, 0, 0);
                Thread.Sleep(Math.Max(30, holdMs));
                WriteMouse(0, 0, 0);
                DebugTrace.Write("VIIPER", $"Mouse click screen={x},{y} moveMs={moveMs} holdMs={holdMs}.");
                InputRuntimeStatus.SetLastMouse($"VIIPER click {moveMs} ms move");
                return true;
            }
            catch (Exception ex)
            {
                RetryLater("VIIPER mouse write failed: " + ex.Message, ex);
                DisposeStreams(removeBus: true);
                return false;
            }
        }
    }

    public int EstimateMoveMsToScreen(int x, int y)
    {
        if (!GetCursorPos(out var p))
            return 35;

        var distance = Math.Sqrt(Math.Pow(x - p.X, 2) + Math.Pow(y - p.Y, 2));
        return Math.Clamp((int)Math.Ceiling(distance / 2.8) + 18, 25, 420);
    }

    private readonly record struct MouseMoveResult(int StartX, int StartY, int FinalX, int FinalY, int Iterations, int ElapsedMs, bool Reached);

    private MouseMoveResult MoveToScreenLocked(int x, int y)
    {
        var start = Stopwatch.GetTimestamp();
        GetCursorPos(out var startPos);
        var final = startPos;
        int iterations = 0;
        for (int i = 0; i < 420; i++)
        {
            if (!GetCursorPos(out var p))
                break;
            final = p;
            iterations = i + 1;

            var dxFull = x - p.X;
            var dyFull = y - p.Y;
            if (Math.Abs(dxFull) <= 2 && Math.Abs(dyFull) <= 2)
                break;

            var dx = Math.Clamp(dxFull, -160, 160);
            var dy = Math.Clamp(dyFull, -160, 160);
            WriteMouse(0, (short)dx, (short)dy);
            Thread.Sleep(Math.Abs(dxFull) > 420 || Math.Abs(dyFull) > 420 ? 0 : 1);
        }
        GetCursorPos(out final);
        bool reached = Math.Abs(x - final.X) <= 3 && Math.Abs(y - final.Y) <= 3;
        return new MouseMoveResult(startPos.X, startPos.Y, final.X, final.Y, iterations,
            (int)Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds), reached);
    }

    private void WriteKeyboard(byte usage)
    {
        if (_keyboardStream == null)
            throw new InvalidOperationException("Keyboard stream is not connected.");

        Span<byte> packet = stackalloc byte[usage == 0 ? 2 : 3];
        packet[0] = 0;
        packet[1] = usage == 0 ? (byte)0 : (byte)1;
        if (usage != 0)
            packet[2] = usage;
        _keyboardStream.Write(packet);
        _keyboardStream.Flush();
    }

    private void WriteMouse(byte buttons, short dx, short dy)
    {
        if (_mouseStream == null)
            throw new InvalidOperationException("Mouse stream is not connected.");

        Span<byte> packet = stackalloc byte[9];
        packet[0] = buttons;
        WriteInt16(packet[1..3], dx);
        WriteInt16(packet[3..5], dy);
        WriteInt16(packet[5..7], 0);
        WriteInt16(packet[7..9], 0);
        _mouseStream.Write(packet);
        _mouseStream.Flush();
    }

    private static void WriteInt16(Span<byte> span, short value)
    {
        span[0] = unchecked((byte)value);
        span[1] = unchecked((byte)(value >> 8));
    }

    private bool Ping()
    {
        try
        {
            var response = SendApi("ping", 650);
            return response.Contains("VIIPER", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool HasHealthyStreams()
    {
        if (_keyboardStream == null || _mouseStream == null || _keyboardClient == null || _mouseClient == null)
            return false;
        if (!IsTcpAlive(_keyboardClient) || !IsTcpAlive(_mouseClient))
            return false;
        return Ping();
    }

    private static bool IsTcpAlive(TcpClient client)
    {
        try
        {
            if (!client.Connected)
                return false;
            var socket = client.Client;
            return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    private static int CreateBus()
    {
        using var doc = JsonDocument.Parse(SendApi("bus/create", 1500));
        return doc.RootElement.GetProperty("busId").GetInt32();
    }

    private static string AddDevice(int busId, string type)
    {
        using var doc = JsonDocument.Parse(SendApi($"bus/{busId}/add {{\"type\":\"{type}\"}}", 1500));
        return doc.RootElement.GetProperty("devId").GetString() ?? "";
    }

    private static TcpClient ConnectDeviceStream(int busId, string devId, out NetworkStream stream)
    {
        var client = new TcpClient();
        client.NoDelay = true;
        client.Connect(Host, Port);
        stream = client.GetStream();
        var payload = Encoding.UTF8.GetBytes($"bus/{busId}/{devId}\0");
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
        return client;
    }

    private static string SendApi(string request, int timeoutMs)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        client.ReceiveTimeout = timeoutMs;
        client.SendTimeout = timeoutMs;
        client.Connect(Host, Port);
        using var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(request + "\0");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            ms.Write(buffer, 0, read);
        return Encoding.UTF8.GetString(ms.ToArray()).Trim();
    }

    private bool StartServer()
    {
        var exe = FindViiperExe();
        if (exe == null)
            return false;

        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "4rVivi", "Logs");
            Directory.CreateDirectory(logDir);
            _serverProcess = Process.Start(new ProcessStartInfo(exe)
            {
                Arguments = "server --log.level=info --log.file=\"" + Path.Combine(logDir, "VIIPER.log") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            DebugTrace.Write("VIIPER", $"Started VIIPER server: {exe}");
            return true;
        }
        catch (Exception ex)
        {
            DebugTrace.Write("VIIPER", "Could not start VIIPER server.", ex);
            return false;
        }
    }

    private static string? FindViiperExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIIPER", "viiper.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VIIPER", "viiper.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VIIPER", "viiper.exe"),
            Path.Combine(AppContext.BaseDirectory, "viiper.exe"),
            Path.Combine(AppContext.BaseDirectory, "Drivers", "VIIPER", "viiper.exe"),
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
            catch { }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var path = Path.Combine(dir.Trim(), "viiper.exe");
                if (File.Exists(path))
                    return path;
            }
            catch { }
        }

        return null;
    }

    private void RetryLater(string message, Exception? ex = null)
    {
        if (!string.Equals(_lastError, message, StringComparison.Ordinal))
        {
            DebugTrace.Write("VIIPER", message, ex);
            _lastError = message;
        }
        _nextRetryTick = Environment.TickCount64 + 3000;
    }

    private void DisposeStreams(bool removeBus)
    {
        try { _keyboardStream?.Dispose(); } catch { }
        try { _keyboardClient?.Dispose(); } catch { }
        try { _mouseStream?.Dispose(); } catch { }
        try { _mouseClient?.Dispose(); } catch { }
        _keyboardStream = null;
        _keyboardClient = null;
        _mouseStream = null;
        _mouseClient = null;

        if (removeBus && _busId > 0)
        {
            try { SendApi($"bus/remove {_busId}", 800); } catch { }
        }
        _busId = 0;
        _keyboardDevId = "";
        _mouseDevId = "";
    }

    private static byte KeyToHidUsage(string? key)
    {
        key = (key ?? "").Trim().ToUpperInvariant();
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z') return (byte)(0x04 + (c - 'A'));
            if (c is >= '1' and <= '9') return (byte)(0x1E + (c - '1'));
            if (c == '0') return 0x27;
        }
        if (key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key[1..], out var f) && f is >= 1 and <= 12)
            return (byte)(0x3A + (f - 1));
        return key switch
        {
            "ENTER" => 0x28,
            "ESC" or "ESCAPE" => 0x29,
            "BACK" or "BACKSPACE" => 0x2A,
            "TAB" => 0x2B,
            "SPACE" => 0x2C,
            "PAGEUP" => 0x4B,
            "PAGEDOWN" => 0x4E,
            "LEFT" => 0x50,
            "RIGHT" => 0x4F,
            "UP" => 0x52,
            "DOWN" => 0x51,
            _ => 0
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposeStreams(removeBus: true);
            try
            {
                if (_serverProcess is { HasExited: false })
                    _serverProcess.Kill(entireProcessTree: true);
            }
            catch { }
            _serverProcess = null;
        }
    }
}
