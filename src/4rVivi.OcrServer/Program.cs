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

        IconRecognizer? icons = null;
        try { icons = new IconRecognizer(threads); } catch { icons = null; }

        EntityDetector? det = null;
        try { det = new EntityDetector(threads); } catch { det = null; }

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
            if (line.StartsWith("ICON\t"))
            {
                string ir = "OK\t\t0";
                try
                {
                    var ipath = line.Substring(5).Split('\t')[0];
                    if (icons != null && icons.Available && File.Exists(ipath))
                    {
                        using var ib = SKBitmap.Decode(ipath);
                        var m = ib != null ? icons.Recognize(ib) : null;
                        if (m.HasValue)
                            ir = "OK\t" + m.Value.label + "\t" +
                                 m.Value.score.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch { }
                Console.Out.WriteLine(ir);
                Console.Out.Flush();
                continue;
            }
            if (line.StartsWith("DETECT\t"))
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new System.Text.StringBuilder("OK\t");
                try
                {
                    var dpath = line.Substring(7).Split('\t')[0];
                    if (det != null && det.Available && File.Exists(dpath))
                    {
                        using var dbmp = SKBitmap.Decode(dpath);
                        if (dbmp != null)
                            foreach (var b in det.Detect(dbmp))
                            {
                                string cls = (b.ClassId >= 0 && b.ClassId < det.ClassNames.Count) ? det.ClassNames[b.ClassId] : "";
                                bool isMonster = cls.Length == 0
                                    || cls.Equals("monster", StringComparison.OrdinalIgnoreCase)
                                    || cls.Equals("entity", StringComparison.OrdinalIgnoreCase);
                                string lbl = ""; float ls = 0f;
                                // Only the icon embedder names a monster sprite; loot/portal/target/HP are already identified by class.
                                if (isMonster && icons != null && icons.Available)
                                {
                                    using var crop = new SKBitmap(b.W, b.H);
                                    if (dbmp.ExtractSubset(crop, new SKRectI(b.X, b.Y, b.X + b.W, b.Y + b.H)))
                                    {
                                        var m = icons.Recognize(crop);
                                        if (m.HasValue) { lbl = m.Value.label; ls = m.Value.score; }
                                    }
                                }
                                sb.Append(b.X).Append(',').Append(b.Y).Append(',').Append(b.W).Append(',').Append(b.H)
                                  .Append(',').Append(b.Score.ToString(inv)).Append(',').Append(lbl)
                                  .Append(',').Append(ls.ToString(inv)).Append(',').Append(cls).Append(';');
                            }
                    }
                }
                catch { }
                Console.Out.WriteLine(sb.ToString());
                Console.Out.Flush();
                continue;
            }
            if (line.StartsWith("SCAN\t"))
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new System.Text.StringBuilder("OK\t");
                try
                {
                    var spath = line.Substring(5).Split('\t')[0];
                    if (ocr != null && File.Exists(spath))
                    {
                        using var sbmp = SKBitmap.Decode(spath);
                        if (sbmp != null)
                            foreach (var tb in ocr.Detect(sbmp, opts).TextBlocks)
                            {
                                if (tb.BoxPoints == null || tb.BoxPoints.Length < 4) continue;
                                int minx = int.MaxValue, miny = int.MaxValue, maxx = int.MinValue, maxy = int.MinValue;
                                foreach (var pt in tb.BoxPoints)
                                { if (pt.X < minx) minx = pt.X; if (pt.Y < miny) miny = pt.Y; if (pt.X > maxx) maxx = pt.X; if (pt.Y > maxy) maxy = pt.Y; }
                                string b64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tb.Text ?? ""));
                                sb.Append(minx).Append(',').Append(miny).Append(',').Append(maxx - minx).Append(',').Append(maxy - miny)
                                  .Append(',').Append(tb.BoxScore.ToString(inv)).Append(',').Append(b64).Append(';');
                            }
                    }
                }
                catch { }
                Console.Out.WriteLine(sb.ToString());
                Console.Out.Flush();
                continue;
            }
            if (line.StartsWith("REC\t"))
            {
                string rt = ""; float rc = 0f;
                try
                {
                    var rp = line.Substring(4).Split('\t')[0];
                    if (ocr != null && File.Exists(rp))
                    {
                        using var rb = SKBitmap.Decode(rp);
                        if (rb != null) { var (tx, sc) = ocr.RecognizeLine(rb); rt = (tx ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); rc = sc; }
                    }
                }
                catch { rt = ""; rc = 0f; }
                Console.Out.WriteLine("OK\t" + rc.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t" + rt);
                Console.Out.Flush();
                continue;
            }
            string text = "";
            float conf = 0f;
            try
            {
                var path = line.Split('\t')[0];
                if (ocr != null && File.Exists(path))
                {
                    using var bmp = SKBitmap.Decode(path);
                    if (bmp != null)
                    {
                        var r = ocr.Detect(bmp, opts);
                        if (r.TextBlocks != null && r.TextBlocks.Length > 0)
                        {
                            double sum = 0; int cnt = 0;
                            foreach (var tb in r.TextBlocks)
                            {
                                if (tb.CharScores != null && tb.CharScores.Length > 0)
                                    foreach (var cs in tb.CharScores) { sum += cs; cnt++; }
                                else { sum += tb.BoxScore; cnt++; }
                            }
                            conf = cnt > 0 ? (float)(sum / cnt) : 0f;
                        }
                        text = (r.StrRes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                    }
                }
            }
            catch { text = ""; }
            Console.Out.WriteLine("OK\t" + conf.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t" + text);
            Console.Out.Flush();
        }
        ocr?.Dispose();
        icons?.Dispose();
        det?.Dispose();
    }

    private static RapidOcrOptions ApplyCfg(RapidOcrOptions o, string kv)
    {
        float boxThresh = o.BoxThresh, boxScore = o.BoxScoreThresh, unclip = o.UnClipRatio, textScore = o.TextScore;
        int maxSide = o.MaxSideLen, limitSide = o.LimitSideLen, imgResize = o.ImgResize;
        bool doAngle = o.DoAngle;
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
                case "limitSide": int.TryParse(v, out limitSide); break;
                case "imgResize": int.TryParse(v, out imgResize); break;
                case "doAngle": doAngle = v == "1" || v.Equals("true", System.StringComparison.OrdinalIgnoreCase); break;
            }
        }
        return o with { BoxThresh = boxThresh, BoxScoreThresh = boxScore, UnClipRatio = unclip, TextScore = textScore, MaxSideLen = maxSide, LimitSideLen = limitSide, ImgResize = imgResize, DoAngle = doAngle };
    }
}
