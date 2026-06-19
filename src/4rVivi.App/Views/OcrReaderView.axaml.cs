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
using FourRVivi.Core.Ocr;

namespace FourRVivi.App.Views;

public partial class OcrReaderView : UserControl
{
    private Bitmap? _bmp;
    private Point _start;
    private Rectangle? _temp;
    private bool _drawing;

    public OcrReaderView()
    {
        AvaloniaXamlLoader.Load(this);
        LoadBtn.Click += async (_, _) => await LoadImage();
        PasteBtn.Click += async (_, _) => await PasteImage();
        DrawCanvas.PointerPressed += OnPressed;
        DrawCanvas.PointerMoved += OnMoved;
        DrawCanvas.PointerReleased += OnReleased;
        AttachedToVisualTree += (_, _) => RenderMarks();
    }

    private OcrReaderViewModel? Vm => DataContext as OcrReaderViewModel;

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
        _bmp = bmp;
        Img.Source = bmp;
        double w = 720;
        double h = bmp.PixelSize.Width > 0 ? w * bmp.PixelSize.Height / bmp.PixelSize.Width : 405;
        DrawCanvas.Width = w; DrawCanvas.Height = h;
        Img.Width = w; Img.Height = h;
        RenderMarks();
    }

    private void OnPressed(object? s, PointerPressedEventArgs e)
    {
        if (_bmp is null) return;
        _start = e.GetPosition(DrawCanvas);
        _temp = new Rectangle { Stroke = Brushes.Aqua, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 255)) };
        Canvas.SetLeft(_temp, _start.X); Canvas.SetTop(_temp, _start.Y);
        DrawCanvas.Children.Add(_temp);
        _drawing = true;
    }

    private void OnMoved(object? s, PointerEventArgs e)
    {
        if (!_drawing || _temp is null) return;
        var p = e.GetPosition(DrawCanvas);
        double x = Math.Min(p.X, _start.X), y = Math.Min(p.Y, _start.Y);
        Canvas.SetLeft(_temp, x); Canvas.SetTop(_temp, y);
        _temp.Width = Math.Abs(p.X - _start.X); _temp.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        if (!_drawing || _temp is null) { _drawing = false; return; }
        _drawing = false;
        double cw = DrawCanvas.Width, ch = DrawCanvas.Height;
        double x = Canvas.GetLeft(_temp), y = Canvas.GetTop(_temp), w = _temp.Width, h = _temp.Height;
        DrawCanvas.Children.Remove(_temp); _temp = null;
        if (cw <= 0 || ch <= 0 || w < 4 || h < 4) return;
        Vm?.AddMark(Vm.SelectedRole, x / cw, y / ch, w / cw, h / ch);
        RenderMarks();
    }

    private void RenderMarks()
    {
        for (int i = DrawCanvas.Children.Count - 1; i >= 0; i--)
            if (DrawCanvas.Children[i] is not Image) DrawCanvas.Children.RemoveAt(i);

        if (Vm is null || DrawCanvas.Width <= 0) return;
        double cw = DrawCanvas.Width, ch = DrawCanvas.Height;
        foreach (var m in Vm.Marks)
        {
            var box = new Rectangle { Stroke = Brushes.Lime, StrokeThickness = 2, Width = m.W * cw, Height = m.H * ch };
            Canvas.SetLeft(box, m.X * cw); Canvas.SetTop(box, m.Y * ch);
            DrawCanvas.Children.Add(box);
            var lbl = new TextBlock { Text = m.Role, Foreground = Brushes.Lime, FontSize = 11 };
            Canvas.SetLeft(lbl, m.X * cw + 2); Canvas.SetTop(lbl, m.Y * ch);
            DrawCanvas.Children.Add(lbl);
        }
    }
}
