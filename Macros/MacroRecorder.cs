// MacroRecorder.cs — record & replay keyboard+mouse macros for 4rVivi.
// Global low-level hooks (WH_KEYBOARD_LL / WH_MOUSE_LL) capture events with timing;
// playback reproduces them (optionally humanized). Supports credential tokens so a
// recorded login macro injects username/password at replay time (never stored in the macro).
// .NET Framework 4.x, WinForms message loop required. NuGet: Newtonsoft.Json. MIT.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace _4rVivi.Macros
{
    public enum MacroEventType { KeyDown, KeyUp, MouseMove, LeftDown, LeftUp, RightDown, RightUp, TypeUsername, TypePassword, TypeText }

    public class MacroEvent
    {
        public MacroEventType Type;
        public int Code;            // vk for keys
        public int X, Y;            // screen coords for mouse
        public int DelayMs;         // delay BEFORE this event (from previous)
        public string Text;         // for TypeText
    }

    public class MacroRecording
    {
        public string Name = "macro";
        public List<MacroEvent> Events = new List<MacroEvent>();

        public void Save(string path) => System.IO.File.WriteAllText(path,
            Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
        public static MacroRecording Load(string path) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<MacroRecording>(System.IO.File.ReadAllText(path));
    }

    // ===================================================================
    // RECORDER — install hooks, capture until Stop(). Mouse-move sampling is
    // throttled so recordings stay small.
    // ===================================================================
    public sealed class MacroRecorder
    {
        private IntPtr _kbHook = IntPtr.Zero, _msHook = IntPtr.Zero;
        private LowLevelProc _kbProc, _msProc;   // keep delegates alive (GC)
        private MacroRecording _rec;
        private long _lastTick;
        private long _lastMouseSample;
        public int MouseSampleMs = 60;            // throttle mouse-move capture
        public bool Recording { get; private set; }

        public void Start(string name)
        {
            _rec = new MacroRecording { Name = name };
            _lastTick = Now();
            _kbProc = KbCallback; _msProc = MsCallback;
            using (var p = Process.GetCurrentProcess())
            using (var m = p.MainModule)
            {
                IntPtr h = GetModuleHandle(m.ModuleName);
                _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, h, 0);
                _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc, h, 0);
            }
            Recording = true;
        }

        public MacroRecording Stop()
        {
            if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_msHook != IntPtr.Zero) { UnhookWindowsHookEx(_msHook); _msHook = IntPtr.Zero; }
            Recording = false;
            return _rec;
        }

        private int Delta() { long n = Now(); int d = (int)(n - _lastTick); _lastTick = n; return d; }
        private static long Now() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        private IntPtr KbCallback(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = (int)w;
                int vk = Marshal.ReadInt32(l);
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    _rec.Events.Add(new MacroEvent { Type = MacroEventType.KeyDown, Code = vk, DelayMs = Delta() });
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    _rec.Events.Add(new MacroEvent { Type = MacroEventType.KeyUp, Code = vk, DelayMs = Delta() });
            }
            return CallNextHookEx(_kbHook, code, w, l);
        }

        private IntPtr MsCallback(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = (int)w;
                var ms = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(MSLLHOOKSTRUCT));
                switch (msg)
                {
                    case WM_LBUTTONDOWN: Add(MacroEventType.LeftDown, ms); break;
                    case WM_LBUTTONUP: Add(MacroEventType.LeftUp, ms); break;
                    case WM_RBUTTONDOWN: Add(MacroEventType.RightDown, ms); break;
                    case WM_RBUTTONUP: Add(MacroEventType.RightUp, ms); break;
                    case WM_MOUSEMOVE:
                        long n = Now();
                        if (n - _lastMouseSample >= MouseSampleMs)
                        { _lastMouseSample = n; Add(MacroEventType.MouseMove, ms); }
                        break;
                }
            }
            return CallNextHookEx(_msHook, code, w, l);
        }

        private void Add(MacroEventType t, MSLLHOOKSTRUCT ms)
            => _rec.Events.Add(new MacroEvent { Type = t, X = ms.pt.x, Y = ms.pt.y, DelayMs = Delta() });

        #region native
        private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202,
                          WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
        private delegate IntPtr LowLevelProc(int code, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string name);
        #endregion
    }

    // ===================================================================
    // PLAYER — replays a recording. Resolves credential tokens at runtime.
    // ===================================================================
    public sealed class MacroPlayer
    {
        public bool Humanize = true;            // add small jitter so playback isn't pixel/ms-perfect
        public double SpeedMultiplier = 1.0;     // 0.5 = 2x faster
        private readonly Random _rng = new Random();

        public void Play(MacroRecording rec, Func<string> getUser, Func<string> getPass)
        {
            foreach (var e in rec.Events)
            {
                int wait = (int)(e.DelayMs * SpeedMultiplier);
                if (Humanize) wait += _rng.Next(-8, 18);
                if (wait > 0) Thread.Sleep(Math.Max(1, wait));

                switch (e.Type)
                {
                    case MacroEventType.KeyDown: SendKey((ushort)e.Code, false); break;
                    case MacroEventType.KeyUp: SendKey((ushort)e.Code, true); break;
                    case MacroEventType.MouseMove: SetCursorPos(e.X, e.Y); break;
                    case MacroEventType.LeftDown: SetCursorPos(e.X, e.Y); Mouse(MOUSEEVENTF_LEFTDOWN); break;
                    case MacroEventType.LeftUp: Mouse(MOUSEEVENTF_LEFTUP); break;
                    case MacroEventType.RightDown: SetCursorPos(e.X, e.Y); Mouse(MOUSEEVENTF_RIGHTDOWN); break;
                    case MacroEventType.RightUp: Mouse(MOUSEEVENTF_RIGHTUP); break;
                    case MacroEventType.TypeUsername: TypeUnicode(getUser?.Invoke() ?? ""); break;
                    case MacroEventType.TypePassword: TypeUnicode(getPass?.Invoke() ?? ""); break;
                    case MacroEventType.TypeText: TypeUnicode(e.Text ?? ""); break;
                }
            }
        }

        private void TypeUnicode(string s)
        {
            foreach (char ch in s)
            {
                SendUnicode(ch, false);
                Thread.Sleep(Humanize ? _rng.Next(25, 70) : 15);
                SendUnicode(ch, true);
            }
        }

        #region native SendInput
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] i, int size);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        private const uint INPUT_KEYBOARD = 1, INPUT_MOUSE = 0, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
        private const uint MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, MOUSEEVENTF_RIGHTDOWN = 8, MOUSEEVENTF_RIGHTUP = 0x10;

        private static void SendKey(ushort vk, bool up)
        {
            var i = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }
        private static void SendUnicode(char ch, bool up)
        {
            var i = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } } };
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }
        private static void Mouse(uint flag)
        {
            var i = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } } };
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }
        #endregion
    }
}
