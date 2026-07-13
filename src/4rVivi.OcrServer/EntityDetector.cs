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

/// <summary>Detects WHERE RO entities are on a screenshot using the trained multi-class YOLO ONNX model
/// (entity.onnx). Returns boxes in the ORIGINAL image's pixel coords. The caller
/// crops each box and asks IconRecognizer WHICH entity it is. Disabled if the model is absent.</summary>
public sealed class EntityDetector : IDisposable
{
    public readonly record struct Box(int X, int Y, int W, int H, float Score, int ClassId);

    private readonly InferenceSession? _net;
    private readonly string _inName = "images";
    private readonly int _imgsz = 640;
    private float _conf, _iou;
    private string[] _classNames = { "entity" };
    public System.Collections.Generic.IReadOnlyList<string> ClassNames => _classNames;

    public bool Available => _net != null;

    public EntityDetector(int numThread = 0, float conf = 0.15f, float iou = 0.45f)
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
                    if (d.RootElement.TryGetProperty("recommended", out var rec) && rec.ValueKind == JsonValueKind.Object)
                    {
                        if (rec.TryGetProperty("monster_runtime_floor", out var floor) && floor.TryGetSingle(out var floorValue))
                            _conf = Math.Clamp(floorValue, 0.01f, 0.95f);
                        if (rec.TryGetProperty("nms_iou", out var iouProp) && iouProp.TryGetSingle(out var iouValue))
                            _iou = Math.Clamp(iouValue, 0.05f, 0.95f);
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
            var inputSpan = input.Buffer.Span;
            inputSpan.Fill(114f / 255f);
            using (var rs = img.Resize(new SKSizeI(nw, nh), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)))
            {
                if (rs == null) return outBoxes;
                int plane = S * S;
                int rowBytes = rs.RowBytes;
                int channels = rs.BytesPerPixel;
                ReadOnlySpan<byte> pixels = rs.GetPixelSpan();

                if (rs.Info.ColorType == SKColorType.Bgra8888 || rs.Info.ColorType == SKColorType.Rgba8888)
                {
                    bool bgra = rs.Info.ColorType == SKColorType.Bgra8888;
                    for (int y = 0; y < nh; y++)
                    {
                        int srcRow = y * rowBytes;
                        int dstBase = (oy + y) * S + ox;
                        for (int x = 0; x < nw; x++)
                        {
                            int p = srcRow + x * channels;
                            int dst = dstBase + x;
                            byte r = pixels[p + (bgra ? 2 : 0)];
                            byte g = pixels[p + 1];
                            byte b = pixels[p + (bgra ? 0 : 2)];
                            inputSpan[dst] = r / 255f;
                            inputSpan[plane + dst] = g / 255f;
                            inputSpan[plane * 2 + dst] = b / 255f;
                        }
                    }
                }
                else
                {
                    for (int y = 0; y < nh; y++)
                        for (int x = 0; x < nw; x++)
                        {
                            var c = rs.GetPixel(x, y);
                            int dst = (oy + y) * S + ox + x;
                            inputSpan[dst] = c.Red / 255f;
                            inputSpan[plane + dst] = c.Green / 255f;
                            inputSpan[plane * 2 + dst] = c.Blue / 255f;
                        }
                    }
            }

            using var results = _net!.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inName, input) });
            var t = results.First().AsTensor<float>();
            var dims = t.Dimensions.ToArray();              // [1, 4+nc, N] or [1, 5+nc, N]
            int ch = dims[1], N = dims[2];
            ReadOnlySpan<float> flat = t is DenseTensor<float> dense ? dense.Buffer.Span : t.ToArray();
            int metaClassCount = Math.Max(1, _classNames.Length);
            bool hasObjectness = ch >= metaClassCount + 5;
            int classStart = hasObjectness ? 5 : 4;

            var cand = new List<Box>();
            for (int i = 0; i < N; i++)
            {
                float obj = hasObjectness ? flat[4 * N + i] : 1f;
                float bestClass = 0f;
                int cls = 0;
                for (int c = classStart; c < ch; c++)
                {
                    float v = flat[c * N + i];
                    if (v > bestClass) { bestClass = v; cls = c - classStart; }
                }
                float conf = obj * bestClass;
                if (conf < _conf) continue;
                float cx = flat[i], cy = flat[N + i], w = flat[2 * N + i], h = flat[3 * N + i];   // letterboxed px
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
