namespace FourRVivi.Core.Input;

/// <summary>Selectable low-level input backend (all standard Windows APIs — the same ones AutoHotkey
/// exposes). Different servers/clients accept different ones, so the user can switch and test.</summary>
public enum InputMethod
{
    SendInput = 0,        // AHK "Send"/"Click" default — synthesizes real OS input (needs the window focused)
    MouseKeyEvent = 1,    // AHK "SendEvent" — legacy mouse_event/keybd_event (also focused)
    PostMessage = 2,      // AHK "ControlSend"/"ControlClick" — posts window messages (works unfocused)
}
