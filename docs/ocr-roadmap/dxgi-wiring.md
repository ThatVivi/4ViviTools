# DXGI Desktop Duplication capture — wiring note (roadmap #38)

Implements **Stage 1 ("Fix Capture Quality")** from the OCR engineering guide §8: capture quality
matters most; use **DXGI Desktop Duplication**, never GDI / BitBlt / `Graphics.CopyFromScreen`;
keep the native **BGRA8** format; avoid Bitmap/PNG/JPEG conversions between stages.

New file: `src/4rVivi.App/Capture/DxgiDuplicationCapture.cs`
(namespace `FourRVivi.App.Capture`). Produces an `SKBitmap` (`SKColorType.Bgra8888`) directly from
the GPU surface, with optional region crop, automatic re-init on `DXGI_ERROR_ACCESS_LOST`, and a
public `LastError` for diagnostics.

---

## 1. NuGet packages

Add to **`src/4rVivi.App/4rVivi.App.csproj`** (the `<ItemGroup>` that already lists the other
`PackageReference` entries). Latest stable for net8 at time of writing is the Vortice.Windows
**3.6.x** line; both packages must use the same version.

```xml
<PackageReference Include="Vortice.DXGI" Version="3.6.2" />
<PackageReference Include="Vortice.Direct3D11" Version="3.6.2" />
```

Notes:
- `Vortice.Direct3D11` transitively pulls in `Vortice.Direct3D` and `Vortice.DXGI`, but pin both
  explicitly so they cannot drift to mismatched versions.
- No other packages are required. `SkiaSharp` is already referenced (the OCR pipeline uses
  `SKBitmap`), and `System.Drawing.Common` 8.0.10 is already referenced (used only for
  `System.Drawing.Rectangle`).
- The class uses `unsafe` row copies (`Buffer.MemoryCopy` over `byte*`). The App project must allow
  unsafe blocks. If it is not already enabled, add to the same `<PropertyGroup>` as
  `<TargetFramework>` in `4rVivi.App.csproj`:

  ```xml
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  ```

---

## 2. Where the current capture happens

The existing (legacy/GDI) capture path is:

- **`src/4rVivi.App/Services/OcrService.cs`**
  - `public System.Drawing.Bitmap? CaptureWindow(IntPtr hwnd)` — uses `PrintWindow`, falls back to
    `Graphics.CopyFromScreen`.
  - `public System.Drawing.Bitmap? CaptureMonitor(int x, int y, int w, int h)` — uses
    `Graphics.CopyFromScreen`.
- These are called from **`src/4rVivi.App/ViewModels/OcrReaderViewModel.cs`**:
  - `private System.Drawing.Bitmap? CaptureFrame(IntPtr hwnd)` — the single chokepoint that decides
    monitor-vs-window and returns the frame the OCR loop consumes.
  - `public System.Drawing.Bitmap? GrabMonitor()` — calibration screenshot button.

`CaptureFrame` is the cleanest single place to wire in DXGI as a **fallback-on-failure** (DXGI
preferred, GDI used if `TryInit` fails or DXGI returns null).

---

## 3. Integration (paste-ready)

The rest of the OCR pipeline (`ReadRectFrom`, `Preprocess`) currently consumes
`System.Drawing.Bitmap`. To keep the change minimal and behind a toggle, convert the DXGI
`SKBitmap` to a `System.Drawing.Bitmap` only at this boundary (a longer-term Stage 1 follow-up is to
make the pipeline consume `SKBitmap`/`SKImage` end-to-end so the conversion disappears entirely —
that is the actual "avoid Bitmap conversion between stages" win).

### 3a. Add a toggle + lazy DXGI instance to `OcrReaderViewModel`

```csharp
using FourRVivi.App.Capture;        // add with the other usings

// --- new fields/properties on OcrReaderViewModel ---

/// <summary>When true, prefer DXGI Desktop Duplication (guide §8 Stage 1); fall back to GDI.</summary>
[ObservableProperty] private bool _useDxgiCapture = true;

private DxgiDuplicationCapture? _dxgi;
private bool _dxgiInitTried;
private bool _dxgiAvailable;

private DxgiDuplicationCapture? EnsureDxgi(int outputIndex)
{
    if (!UseDxgiCapture) return null;
    if (!_dxgiInitTried)
    {
        _dxgiInitTried = true;
        _dxgi = new DxgiDuplicationCapture();
        _dxgiAvailable = _dxgi.TryInit(outputIndex);
        if (!_dxgiAvailable)
        {
            // Log _dxgi.LastError, then drop back to GDI for the rest of the session.
            _dxgi.Dispose();
            _dxgi = null;
        }
    }
    return _dxgiAvailable ? _dxgi : null;
}
```

### 3b. Make `CaptureFrame` try DXGI first, fall back to the existing path

```csharp
private System.Drawing.Bitmap? CaptureFrame(IntPtr hwnd)
{
    // DXGI Desktop Duplication first (guide §8 Stage 1: never GDI when DXGI is available).
    if (UseMonitor && SelectedMonitor != null)
    {
        int outputIndex = SelectedMonitor.Index;   // map MonitorInfo -> DXGI output index
        var dxgi = EnsureDxgi(outputIndex);
        if (dxgi != null)
        {
            // Region is monitor-local; pass null for the full monitor, or a crop rect if desired.
            using var sk = dxgi.Capture(region: null);
            if (sk != null)
                return SkToGdi(sk);                 // helper below
            // DXGI returned null (timeout / access lost) -> fall through to GDI this frame.
        }
    }

    // Legacy fallback (also used for window-mode capture, which DXGI does not target).
    return (UseMonitor && SelectedMonitor != null)
        ? _ocr.CaptureMonitor(SelectedMonitor.X, SelectedMonitor.Y, SelectedMonitor.W, SelectedMonitor.H)
        : _ocr.CaptureWindow(hwnd);
}

private static System.Drawing.Bitmap SkToGdi(SkiaSharp.SKBitmap sk)
{
    // BGRA8 -> 32bpp ARGB GDI bitmap (temporary bridge until the pipeline consumes SKBitmap).
    var bmp = new System.Drawing.Bitmap(sk.Width, sk.Height,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    var rect = new System.Drawing.Rectangle(0, 0, sk.Width, sk.Height);
    var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try
    {
        int rowBytes = sk.Width * 4;
        unsafe
        {
            byte* src = (byte*)sk.GetPixels();
            byte* dst = (byte*)data.Scan0;
            for (int y = 0; y < sk.Height; y++)
                System.Buffer.MemoryCopy(src + (long)y * rowBytes, dst + (long)y * data.Stride,
                    data.Stride, rowBytes);
        }
    }
    finally { bmp.UnlockBits(data); }
    return bmp;
}
```

> `SelectedMonitor.Index` is assumed to map 1:1 to the DXGI output index on the primary adapter.
> If `MonitorInfo` does not expose an `Index`, pass `0` (primary output) for now, or add the index
> when enumerating monitors. DXGI output ordering = adapter output enumeration order, which matches
> the OS monitor order on single-adapter machines.

### 3c. Dispose

Dispose the DXGI instance when the view model is torn down (wherever the loop/timer is stopped):

```csharp
_dxgi?.Dispose();
_dxgi = null;
```

---

## 4. Behaviour summary

- **DXGI preferred, GDI fallback.** If `TryInit` fails (no DXGI support, RDP session, secure
  desktop, GPU driver quirk), the code disposes the DXGI object and uses the existing
  `CaptureMonitor` / `CaptureWindow` path for the whole session. Current capture is fully retained
  as the fallback.
- **Per-frame resilience.** `Capture` returns null on `DXGI_ERROR_WAIT_TIMEOUT` (no screen change)
  and on `DXGI_ERROR_ACCESS_LOST` (after auto re-init) — both cases fall through to GDI for that one
  frame, so the OCR loop never blanks out.
- **Window-mode capture is unchanged.** DXGI duplicates a whole *output* (monitor), not an
  individual occluded window, so `CaptureWindow(hwnd)` (PrintWindow) remains the path for game-window
  mode. DXGI only replaces the monitor-capture path.
- **Toggle.** `UseDxgiCapture` (default true) lets the user/QA flip back to pure GDI without a
  rebuild — bind it to a settings checkbox if desired.

## 5. Follow-up (true Stage 1 win)

The `SkToGdi` bridge reintroduces one Bitmap copy. The guide's full gain comes from making
`ReadRectFrom` / `Preprocess` accept `SKBitmap` (or feeding the mapped BGRA buffer straight into the
OCR/OpenCV stage as a `Mat`), eliminating the GDI bitmap and PNG round-trips entirely. Track that as
a separate roadmap item.
