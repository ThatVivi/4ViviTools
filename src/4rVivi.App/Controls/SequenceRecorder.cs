using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace FourRVivi.App.Controls;

/// <summary>Click to start, then press keys; each is appended to the bound comma-separated Sequence. Click again to stop.</summary>
public class SequenceRecorder : Button
{
    public static readonly StyledProperty<string> SequenceProperty =
        AvaloniaProperty.Register<SequenceRecorder, string>(nameof(Sequence), "",
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string Sequence { get => GetValue(SequenceProperty); set => SetValue(SequenceProperty, value); }
    private bool _recording;

    public SequenceRecorder() { Focusable = true; Content = "Record"; }

    protected override void OnClick()
    {
        _recording = !_recording;
        if (_recording) { Sequence = ""; Content = "recording... click to stop"; Focus(); }
        else Content = "Record";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_recording)
        {
            var k = KeyCaptureBox.Map(e.Key);
            if (k != null) Sequence = string.IsNullOrEmpty(Sequence) ? k : Sequence + ", " + k;
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }
}
