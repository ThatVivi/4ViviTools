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

/// <summary>Recognizes a game icon/sprite/minimap by IMAGE: embeds the crop with the trained
/// MobileNetV3 embedder (icon_embedder.onnx) and cosine-matches it to a bank of clean reference
/// embeddings (icon_refs.bin). Returns the class label (e.g. "items__501", "skills__SM_BASH",
/// "spr_monsters__poring", "map__prontera"). Disabled (Available=false) if any model file is missing.</summary>
public sealed class IconRecognizer : IDisposable
{
    private readonly InferenceSession? _net;
    private readonly string _inName = "x";
    private readonly string _outName = "";
    private readonly float[] _bank = Array.Empty<float>();   // [n * emb], L2-normalized
    private readonly string[] _labels = Array.Empty<string>();
    private readonly int _n, _emb, _img;

    public bool Available => _net != null && _n > 0;

    public IconRecognizer(int numThread = 0)
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, RapidOcr.ModelsFolderName, "icons");
            string onnx = Path.Combine(dir, "icon_embedder.onnx");
            string binp = Path.Combine(dir, "icon_refs.bin");
            string lab = Path.Combine(dir, "labels.txt");
            string metaP = Path.Combine(dir, "icon_meta.json");
            if (!File.Exists(onnx) || !File.Exists(binp) || !File.Exists(lab) || !File.Exists(metaP))
                return;

            using (var doc = JsonDocument.Parse(File.ReadAllText(metaP)))
            {
                var r = doc.RootElement;
                _emb = r.GetProperty("emb").GetInt32();
                _img = r.GetProperty("img").GetInt32();
                _n = r.TryGetProperty("n", out var nv) ? nv.GetInt32() : 0;
            }

            // labels: lines "idx\tname" -> array indexed by idx
            var pairs = File.ReadAllLines(lab)
                .Select(l => l.Split('\t'))
                .Where(p => p.Length >= 2 && int.TryParse(p[0], out _))
                .Select(p => (idx: int.Parse(p[0]), name: p[1]))
                .ToList();
            int maxIdx = pairs.Count > 0 ? pairs.Max(p => p.idx) : -1;
            _labels = new string[maxIdx + 1];
            foreach (var p in pairs) _labels[p.idx] = p.name;

            // bank: raw little-endian float32, n*emb
            byte[] raw = File.ReadAllBytes(binp);
            int floats = raw.Length / 4;
            if (_n <= 0) _n = (_emb > 0) ? floats / _emb : 0;
            _bank = new float[floats];
            Buffer.BlockCopy(raw, 0, _bank, 0, floats * 4);
            if (_emb <= 0 || _n <= 0 || _bank.Length < (long)_n * _emb) return;

            var so = RapidOcr.GetDefaultSessionOptions(numThread);
            _net = new InferenceSession(onnx, so);
            _inName = _net.InputMetadata.Keys.First();
            _outName = _net.OutputMetadata.Keys.First();
        }
        catch { _net = null; }
    }

    /// <summary>Embed + match. Returns (label, score in [-1,1]) or null if unavailable.</summary>
    public (string label, float score)? Recognize(SKBitmap crop)
    {
        if (!Available || crop == null) return null;
        try
        {
            using var rs = crop.Resize(new SKSizeI(_img, _img), new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (rs == null) return null;
            var input = new DenseTensor<float>(new[] { 1, 3, _img, _img });
            for (int y = 0; y < _img; y++)
                for (int x = 0; x < _img; x++)
                {
                    var c = rs.GetPixel(x, y);
                    // training preprocessing: (channel/255 - 0.5) / 0.5  == channel/127.5 - 1, RGB order
                    input[0, 0, y, x] = c.Red   / 127.5f - 1f;
                    input[0, 1, y, x] = c.Green / 127.5f - 1f;
                    input[0, 2, y, x] = c.Blue  / 127.5f - 1f;
                }

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inName, input) };
            using var results = _net!.Run(inputs);
            var outT = results.First(v => v.Name == _outName).AsTensor<float>();
            var emb = outT.ToArray();
            if (emb.Length < _emb) return null;

            // L2-normalize the query (the bank is already normalized)
            double norm = 0; for (int k = 0; k < _emb; k++) norm += (double)emb[k] * emb[k];
            float inv = (float)(1.0 / Math.Sqrt(Math.Max(norm, 1e-12)));
            for (int k = 0; k < _emb; k++) emb[k] *= inv;

            // cosine = dot vs each reference; take argmax
            int best = -1; float bestScore = -2f;
            for (int r = 0; r < _n; r++)
            {
                int off = r * _emb; float dot = 0f;
                for (int k = 0; k < _emb; k++) dot += emb[k] * _bank[off + k];
                if (dot > bestScore) { bestScore = dot; best = r; }
            }
            if (best < 0 || best >= _labels.Length || _labels[best] == null) return null;
            return (_labels[best], bestScore);
        }
        catch { return null; }
    }

    public (string label, float score)? Recognize(byte[] png)
    {
        if (png == null || png.Length == 0) return null;
        using var bmp = SKBitmap.Decode(png);
        return bmp == null ? null : Recognize(bmp);
    }

    public void Dispose() => _net?.Dispose();
}
