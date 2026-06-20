using RapidOcrNet;
using SkiaSharp;

// Persistent OCR worker. Protocol over stdio (one request per line):
//   "<imagePath>\t<mode>"       -> prints "OK\t<text>"   (mode: 0=normal text, 1=digits-only)
//   "CFG\t<k=v;k=v;...>"        -> rebuild RapidOcrOptions from defaults, no reply
//   "QUIT"                       -> exits
// Runs PaddleOCR PP-OCRv5 (via RapidOcrNet/ONNX) in its own process so SkiaSharp 3
// never clashes with the host app's Avalonia/Skia.

namespace FourRVivi.OcrServer;

internal static class Program
{
    private static void Main()
    {
        int.TryParse(Environment.GetEnvironmentVariable("OCR_CPU_THREADS"), out int threads);

        RapidOcr? ocr = null;
        try { ocr = new RapidOcr(); ocr.InitModels(threads); }
        catch { Console.Out.WriteLine("ERR\tinit"); }

        var opts = RapidOcrOptions.Default;
        string? line;
        while ((line = Console.In.ReadLine()) != null)
        {
            if (line == "QUIT") break;
            if (line.StartsWith("CFG\t"))
            {
                opts = ApplyCfg(opts, line.Substring(4));
                continue;
            }
            string text = "";
            try
            {
                var path = line.Split('\t')[0];
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

    private static RapidOcrOptions ApplyCfg(RapidOcrOptions o, string kv)
    {
        float boxThresh = o.BoxThresh, boxScore = o.BoxScoreThresh, unclip = o.UnClipRatio, textScore = o.TextScore;
        int maxSide = o.MaxSideLen;
        foreach (var pair in kv.Split(';'))
        {
            var i = pair.IndexOf('=');
            if (i < 0) continue;
            var k = pair.Substring(0, i); var v = pair.Substring(i + 1);
            switch (k)
            {
                case "boxThresh": float.TryParse(v, out boxThresh); break;
                case "boxScore": float.TryParse(v, out boxScore); break;
                case "unclip": float.TryParse(v, out unclip); break;
                case "textScore": float.TryParse(v, out textScore); break;
                case "maxSide": int.TryParse(v, out maxSide); break;
            }
        }
        return o with { BoxThresh = boxThresh, BoxScoreThresh = boxScore, UnClipRatio = unclip, TextScore = textScore, MaxSideLen = maxSide };
    }
}
