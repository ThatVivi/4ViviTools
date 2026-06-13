using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace FourRVivi.App.Controls;

/// <summary>Click it, press a key — it records the key name (F1, A, 1, SPACE…) instead of typing.</summary>
public class KeyCaptureBox : Button
{
    public static readonly StyledProperty<string> KeyTextProperty =
        AvaloniaProperty.Register<KeyCaptureBox, string>(nameof(KeyText), "",
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string KeyText { get => GetValue(KeyTextProperty); set => SetValue(KeyTextProperty, value); }

    private bool _capturing;

    public KeyCaptureBox() { Focusable = true; Width = double.IsNaN(Width) ? 70 : Width; UpdateContent(); }

    protected override void OnClick() { _capturing = true; Content = "press…"; Focus(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_capturing)
        {
            var k = Map(e.Key);
            if (k != null) KeyText = k;
            _capturing = false; UpdateContent(); e.Handled = true;
        }
        else base.OnKeyDown(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == KeyTextProperty && !_capturing) UpdateContent();
    }

    private void UpdateContent() => Content = string.IsNullOrEmpty(KeyText) ? "—" : KeyText;

    public static string? Map(Key k)
    {
        if (k >= Key.F1 && k <= Key.F24) return "F" + ((int)k - (int)Key.F1 + 1);
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((int)k - (int)Key.D0).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((int)k - (int)Key.NumPad0).ToString();
        return k switch
        {
            Key.Space => "SPACE", Key.Enter => "ENTER", Key.Tab => "TAB",
            Key.Insert => "INSERT", Key.Delete => "DELETE", Key.Home => "HOME", Key.End => "END",
            Key.PageUp => "PAGEUP", Key.PageDown => "PAGEDOWN",
            Key.Up => "UP", Key.Down => "DOWN", Key.Left => "LEFT", Key.Right => "RIGHT",
            _ => null
        };
    }
}
