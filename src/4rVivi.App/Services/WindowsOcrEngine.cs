using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace FourRVivi.App.Services;

/// <summary>Windows.Media.Ocr (built into Win10/11) — fast, accurate, no model download. Translumo's
/// recommended engine. Returns null on any failure so the caller can fall back to Tesseract.</summary>
public sealed class WindowsOcrEngine
{
    private readonly OcrEngine? _engine;
    public bool Available => _engine != null;

    public WindowsOcrEngine()
    {
        try { _engine = OcrEngine.TryCreateFromLanguage(new Language("en")) ?? OcrEngine.TryCreateFromUserProfileLanguages(); }
        catch { _engine = null; }
    }

    public string? Recognize(byte[] pngBytes)
    {
        if (_engine == null || pngBytes.Length == 0) return null;
        try { return RecognizeAsync(pngBytes).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private async Task<string?> RecognizeAsync(byte[] png)
    {
        using var ras = new InMemoryRandomAccessStream();
        using (var dw = new DataWriter(ras)) { dw.WriteBytes(png); await dw.StoreAsync(); await dw.FlushAsync(); dw.DetachStream(); }
        ras.Seek(0);
        var dec = await BitmapDecoder.CreateAsync(ras);
        using var sb = await dec.GetSoftwareBitmapAsync();
        var res = await _engine!.RecognizeAsync(sb);
        return res?.Text;
    }
}
