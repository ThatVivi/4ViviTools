using System.Drawing;

namespace FourRVivi.App.Services;

/// <summary>Small normalized cross-correlation matcher for refining UI-cell locations inside a marked crop.</summary>
public sealed class TemplateMatchService
{
    public readonly record struct Match(int X, int Y, int W, int H, double Score, string Name);

    private sealed class Template
    {
        public string Name = "";
        public int W;
        public int H;
        public double[] Data = Array.Empty<double>();
        public double Norm;
    }

    private readonly Dictionary<string, Template> _templates = new(StringComparer.Ordinal);

    public void Clear() => _templates.Clear();

    public void AddTemplate(string name, Bitmap template)
    {
        if (string.IsNullOrWhiteSpace(name) || template.Width < 4 || template.Height < 4) return;
        var data = Gray(template);
        double mean = data.Average();
        double normSq = 0;
        for (int i = 0; i < data.Length; i++)
        {
            data[i] -= mean;
            normSq += data[i] * data[i];
        }
        _templates[name] = new Template
        {
            Name = name,
            W = template.Width,
            H = template.Height,
            Data = data,
            Norm = Math.Sqrt(normSq)
        };
    }

    public Match? FindBest(Bitmap haystack, string name, Rectangle? search = null, double minScore = 0.72)
    {
        if (!_templates.TryGetValue(name, out var t) || t.Norm <= 1e-9) return null;
        if (haystack.Width < t.W || haystack.Height < t.H) return null;

        var area = search ?? new Rectangle(0, 0, haystack.Width, haystack.Height);
        area.Intersect(new Rectangle(0, 0, Math.Max(1, haystack.Width - t.W + 1), Math.Max(1, haystack.Height - t.H + 1)));
        if (area.Width <= 0 || area.Height <= 0) return null;

        var hg = Gray(haystack);
        double best = double.NegativeInfinity;
        int bestX = 0, bestY = 0;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            for (int x = area.X; x < area.Right; x++)
            {
                double sum = 0;
                for (int ty = 0; ty < t.H; ty++)
                {
                    int row = (y + ty) * haystack.Width + x;
                    for (int tx = 0; tx < t.W; tx++) sum += hg[row + tx];
                }
                double mean = sum / (t.W * t.H);
                double dot = 0, normSq = 0;
                for (int ty = 0; ty < t.H; ty++)
                {
                    int hRow = (y + ty) * haystack.Width + x;
                    int tRow = ty * t.W;
                    for (int tx = 0; tx < t.W; tx++)
                    {
                        double hv = hg[hRow + tx] - mean;
                        dot += hv * t.Data[tRow + tx];
                        normSq += hv * hv;
                    }
                }
                if (normSq <= 1e-9) continue;
                double score = dot / (Math.Sqrt(normSq) * t.Norm);
                if (score > best) { best = score; bestX = x; bestY = y; }
            }
        }

        return best >= minScore ? new Match(bestX, bestY, t.W, t.H, best, name) : null;
    }

    private static double[] Gray(Bitmap bmp)
    {
        var data = new double[bmp.Width * bmp.Height];
        int i = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                data[i++] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            }
        return data;
    }
}
