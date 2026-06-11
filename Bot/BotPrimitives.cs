// BotPrimitives.cs — Win32 capture + humanised input used by BotEngine.cs.
// .NET Framework 4.x, x86. Drop into 4rVivi/Bot/.
// MIT. Private-server use only.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using _4rVivi.Utils;

namespace _4rVivi.Bot
{
    // ---------- native ----------
    internal static class NativeWin
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int dx, int dy, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        public const int SRCCOPY = 0x00CC0020;
    }

    // ---------- screen capture ----------
    public static class ScreenCapture
    {
        public static Bitmap CaptureWindow(IntPtr hWnd)
        {
            if (!NativeWin.GetClientRect(hWnd, out var rc) || rc.Width <= 0 || rc.Height <= 0) return null;
            IntPtr src = NativeWin.GetDC(hWnd);
            IntPtr mem = NativeWin.CreateCompatibleDC(src);
            IntPtr bmp = NativeWin.CreateCompatibleBitmap(src, rc.Width, rc.Height);
            IntPtr old = NativeWin.SelectObject(mem, bmp);
            NativeWin.BitBlt(mem, 0, 0, rc.Width, rc.Height, src, 0, 0, NativeWin.SRCCOPY);
            NativeWin.SelectObject(mem, old);
            Bitmap result = Image.FromHbitmap(bmp);
            NativeWin.DeleteObject(bmp);
            NativeWin.DeleteDC(mem);
            NativeWin.ReleaseDC(hWnd, src);
            return result;
        }

        /// <summary>Baseline blob finder: cluster pixels near a key colour. Returns blob centroids.
        /// This is the no-dependency fallback. For real accuracy, swap in OpenCvSharp MatchTemplate.</summary>
        public static List<Point> FindColorBlobs(Bitmap bmp, Color key, int tol, int minBlob)
        {
            var pts = new List<Point>();
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride, h = bmp.Height, w = bmp.Width;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                bool[] seen = new bool[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * stride + x * 4;
                        if (seen[y * w + x]) continue;
                        if (!Near(buf[idx + 2], buf[idx + 1], buf[idx], key, tol)) continue;
                        // flood the blob, accumulate centroid
                        long sx = 0, sy = 0; int n = 0;
                        var stack = new Stack<Point>(); stack.Push(new Point(x, y));
                        while (stack.Count > 0)
                        {
                            var p = stack.Pop();
                            if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h) continue;
                            if (seen[p.Y * w + p.X]) continue;
                            int i2 = p.Y * stride + p.X * 4;
                            if (!Near(buf[i2 + 2], buf[i2 + 1], buf[i2], key, tol)) continue;
                            seen[p.Y * w + p.X] = true;
                            sx += p.X; sy += p.Y; n++;
                            stack.Push(new Point(p.X + 1, p.Y)); stack.Push(new Point(p.X - 1, p.Y));
                            stack.Push(new Point(p.X, p.Y + 1)); stack.Push(new Point(p.X, p.Y - 1));
                        }
                        if (n >= minBlob) pts.Add(new Point((int)(sx / n), (int)(sy / n)));
                    }
            }
            finally { bmp.UnlockBits(data); }
            return pts;
        }

        private static bool Near(byte r, byte g, byte b, Color k, int tol)
            => Math.Abs(r - k.R) <= tol && Math.Abs(g - k.G) <= tol && Math.Abs(b - k.B) <= tol;
    }

    // ---------- humanised input ----------
    public sealed class InputSender
    {
        #region SendInput
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

        private const uint INPUT_KEYBOARD = 1, INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
                           MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
        #endregion

        public void PressVk(int vk, HumanizedTiming t)
        {
            SendKey((ushort)vk, false);
            Thread.Sleep(t.HoldMs());      // realistic key-down hold
            SendKey((ushort)vk, true);
        }

        public void LeftClick(IntPtr hWnd, int clientX, int clientY, HumanizedTiming t)
        {
            MoveCursorToClient(hWnd, clientX, clientY);
            Thread.Sleep(t.HoldMs(8, 25));
            Mouse(MOUSEEVENTF_LEFTDOWN);
            Thread.Sleep(t.HoldMs(20, 55));
            Mouse(MOUSEEVENTF_LEFTUP);
        }

        // RO move = left click on ground; kept separate for clarity / future right-click servers.
        public void ClickMove(IntPtr hWnd, int clientX, int clientY, HumanizedTiming t)
            => LeftClick(hWnd, clientX, clientY, t);

        private void MoveCursorToClient(IntPtr hWnd, int x, int y)
        {
            var p = new NativeWin.POINT { X = x, Y = y };
            NativeWin.ClientToScreen(hWnd, ref p);
            SetCursorPos(p.X, p.Y);
        }

        private void SendKey(ushort vk, bool up)
        {
            var inp = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }

        private void Mouse(uint flag)
        {
            var inp = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
