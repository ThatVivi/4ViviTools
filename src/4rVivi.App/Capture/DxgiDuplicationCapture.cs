using System;
using System.Drawing;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace FourRVivi.App.Capture;

/// <summary>
/// GPU screen capture using DXGI Desktop Duplication (see the engineering guide §8 "Stage 1 –
/// Fix Capture Quality"). Per that guide the capture stage matters most for OCR accuracy: we use
/// DXGI Desktop Duplication instead of GDI / BitBlt / Graphics.CopyFromScreen, keep the native
/// BGRA8 desktop format, and copy raw rows straight into an <see cref="SKBitmap"/> with
/// <see cref="SKColorType.Bgra8888"/> — no intermediate Bitmap / PNG / JPEG conversion between
/// stages. The class is self-contained and depends only on Vortice.* + SkiaSharp +
/// System.Drawing.Common (for <see cref="System.Drawing.Rectangle"/>) + the BCL.
/// </summary>
public sealed class DxgiDuplicationCapture : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private IDXGIOutput1? _output1;

    // Reusable CPU-readable staging texture, recreated whenever the frame dimensions change.
    private ID3D11Texture2D? _staging;
    private int _stagingWidth;
    private int _stagingHeight;

    // Desktop dimensions discovered during init (full output bounds).
    private int _desktopWidth;
    private int _desktopHeight;

    private int _outputIndex;
    private bool _initialized;
    private bool _disposed;

    /// <summary>Description of the last failure (empty when the last operation succeeded).</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>True once <see cref="TryInit"/> has succeeded and the duplication is live.</summary>
    public bool IsInitialized => _initialized && !_disposed;

    /// <summary>
    /// Create the D3D11 device, obtain <see cref="IDXGIOutput1"/> for the requested monitor and
    /// start desktop duplication. Returns false (with <see cref="LastError"/> set) on any failure
    /// so the caller can fall back to the legacy GDI capture path.
    /// </summary>
    public bool TryInit(int outputIndex = 0)
    {
        if (_disposed) { LastError = "Capture object is disposed."; return false; }
        try
        {
            ReleaseDuplication();
            _outputIndex = outputIndex;

            // Create a hardware D3D11 device. BGRA support is requested because the desktop
            // surfaces are DXGI_FORMAT_B8G8R8A8_UNORM.
            var result = D3D11.D3D11CreateDevice(
                adapter: null,
                driverType: DriverType.Hardware,
                flags: DeviceCreationFlags.BgraSupport,
                featureLevels: null!,
                device: out _device,
                immediateContext: out _context);
            if (result.Failure || _device is null || _context is null)
            {
                LastError = $"D3D11CreateDevice failed: 0x{result.Code:X8}";
                ReleaseDuplication();
                return false;
            }

            // Walk DXGI: device -> adapter -> output[outputIndex] -> IDXGIOutput1.
            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            if (adapter.EnumOutputs((uint)outputIndex, out var output).Failure || output is null)
            {
                LastError = $"Adapter has no output at index {outputIndex}.";
                ReleaseDuplication();
                return false;
            }

            using (output)
            {
                var desc = output.Description;
                var bounds = desc.DesktopCoordinates;
                _desktopWidth = bounds.Right - bounds.Left;
                _desktopHeight = bounds.Bottom - bounds.Top;

                _output1 = output.QueryInterface<IDXGIOutput1>();
            }

            _duplication = _output1.DuplicateOutput(_device);
            if (_duplication is null)
            {
                LastError = "DuplicateOutput returned null.";
                ReleaseDuplication();
                return false;
            }

            _initialized = true;
            LastError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"TryInit failed: {ex.Message}";
            ReleaseDuplication();
            return false;
        }
    }

    /// <summary>
    /// Acquire the next desktop frame and copy it (BGRA8) into an <see cref="SKBitmap"/>. When
    /// <paramref name="region"/> is supplied the result is cropped to that screen-space rectangle.
    /// Returns null on timeout (no new frame), or when the duplication had to be re-initialised.
    /// On <c>DXGI_ERROR_ACCESS_LOST</c> the duplication is rebuilt automatically; on
    /// <c>DXGI_ERROR_WAIT_TIMEOUT</c> null is returned without error.
    /// </summary>
    public SKBitmap? Capture(Rectangle? region = null)
    {
        if (_disposed) { LastError = "Capture object is disposed."; return null; }
        if (!_initialized || _duplication is null || _device is null || _context is null)
        {
            LastError = "Capture called before successful TryInit.";
            return null;
        }

        IDXGIResource? desktopResource = null;
        ID3D11Texture2D? desktopTexture = null;
        bool frameAcquired = false;

        try
        {
            var acquire = _duplication.AcquireNextFrame(500, out _, out desktopResource);

            if (acquire == Vortice.DXGI.ResultCode.WaitTimeout)
            {
                // No screen change within the timeout window — not an error.
                LastError = string.Empty;
                return null;
            }
            if (acquire == Vortice.DXGI.ResultCode.AccessLost)
            {
                LastError = "DXGI_ERROR_ACCESS_LOST — re-initialising duplication.";
                TryInit(_outputIndex);
                return null;
            }
            if (acquire.Failure || desktopResource is null)
            {
                LastError = $"AcquireNextFrame failed: 0x{acquire.Code:X8}";
                return null;
            }
            frameAcquired = true;

            desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
            var texDesc = desktopTexture.Description;
            int srcW = (int)texDesc.Width;
            int srcH = (int)texDesc.Height;

            EnsureStaging(srcW, srcH, texDesc.Format);
            if (_staging is null)
            {
                LastError = "Failed to create staging texture.";
                return null;
            }

            // Copy the GPU desktop surface into a CPU-readable staging texture.
            _context.CopyResource(_staging, desktopTexture);

            var mapped = _context.Map(_staging, 0, Vortice.Direct3D11.MapMode.Read, MapFlags.None);
            try
            {
                // Determine the crop rectangle, clamped to the captured surface.
                var crop = region ?? new Rectangle(0, 0, srcW, srcH);
                int cx = Math.Max(0, crop.X);
                int cy = Math.Max(0, crop.Y);
                int cw = Math.Min(crop.Width, srcW - cx);
                int ch = Math.Min(crop.Height, srcH - cy);
                if (cw <= 0 || ch <= 0)
                {
                    LastError = "Requested region is empty or outside the desktop bounds.";
                    return null;
                }

                var info = new SKImageInfo(cw, ch, SKColorType.Bgra8888, SKAlphaType.Premul);
                var bitmap = new SKBitmap(info);
                try
                {
                    IntPtr dstBase = bitmap.GetPixels();
                    int dstStride = bitmap.RowBytes;          // tightly packed: cw * 4
                    int rowPitch = (int)mapped.RowPitch;      // source stride (may exceed srcW * 4)
                    IntPtr srcBase = mapped.DataPointer;
                    const int bytesPerPixel = 4;
                    int rowBytes = cw * bytesPerPixel;

                    unsafe
                    {
                        byte* src = (byte*)srcBase;
                        byte* dst = (byte*)dstBase;
                        for (int row = 0; row < ch; row++)
                        {
                            byte* srcRow = src + ((long)(cy + row) * rowPitch) + ((long)cx * bytesPerPixel);
                            byte* dstRow = dst + ((long)row * dstStride);
                            Buffer.MemoryCopy(srcRow, dstRow, dstStride, rowBytes);
                        }
                    }

                    LastError = string.Empty;
                    var produced = bitmap;
                    bitmap = null!; // ownership transferred to caller
                    return produced;
                }
                finally
                {
                    bitmap?.Dispose();
                }
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
        }
        catch (Exception ex)
        {
            LastError = $"Capture failed: {ex.Message}";
            return null;
        }
        finally
        {
            desktopTexture?.Dispose();
            desktopResource?.Dispose();
            if (frameAcquired)
            {
                try { _duplication?.ReleaseFrame(); } catch { /* ignore release races */ }
            }
        }
    }

    /// <summary>Create (or recreate) the CPU-readable staging texture for the given dimensions.</summary>
    private void EnsureStaging(int width, int height, Format format)
    {
        if (_staging is not null && _stagingWidth == width && _stagingHeight == height)
            return;

        _staging?.Dispose();
        _staging = null;

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };

        _staging = _device!.CreateTexture2D(desc);
        _stagingWidth = width;
        _stagingHeight = height;
    }

    private void ReleaseDuplication()
    {
        _initialized = false;

        try { _duplication?.ReleaseFrame(); } catch { /* a frame may not be held */ }

        _staging?.Dispose(); _staging = null;
        _stagingWidth = 0; _stagingHeight = 0;

        _duplication?.Dispose(); _duplication = null;
        _output1?.Dispose(); _output1 = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseDuplication();
    }
}
