namespace FourRVivi.Core.Input;

/// <summary>Selectable low-level input backend.</summary>
public enum InputMethod
{
    SendInput = 0,        // AHK "Send"/"Click" default; needs the window focused.
    MouseKeyEvent = 1,    // AHK "SendEvent"; legacy mouse_event/keybd_event.
    PostMessage = 2,      // AHK "ControlSend"/"ControlClick"; works unfocused when accepted.
    ReWasdClick = 3,      // Move cursor normally, then click through a ViGEm virtual Xbox button.
}
