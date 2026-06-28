using System;
using System.Collections.Generic;
using SkiaSharp;

namespace FourRVivi.OcrServer.Ocr;

/// <summary>
/// Normalized cross-correlation (NCC) template matching on SkiaSharp <see cref="SKBitmap"/> images.
/// Locates small UI templates (skill-bar slots, hotkey icons, buttons) inside a larger screenshot.
/// See guide §8 "Template Matching" / roadmap Stage 50.
///
/// Each registered template is stored as a grayscale float array. To find a template the service
/// slides it over the haystack and computes NCC = covariance / (stdWindow * stdTemplate). A
/// summed-area table (integral image) of the haystack and its square is precomputed so each
/// window's mean and standard deviation are O(1) regardless of template size.
/// </summary>
public sealed class TemplateMatchService
{
    /// <summary>A single template match result in haystack pixel coordinates.</summary>
    public sealed class TemplateMatch
    {
        public int X;
        public int Y;
        public int W;
        public int H;
        public double Score;
        public string Name = "";
    }

    private sealed class Template
    {
        public string Name = "";
        public int W;
        public int H;
        public float[] Gray = Array.Empty<float>(); // mean-subtracted grayscale, length W*H
        public double NormSq;                        // sum of (gray - mean)^2 over the template
    }

    private readonly Dictionary<string, Template> _templates = new(StringComparer.Ordinal);

    /// <summary>
    /// Stores a grayscale copy of <paramref name="template"/> under <paramref name="name"/>.
    /// A degenerate (empty or constant-color) template is still stored but will never match,
    /// because its standard deviation is zero.
    /// </summary>
    public void AddTemplate(string name, SKBitmap template)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name must be non-empty", nameof(name));
        if (template == null) throw new ArgumentNullException(nameof(template));

        int w = template.Width;
        int h = template.Height;
        if (w <= 0 || h <= 0)
        {
            _templates[name] = new Template { Name = name, W = 0, H = 0, Gray = Array.Empty<float>(), NormSq = 0.0 };
            return;
        }

        float[] gray = ToGray(template, out _);

        // Subtract the mean so NCC reduces to a plain dot product against the (also mean-subtracted)
        // haystack window, and precompute the template norm.
        double sum = 0.0;
        for (int i = 0; i < gray.Length; i++) sum += gray[i];
        double mean = sum / gray.Length;

        double normSq = 0.0;
        for (int i = 0; i < gray.Length; i++)
        {
            float v = (float)(gray[i] - mean);
            gray[i] = v;
            normSq += (double)v * v;
        }

        _templates[name] = new Template { Name = name, W = w, H = h, Gray = gray, NormSq = normSq };
    }

    /// <summary>Best single match for <paramref name="name"/> in <paramref name="haystack"/>, or null.</summary>
    public TemplateMatch? Find(SKBitmap haystack, string name, double minScore = 0.8)
    {
        var all = FindAllInternal(haystack, name, minScore, maxResults: 1, stride: 1, suppress: false);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// All non-max-suppressed matches for <paramref name="name"/>, best first.
    /// Overlapping candidates with IoU &gt; 0.3 are suppressed in favor of the higher score.
    /// </summary>
    public List<TemplateMatch> FindAll(SKBitmap haystack, string name, double minScore = 0.8, int maxResults = 50)
    {
        return FindAllInternal(haystack, name, minScore, maxResults, stride: 1, suppress: true);
    }

    /// <summary>
    /// Core search. <paramref name="stride"/> controls the sliding step (1 = exhaustive; larger = coarse/faster).
    /// When <paramref name="suppress"/> is true, IoU&gt;0.3 non-maximum suppression is applied.
    /// </summary>
    private List<TemplateMatch> FindAllInternal(SKBitmap haystack, string name, double minScore, int maxResults, int stride, bool suppress)
    {
        var results = new List<TemplateMatch>();

        if (haystack == null) return results;
        if (maxResults <= 0) return results;
        if (!_templates.TryGetValue(name, out var tpl)) return results;

        int hw = haystack.Width;
        int hh = haystack.Height;
        int tw = tpl.W;
        int th = tpl.H;

        // Guard empty / oversized templates and degenerate haystacks.
        if (tw <= 0 || th <= 0) return results;
        if (hw <= 0 || hh <= 0) return results;
        if (tw > hw || th > hh) return results;
        if (tpl.NormSq <= 1e-9) return results; // constant template can never correlate

        if (stride < 1) stride = 1;

        // Grayscale haystack as a flat float array (row-major).
        float[] hg = ToGray(haystack, out _);

        // Integral images for O(1) window sum and sum-of-squares.
        // Dimensions are (hh+1) x (hw+1) so the top/left border is all zeros.
        int iw = hw + 1;
        int ih = hh + 1;
        double[] sat = new double[iw * ih];
        double[] sat2 = new double[iw * ih];
        for (int y = 0; y < hh; y++)
        {
            int rowAbove = y * iw;
            int row = (y + 1) * iw;
            double rowSum = 0.0;
            double rowSum2 = 0.0;
            for (int x = 0; x < hw; x++)
            {
                float v = hg[y * hw + x];
                rowSum += v;
                rowSum2 += (double)v * v;
                sat[row + x + 1] = sat[rowAbove + x + 1] + rowSum;
                sat2[row + x + 1] = sat2[rowAbove + x + 1] + rowSum2;
            }
        }

        double area = (double)tw * th;
        double tplNorm = Math.Sqrt(tpl.NormSq);

        int maxX = hw - tw;
        int maxY = hh - th;

        for (int y = 0; y <= maxY; y += stride)
        {
            for (int x = 0; x <= maxX; x += stride)
            {
                // Window mean and variance via the summed-area tables.
                double s = RectSum(sat, iw, x, y, tw, th);
                double s2 = RectSum(sat2, iw, x, y, tw, th);
                double mean = s / area;
                double varSum = s2 - (s * s) / area; // sum of (window - mean)^2
                if (varSum <= 1e-9) continue;        // flat window cannot correlate

                // Covariance = sum( (window - meanWin) * (template - meanTpl) ).
                // The template is already mean-subtracted, so sum(template)=0 and the meanWin
                // term cancels; we just need the raw dot product.
                double dot = 0.0;
                float[] tg = tpl.Gray;
                for (int ty = 0; ty < th; ty++)
                {
                    int hRow = (y + ty) * hw + x;
                    int tRow = ty * tw;
                    for (int tx = 0; tx < tw; tx++)
                    {
                        dot += (double)hg[hRow + tx] * tg[tRow + tx];
                    }
                }

                double denom = Math.Sqrt(varSum) * tplNorm;
                if (denom <= 1e-9) continue;
                double score = dot / denom;

                if (score >= minScore)
                {
                    results.Add(new TemplateMatch
                    {
                        X = x,
                        Y = y,
                        W = tw,
                        H = th,
                        Score = score,
                        Name = name
                    });
                }
            }
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (suppress)
        {
            results = NonMaxSuppress(results, 0.3, maxResults);
        }
        else if (results.Count > maxResults)
        {
            results.RemoveRange(maxResults, results.Count - maxResults);
        }

        return results;
    }

    /// <summary>Sum of the integral image over the tw x th rectangle whose top-left is (x,y).</summary>
    private static double RectSum(double[] sat, int iw, int x, int y, int tw, int th)
    {
        int x0 = x;
        int y0 = y;
        int x1 = x + tw;
        int y1 = y + th;
        double a = sat[y0 * iw + x0];
        double b = sat[y0 * iw + x1];
        double c = sat[y1 * iw + x0];
        double d = sat[y1 * iw + x1];
        return d - b - c + a;
    }

    /// <summary>Greedy IoU-based non-maximum suppression. Input must be sorted best-first.</summary>
    private static List<TemplateMatch> NonMaxSuppress(List<TemplateMatch> sorted, double iouThreshold, int maxResults)
    {
        var kept = new List<TemplateMatch>();
        foreach (var cand in sorted)
        {
            bool overlaps = false;
            for (int i = 0; i < kept.Count; i++)
            {
                if (Iou(cand, kept[i]) > iouThreshold)
                {
                    overlaps = true;
                    break;
                }
            }
            if (!overlaps)
            {
                kept.Add(cand);
                if (kept.Count >= maxResults) break;
            }
        }
        return kept;
    }

    private static double Iou(TemplateMatch a, TemplateMatch b)
    {
        int ix0 = Math.Max(a.X, b.X);
        int iy0 = Math.Max(a.Y, b.Y);
        int ix1 = Math.Min(a.X + a.W, b.X + b.W);
        int iy1 = Math.Min(a.Y + a.H, b.Y + b.H);

        int iwid = ix1 - ix0;
        int ihei = iy1 - iy0;
        if (iwid <= 0 || ihei <= 0) return 0.0;

        double inter = (double)iwid * ihei;
        double union = (double)a.W * a.H + (double)b.W * b.H - inter;
        if (union <= 0.0) return 0.0;
        return inter / union;
    }

    /// <summary>
    /// Converts an <see cref="SKBitmap"/> to a row-major grayscale float array using the luma
    /// weights 0.299R + 0.587G + 0.114B. Values are in the 0..255 range.
    /// </summary>
    private static float[] ToGray(SKBitmap bmp, out int stride)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        stride = w;
        float[] gray = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                gray[row + x] = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
            }
        }
        return gray;
    }
}
