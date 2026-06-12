using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Grf;

namespace FourRVivi.App.ViewModels;

/// <summary>Opens .spr (renders frames), or .act/.rsm (shows metadata).</summary>
public sealed partial class SpriteViewerViewModel : ViewModelBase
{
    private List<SpriteFrame> _frames = new();
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private int _frameIndex;
    [ObservableProperty] private int _frameCount;
    [ObservableProperty] private string _info = "Open a .spr to view sprite frames, or a .act / .rsm to read its info.";

    [RelayCommand] private void Open()
    {
        try
        {
            byte[] data = System.IO.File.ReadAllBytes(Path);
            string ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            if (ext == ".spr")
            {
                _frames = SprReader.Decode(data);
                FrameCount = _frames.Count; FrameIndex = 0;
                Info = $"SPR: {_frames.Count} frames.";
                ShowFrame();
            }
            else if (ext == ".act") { Info = ModelInfo.Act(data); Image = null; FrameCount = 0; }
            else if (ext == ".rsm") { Info = ModelInfo.Rsm(data); Image = null; FrameCount = 0; }
            else Info = "Unsupported file type (use .spr / .act / .rsm).";
        }
        catch (Exception e) { Info = "Open failed: " + e.Message; }
    }

    [RelayCommand] private void Next() { if (FrameCount > 0) { FrameIndex = (FrameIndex + 1) % FrameCount; ShowFrame(); } }
    [RelayCommand] private void Prev() { if (FrameCount > 0) { FrameIndex = (FrameIndex - 1 + FrameCount) % FrameCount; ShowFrame(); } }

    private void ShowFrame()
    {
        if (FrameIndex < 0 || FrameIndex >= _frames.Count) return;
        var f = _frames[FrameIndex];
        if (f.Width <= 0 || f.Height <= 0) return;
        var bmp = new WriteableBitmap(new PixelSize(f.Width, f.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
        {
            var bgra = new byte[f.Rgba.Length];
            for (int i = 0; i < f.Rgba.Length; i += 4)
            {
                bgra[i] = f.Rgba[i + 2]; bgra[i + 1] = f.Rgba[i + 1]; bgra[i + 2] = f.Rgba[i]; bgra[i + 3] = f.Rgba[i + 3];
            }
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        }
        Image = bmp;
    }
}
