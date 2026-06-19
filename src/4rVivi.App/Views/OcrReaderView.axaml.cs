using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FourRVivi.App.ViewModels;
using System.ComponentModel;
using FourRVivi.App.Services;

namespace FourRVivi.App.Views;

public partial class OcrReaderView : UserControl
{
    private Canvas? _canvas;
    private Image? _img;
    private Bitmap? _bmp;
    private Point _start;
    private Rectangle? _temp;
    private bool _drawing;
    private bool _wired;

    public OcrReaderView()
    {
        AvaloniaXamlLoader.Load(this);
        Focusable = true;
        Loaded += OnViewLoaded;
        KeyDown += OnKeyDown;
        PointerPressed += (_, _) => Focus();
    }

    private OcrReaderViewModel? Vm => DataContext as OcrReaderViewModel;

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (_wired) { RenderMarks(); return; }
        try
        {
            _canvas = this.FindControl<Canvas>("DrawCanvas");
            _img = this.FindControl<Image>("Img");
            if (_canvas != null)
            {
                _canvas.PointerPressed += OnPressed;
                _canvas.PointerMoved += OnMoved;
                _canvas.PointerReleased += OnReleased;
            }
            _wired = true;
            if (Vm != null) Vm.PropertyChanged += OnVmPropertyChanged;
            PopulateMonitors();
            ApplySize();
        }
        catch { }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _ = PasteImage(); e.Handled = true; return; }
        if (Vm != null && !string.IsNullOrEmpty(Vm.OverlayHotkey) &&
            string.Equals(e.Key.ToString(), Vm.OverlayHotkey, StringComparison.OrdinalIgnoreCase))
        { Vm.ToggleOverlayCommand.Execute(null); e.Handled = true; }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Zoom") ApplySize();
    }

    private const double BaseWidth = 900;
    private void ApplySize()
    {
        if (_canvas is null) return;
        double z = Vm?.Zoom ?? 1.0; if (z <= 0) z = 1;
        double w = BaseWidth * z;
        double h = (_bmp != null && _bmp.PixelSize.Width > 0) ? w * _bmp.PixelSize.Height / _bmp.PixelSize.Width : 506 * z;
        _canvas.Width = w; _canvas.Height = h;
        if (_img != null) { _img.Width = w; _img.Height = h; }
        RenderMarks();
    }

    private void PopulateMonitors()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            var screens = top?.Screens;
            if (screens == null || Vm == null || Vm.Monitors.Count > 0) return;
            int i = 1;
            foreach (var sc in screens.All)
            {
                var b = sc.Bounds;
                Vm.Monitors.Add(new MonitorInfo { Name = $"Screen {i} — {b.Width}x{b.Height}", X = b.X, Y = b.Y, W = b.Width, H = b.Height });
                i++;
            }
            if (Vm.SelectedMonitor == null && Vm.Monitors.Count > 0) Vm.SelectedMonitor = Vm.Monitors[0];
        }
        catch { }
    }

    private async void OnCaptureMonitorClick(object? sender, RoutedEventArgs e)
    {
        var win = TopLevel.GetTopLevel(this) as Window;
        var prev = win?.WindowState ?? WindowState.Normal;
        try
        {
            if (Vm?.SelectedMonitor == null) { if (Vm != null) Vm.Status = "Pick a monitor first."; return; }
            if (win != null) win.WindowState = WindowState.Minimized;   // hide our tool before the shot
            await System.Threading.Tasks.Task.Delay(350);
            var sd = Vm.GrabMonitor();
            if (win != null) { win.WindowState = prev; win.Activate(); }
            if (sd != null)
            {
                using var ms = new System.IO.MemoryStream();
                sd.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                SetBitmap(new Bitmap(ms));
                sd.Dispose();
            }
        }
        catch { if (win != null) win.WindowState = prev; }
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e) => await LoadImage();
    private async void OnPasteClick(object? sender, RoutedEventArgs e) => await PasteImage();

    private async System.Threading.Tasks.Task LoadImage()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load a game screenshot", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } } }
            });
            if (files.Count == 0) return;
            await using var st = await files[0].OpenReadAsync();
            SetBitmap(new Bitmap(st));
        }
        catch { }
    }

    private async System.Threading.Tasks.Task PasteImage()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null) return;
            foreach (var fmt in new[] { "PNG", "image/png", "Bitmap" })
            {
                var data = await top.Clipboard.GetDataAsync(fmt);
                if (data is byte[] bytes && bytes.Length > 0)
                {
                    using var ms = new System.IO.MemoryStream(bytes);
                    SetBitmap(new Bitmap(ms));
                    return;
                }
            }
        }
        catch { }
    }

    private void SetBitmap(Bitmap bmp)
    {
        if (_canvas is null || _img is null) return;
        _bmp = bmp;
        _img.Source = bmp;
        ApplySize();
    }

    private void OnPressed(object? s, PointerPressedEventArgs e)
    {
        if (_bmp is null || _canvas is null) return;
        _start = e.GetPosition(_canvas);
        var rc = RolePalette.ColorFor(Vm?.SelectedRole ?? "HP");
        _temp = new Rectangle { Stroke = new SolidColorBrush(rc), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(48, rc.R, rc.G, rc.B)) };
        Canvas.SetLeft(_temp, _start.X); Canvas.SetTop(_temp, _start.Y);
        _canvas.Children.Add(_temp);
        _drawing = true;
    }

    private void OnMoved(object? s, PointerEventArgs e)
    {
        if (!_drawing || _temp is null || _canvas is null) return;
        var p = e.GetPosition(_canvas);
        double x = Math.Min(p.X, _start.X), y = Math.Min(p.Y, _start.Y);
        Canvas.SetLeft(_temp, x); Canvas.SetTop(_temp, y);
        _temp.Width = Math.Abs(p.X - _start.X); _temp.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        if (!_drawing || _temp is null || _canvas is null) { _drawing = false; return; }
        _drawing = false;
        double cw = _canvas.Width, ch = _canvas.Height;
        double x = Canvas.GetLeft(_temp), y = Canvas.GetTop(_temp), w = _temp.Width, h = _temp.Height;
        _canvas.Children.Remove(_temp); _temp = null;
        if (cw <= 0 || ch <= 0 || w < 4 || h < 4) return;
        Vm?.AddMark(Vm.SelectedRole, x / cw, y / ch, w / cw, h / ch);
        RenderMarks();
    }

    private void RenderMarks()
    {
        if (_canvas is null || Vm is null || _canvas.Width <= 0) return;
        for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            if (_canvas.Children[i] is not Image) _canvas.Children.RemoveAt(i);

        double cw = _canvas.Width, ch = _canvas.Height;
        foreach (var m in Vm.Marks)
        {
            var box = new Rectangle { Stroke = RolePalette.Brush(m.Role), StrokeThickness = 2, Width = m.W * cw, Height = m.H * ch };
            Canvas.SetLeft(box, m.X * cw); Canvas.SetTop(box, m.Y * ch);
            _canvas.Children.Add(box);
            var lbl = new TextBlock { Text = m.Role, Foreground = RolePalette.Brush(m.Role), FontSize = 11, FontWeight = FontWeight.Bold };
            Canvas.SetLeft(lbl, m.X * cw + 2); Canvas.SetTop(lbl, m.Y * ch);
            _canvas.Children.Add(lbl);
        }
    }
}
