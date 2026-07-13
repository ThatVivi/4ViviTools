using System.Runtime.InteropServices;
using FourRVivi.Core.Common;
using FourRVivi.Core.Game;

namespace FourRVivi.Core.Input;

/// <summary>Sends key presses via a selectable standard Windows backend (see <see cref="InputMethod"/>):
/// SendInput, the legacy keybd_event, or PostMessage. All three are normal OS input APIs (the same ones
/// AutoHotkey uses); the user picks whichever their server accepts.</summary>
public sealed class KeySender
{
    public InputMethod Method { get; set; } = InputMethod.SendInput;
    public VirtualHidInput? VirtualHid { get; set; }
    public ViiperInput? Viiper { get; set; }
    public FocusGate? FocusGate { get; set; }
    public bool FallbackToNormalKeyWhenVirtualHidFails { get; set; }

    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;
    private const uint INPUT_KEYBOARD = 1, MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inp, int cb);

    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }

    private static bool IsExtended(int vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2D or 0x2E or 0x90 or 0x6F or 0xA3;

    private bool CanAct(string action)
    {
        if (FocusGate == null || FocusGate.CanAct(out _))
            return true;
        DebugTrace.Write("Input", $"Key action blocked by FocusGate action='{action}'.");
        InputRuntimeStatus.SetLastKeyboard("Paused: focus RO client");
        return false;
    }

    public bool TryVirtualHidTap(IntPtr hWnd, int virtualKey, int holdMs = 0)
    {
        if (!CanAct($"FakerInput {KeyName.FromVk(virtualKey)}")) return false;
        if (hWnd == IntPtr.Zero || virtualKey <= 0 || VirtualHid == null)
        {
            DebugTrace.Write("Input", $"FakerInput key skipped hwnd=0x{hWnd.ToInt64():X} vk={virtualKey} hasVirtualHid={VirtualHid != null}.");
            return false;
        }
        var ok = VirtualHid.TapKey(KeyName.FromVk(virtualKey), holdMs);
        DebugTrace.Write("Input", $"FakerInput key key={KeyName.FromVk(virtualKey)} vk={virtualKey} holdMs={holdMs} ok={ok}.");
        InputRuntimeStatus.SetLastKeyboard(ok ? $"FakerInput {KeyName.FromVk(virtualKey)}" : $"FakerInput failed {KeyName.FromVk(virtualKey)}");
        return ok;
    }

    public void Tap(IntPtr hWnd, int virtualKey, int holdMs = 0)
    {
        DebugTrace.Write("Input", $"Key tap requested method={Method} hwnd=0x{hWnd.ToInt64():X} key={KeyName.FromVk(virtualKey)} vk={virtualKey} holdMs={holdMs} fallback={FallbackToNormalKeyWhenVirtualHidFails}.");
        if (!CanAct($"Tap {KeyName.FromVk(virtualKey)}")) return;
        if (hWnd == IntPtr.Zero || virtualKey <= 0)
        {
            DebugTrace.Write("Input", "Key tap ignored because hwnd or vk is invalid.");
            return;
        }
        if (Method == InputMethod.Viiper)
        {
            var key = KeyName.FromVk(virtualKey);
            if (Viiper?.TapKey(key, holdMs) == true)
            {
                DebugTrace.Write("Input", $"VIIPER key sent key={key} holdMs={holdMs}.");
                return;
            }
            DebugTrace.Write("Input", $"VIIPER key failed/unavailable key={key}; trying FakerInput.");
            if (TryVirtualHidTap(hWnd, virtualKey, holdMs))
                return;
            if (!FallbackToNormalKeyWhenVirtualHidFails)
            {
                DebugTrace.Write("Input", $"Key tap stopped after VIIPER/FakerInput failure; fallback disabled key={key}.");
                return;
            }
            InputRuntimeStatus.SetLastKeyboard($"SendInput fallback {key}");
            DebugTrace.Write("Input", $"SendInput key fallback key={key}.");
            SendKey(virtualKey, false);
            Thread.Sleep(System.Math.Max(30, holdMs));
            SendKey(virtualKey, true);
            return;
        }
        if (Method == InputMethod.VirtualHid)
        {
            if (TryVirtualHidTap(hWnd, virtualKey, holdMs))
                return;
            if (!FallbackToNormalKeyWhenVirtualHidFails)
            {
                DebugTrace.Write("Input", $"Key tap stopped after FakerInput failure; fallback disabled key={KeyName.FromVk(virtualKey)}.");
                return;
            }
            InputRuntimeStatus.SetLastKeyboard($"SendInput fallback {KeyName.FromVk(virtualKey)}");
            DebugTrace.Write("Input", $"SendInput key fallback key={KeyName.FromVk(virtualKey)}.");
            SendKey(virtualKey, false);
            Thread.Sleep(System.Math.Max(30, holdMs));
            SendKey(virtualKey, true);
            return;
        }
        Down(hWnd, virtualKey);
        Thread.Sleep(System.Math.Max(30, holdMs));   // hold long enough for the game to register the press
        Up(hWnd, virtualKey);
    }

    public void TapSendInputFallback(IntPtr hWnd, int virtualKey, int holdMs = 0)
    {
        if (!CanAct($"SendInput fallback {KeyName.FromVk(virtualKey)}")) return;
        if (hWnd == IntPtr.Zero || virtualKey <= 0) return;
        InputRuntimeStatus.SetLastKeyboard($"SendInput fallback {KeyName.FromVk(virtualKey)}");
        SendKey(virtualKey, false);
        Thread.Sleep(System.Math.Max(30, holdMs));
        SendKey(virtualKey, true);
    }

    public void Down(IntPtr hWnd, int vk)
    {
        if (!CanAct($"Down {KeyName.FromVk(vk)}")) return;
        if (hWnd == IntPtr.Zero || vk <= 0) return;
        switch (Method)
        {
            case InputMethod.Viiper:
                if (Viiper?.TapKey(KeyName.FromVk(vk), 30) == true) return;
                if (VirtualHid?.TapKey(KeyName.FromVk(vk), 30) == true) return;
                if (!FallbackToNormalKeyWhenVirtualHidFails) return;
                InputRuntimeStatus.SetLastKeyboard($"SendInput fallback {KeyName.FromVk(vk)}");
                SendKey(vk, false);
                break;
            case InputMethod.VirtualHid:
                if (VirtualHid?.TapKey(KeyName.FromVk(vk), 30) == true) return;
                if (!FallbackToNormalKeyWhenVirtualHidFails) return;
                InputRuntimeStatus.SetLastKeyboard($"SendInput fallback {KeyName.FromVk(vk)}");
                SendKey(vk, false);
                break;
            case InputMethod.PostMessage:
                InputRuntimeStatus.SetLastKeyboard($"PostMessage {KeyName.FromVk(vk)}");
                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
                break;
            case InputMethod.MouseKeyEvent:
                InputRuntimeStatus.SetLastKeyboard($"keybd_event {KeyName.FromVk(vk)}");
                keybd_event((byte)vk, (byte)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC),
                    KEYEVENTF_SCANCODE | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0), IntPtr.Zero);
                break;
            default: // SendInput
                InputRuntimeStatus.SetLastKeyboard($"SendInput {KeyName.FromVk(vk)}");
                SendKey(vk, false);
                break;
        }
    }

    public void Up(IntPtr hWnd, int vk)
    {
        if (hWnd == IntPtr.Zero || vk <= 0) return;
        switch (Method)
        {
            case InputMethod.Viiper:
                break;
            case InputMethod.VirtualHid:
                break;
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
