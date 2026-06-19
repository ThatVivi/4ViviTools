using RapidOcrNet;
using SkiaSharp;

// Persistent OCR worker. Protocol over stdio (one request per line):
//   "<imagePath>\t<mode>"  -> prints "OK\t<text>"   (mode: 0=normal text, 1=digits-only)
//   "QUIT"                  -> exits
// Runs PaddleOCR PP-OCRv5 (via RapidOcrNet/ONNX) in its own process so SkiaSharp 3
// never clashes with the host app's Avalonia/Skia.

namespace FourRVivi.OcrServer;

internal static class Program
{
    private static void Main()
    {
        RapidOcr? ocr = null;
        try { ocr = new RapidOcr(); ocr.InitModels(); }
        catch { Console.Out.WriteLine("ERR\tinit"); }

        var opts = RapidOcrOptions.Default;
        string? line;
        while ((line = Console.In.ReadLine()) != null)
        {
            if (line == "QUIT") break;
            string text = "";
            try
            {
                var parts = line.Split('\t');
                string path = parts[0];
                if (ocr != null && File.Exists(path))
                {
                    using var bmp = SKBitmap.Decode(path);
                    if (bmp != null)
                    {
                        var r = ocr.Detect(bmp, opts);
                        text = (r.StrRes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                    }
                }
            }
            catch { text = ""; }
            Console.Out.WriteLine("OK\t" + text);
            Console.Out.Flush();
        }
        ocr?.Dispose();
    }
}
