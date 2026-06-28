using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Controls;

/// <summary>A search box that drops a filtered, icon'd list of strings. Reliable replacement for
/// AutoCompleteBox: type to filter (Contains), click a row to pick. Two-way <see cref="Text"/>.</summary>
public partial class SearchPicker : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchPicker, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchPicker, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<SearchPicker, string?>(nameof(Watermark));

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Watermark { get => GetValue(WatermarkProperty); set => SetValue(WatermarkProperty, value); }

    private TextBox _tb = null!;
    private Popup _pop = null!;
    private ListBox _list = null!;
    private bool _sync;

    public SearchPicker()
    {
        InitializeComponent();
        _tb = this.FindControl<TextBox>("PART_Text")!;
        _pop = this.FindControl<Popup>("PART_Popup")!;
        _list = this.FindControl<ListBox>("PART_List")!;

        _tb.GetObservable(TextBox.TextProperty).Subscribe(new AnonObserver<string?>(OnTyped));
        this.GetObservable(TextProperty).Subscribe(new AnonObserver<string?>(v =>
        {
            if (_sync) return;
            _sync = true; _tb.Text = v; _sync = false;
        }));
        this.GetObservable(WatermarkProperty).Subscribe(new AnonObserver<string?>(v => _tb.Watermark = v));

        _list.SelectionChanged += (_, _) =>
        {
            if (_sync) return;                       // ignore programmatic selection clears
            if (_list.SelectedItem is string s)
            {
                _sync = true;
                try
                {
                    Text = s;
                    _tb.Text = s;
                    _tb.CaretIndex = s.Length;
                    _pop.IsOpen = false;
                    _list.SelectedItem = null;       // safe: guarded by _sync above
                }
                finally { _sync = false; }
            }
        };
        // Show the list as soon as the box is focused (reflects the current ItemsSource, e.g. after a class change).
        _tb.GotFocus += (_, _) => { if (!_sync) Filter(_tb.Text); };
    }

    /// <summary>Text observer — only reacts to USER typing (skips programmatic sets to avoid reentrancy).</summary>
    private void OnTyped(string? typed)
    {
        if (_sync) return;          // a programmatic set (selection / bound value) — do not touch the list
        Text = typed;               // push to bound VM property
        Filter(typed);
    }

    /// <summary>Rebuild the filtered list + open the popup. Never called while _sync is set.</summary>
    private void Filter(string? typed)
    {
        try
        {
            var q = typed ?? "";
            var src = (ItemsSource ?? System.Array.Empty<string>()).Cast<object?>()
                        .Select(o => o?.ToString() ?? "");
            List<string> matches = q.Length == 0
                ? src.Take(80).ToList()
                : src.Where(s => s.Contains(q, System.StringComparison.OrdinalIgnoreCase)).Take(80).ToList();

            _list.ItemsSource = matches;
            if (_tb.Bounds.Width > 0) _pop.Width = _tb.Bounds.Width;
            _pop.IsOpen = matches.Count > 0 && _tb.IsFocused;
        }
        catch { }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Tiny IObserver so we can subscribe without Rx.</summary>
    private sealed class AnonObserver<T> : System.IObserver<T>
    {
        private readonly System.Action<T> _on;
        public AnonObserver(System.Action<T> on) => _on = on;
        public void OnCompleted() { }
        public void OnError(System.Exception error) { }
        public void OnNext(T value) => _on(value);
    }
}
