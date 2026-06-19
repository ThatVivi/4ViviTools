using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace FourRVivi.App.Controls;

/// <summary>Click, then press any key (incl. NumPad) or a combo like Ctrl+F8 — records it as a string
/// that matches the global hotkey matcher (e.g. "Ctrl+F8", "NumPad8", "F9").</summary>
public class HotkeyCaptureBox : Button
{
    public static readonly StyledProperty<string> KeyTextProperty =
        AvaloniaProperty.Register<HotkeyCaptureBox, string>(nameof(KeyText), "", defaultBindingMode: BindingMode.TwoWay);

    public string KeyText { get => GetValue(KeyTextProperty); set => SetValue(KeyTextProperty, value); }

    private bool _cap;

    public HotkeyCaptureBox() { Focusable = true; UpdateContent(); }

    protected override void OnClick() { _cap = true; Content = "press keys…"; Focus(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_cap) { base.OnKeyDown(e); return; }
        if (IsMod(e.Key)) { e.Handled = true; return; }   // wait for a non-modifier
        string name = KeyName(e.Key);
        if (name.Length == 0) { e.Handled = true; return; }
        var m = e.KeyModifiers;
        string combo = (m.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "")
                     + (m.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
                     + (m.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") + name;
        KeyText = combo; _cap = false; UpdateContent(); e.Handled = true;
    }

    private void UpdateContent() => Content = string.IsNullOrEmpty(KeyText) ? "set key" : KeyText;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == KeyTextProperty && !_cap) UpdateContent();
    }

    private static bool IsMod(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string KeyName(Key k)
    {
        string n = k.ToString();
        if (n.Length == 2 && n[0] == 'D' && char.IsDigit(n[1])) return n.Substring(1);  // D0-D9 -> 0-9
        if (n == "Return") return "Enter";
        return n;  // A, F8, NumPad8, Space, Multiply, ...
    }
}
