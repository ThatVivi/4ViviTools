using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RapidOcrNet;
using SkiaSharp;

namespace FourRVivi.OcrServer;

/// <summary>Detects WHERE entities (sprites) are on a screenshot using a YOLOv8 ONNX model
/// (entity.onnx, single class). Returns boxes in the ORIGINAL image's pixel coords. The caller
/// crops each box and asks IconRecognizer WHICH entity it is. Disabled if the model is absent.</summary>
public sealed class EntityDetector : IDisposable
{
    public readonly record struct Box(int X, int Y, int W, int H, float Score, int ClassId);

    private readonly InferenceSession? _net;
    private readonly string _inName = "images";
    private readonly int _imgsz = 640;
    private readonly float _conf, _iou;
    private string[] _classNames = { "entity" };
    public System.Collections.Generic.IReadOnlyList<string> ClassNames => _classNames;

    public bool Available => _net != null;

    public EntityDetector(int numThread = 0, float conf = 0.25f, float iou = 0.45f)
    {
        _conf = conf; _iou = iou;
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, RapidOcr.ModelsFolderName, "yolo");
            string onnx = Path.Combine(dir, "entity.onnx");
            string metaP = Path.Combine(dir, "entity_meta.json");
            if (!File.Exists(onnx)) return;
            if (File.Exists(metaP))
                try
                {
                    using var d = JsonDocument.Parse(File.ReadAllText(metaP));
                    _imgsz = d.RootElement.GetProperty("imgsz").GetInt32();
                    if (d.RootElement.TryGetProperty("classes", out var cls) && cls.ValueKind == JsonValueKind.Array)
                    {
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var e in cls.EnumerateArray()) list.Add(e.GetString() ?? "");
                        if (list.Count > 0) _classNames = list.ToArray();
                    }
                }
                catch { }
            _net = new InferenceSession(onnx, RapidOcr.GetDefaultSessionOptions(numThread));
            _inName = _net.InputMetadata.Keys.First();
        }
        catch { _net = null; }
    }

    public List<Box> Detect(SKBitmap img)
    {
        var outBoxes = new List<Box>();
        if (!Available || img == null) return outBoxes;
        try
        {
            int W = img.Width, H = img.Height, S = _imgsz;
            float scale = Math.Min(S / (float)W, S / (float)H);
            int nw = (int)Math.Round(W * scale), nh = (int)Math.Round(H * scale);
            int ox = (S - nw) / 2, oy = (S - nh) / 2;

            var input = new DenseTensor<float>(new[] { 1, 3, S, S });
            for (int i = 0; i < S * S; i++) { int y = i / S, x = i % S; input[0, 0, y, x] = input[0, 1, y, x] = input[0, 2, y, x] = 114f / 255f; }
            using (var rs = img.Resize(new SKSizeI(nw, nh), new SKSamplingOptions(SKCubicResampler.Mitchell)))
            {
                if (rs == null) return outBoxes;
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        var c = rs.GetPixel(x, y);
                        input[0, 0, oy + y, ox + x] = c.Red / 255f;
                        input[0, 1, oy + y, ox + x] = c.Green / 255f;
                        input[0, 2, oy + y, ox + x] = c.Blue / 255f;
                    }
            }

            using var results = _net!.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inName, input) });
            var t = results.First().AsTensor<float>();
            var dims = t.Dimensions.ToArray();              // [1, 4+nc, N]
            int ch = dims[1], N = dims[2];
            var flat = t.ToArray();                         // channel-major: flat[c*N + i]
            float At(int c, int i) => flat[c * N + i];

            var cand = new List<Box>();
            for (int i = 0; i < N; i++)
            {
                float conf = At(4, i); int cls = 0;         // argmax over class channels (already activated)
                for (int c = 5; c < ch; c++) { float v = At(c, i); if (v > conf) { conf = v; cls = c - 4; } }
                if (conf < _conf) continue;
                float cx = At(0, i), cy = At(1, i), w = At(2, i), h = At(3, i);   // letterboxed px
                float left = (cx - w / 2 - ox) / scale, top = (cy - h / 2 - oy) / scale;
                float bw = w / scale, bh = h / scale;
                int X = (int)Math.Round(Math.Max(0, left)), Y = (int)Math.Round(Math.Max(0, top));
                int BW = (int)Math.Round(Math.Min(W - X, bw)), BH = (int)Math.Round(Math.Min(H - Y, bh));
                if (BW > 2 && BH > 2) cand.Add(new Box(X, Y, BW, BH, conf, cls));
            }
            return Nms(cand, _iou);
        }
        catch { return outBoxes; }
    }

    private static List<Box> Nms(List<Box> b, float iouThr)
    {
        var keep = new List<Box>();
        foreach (var x in b.OrderByDescending(z => z.Score))
        {
            bool drop = false;
            foreach (var k in keep) if (Iou(x, k) > iouThr) { drop = true; break; }
            if (!drop) keep.Add(x);
            if (keep.Count >= 300) break;
        }
        return keep;
    }

    private static float Iou(Box a, Box b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.W, b.X + b.W), y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        int iw = Math.Max(0, x2 - x1), ih = Math.Max(0, y2 - y1);
        float inter = iw * ih, uni = a.W * a.H + b.W * b.H - inter;
        return uni <= 0 ? 0 : inter / uni;
    }

    public void Dispose() => _net?.Dispose();
}
