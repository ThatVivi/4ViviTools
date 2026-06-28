using System.Runtime.InteropServices;

namespace FourRVivi.Core.Input;

/// <summary>Sends key presses via a selectable standard Windows backend (see <see cref="InputMethod"/>):
/// SendInput, the legacy keybd_event, or PostMessage. All three are normal OS input APIs (the same ones
/// AutoHotkey uses); the user picks whichever their server accepts.</summary>
public sealed class KeySender
{
    public InputMethod Method { get; set; } = InputMethod.SendInput;

    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;
    private const uint INPUT_KEYBOARD = 1, MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")] private static extern IntPtr SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inp, int cb);

    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }

    private static bool IsExtended(int vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2D or 0x2E or 0x90 or 0x6F or 0xA3;

    public void Tap(IntPtr hWnd, int virtualKey, int holdMs = 0)
    {
        if (hWnd == IntPtr.Zero || virtualKey <= 0) return;
        Down(hWnd, virtualKey);
        Thread.Sleep(System.Math.Max(30, holdMs));   // hold long enough for the game to register the press
        Up(hWnd, virtualKey);
    }

    public void Down(IntPtr hWnd, int vk)
    {
        if (hWnd == IntPtr.Zero || vk <= 0) return;
        switch (Method)
        {
            case InputMethod.PostMessage:
                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
                break;
            case InputMethod.MouseKeyEvent:
                SetForegroundWindow(hWnd);
                keybd_event((byte)vk, (byte)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC),
                    KEYEVENTF_SCANCODE | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0), IntPtr.Zero);
                break;
            default: // SendInput
                SetForegroundWindow(hWnd);
                SendKey(vk, false);
                break;
        }
    }

    public void Up(IntPtr hWnd, int vk)
    {
        if (hWnd == IntPtr.Zero || vk <= 0) return;
        switch (Method)
        {
            case InputMethod.PostMessage:
                PostMessage(hWnd, WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
                break;
            case InputMethod.MouseKeyEvent:
                keybd_event((byte)vk, (byte)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC),
                    KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0), IntPtr.Zero);
                break;
            default: // SendInput
                SendKey(vk, true);
                break;
        }
    }

    private void SendKey(int vk, bool up)
    {
        ushort scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
        var inp = new INPUT[] { new INPUT { type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = scan, dwFlags = flags } } } };
        SendInput(1, inp, Marshal.SizeOf<INPUT>());
    }
}
