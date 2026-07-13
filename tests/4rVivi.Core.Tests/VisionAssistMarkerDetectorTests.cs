using System.Drawing;
using System.Text.Json;
using FourRVivi.App.Services;
using FourRVivi.Core.Game;
using Xunit;

namespace FourRVivi.Core.Tests;

public class VisionAssistMarkerDetectorTests
{
    [Fact]
    public void Detects_decoded_marker_and_reports_counters()
    {
        using var dir = new TempDir();
        int mobId = 1002;
        var manifest = WriteManifest(dir.Path, mobId, "Poring");
        using var frame = DrawMarker(mobId);
        var detector = new VisionAssistMarkerDetector();
        detector.LoadManifest(manifest);

        var result = detector.DetectWithDiagnostics(frame);

        Assert.Equal(1, result.RawBoxes);
        Assert.Equal(1, result.Decoded);
        Assert.Equal(0, result.NameUnknown);
        var marker = Assert.Single(result.Markers);
        Assert.Equal(mobId, marker.MobId);
        Assert.Equal("Poring", marker.Name);
    }

    [Fact]
    public void Detects_marker_from_built_in_game_data_without_manifest_file()
    {
        int mobId = 1002;
        using var frame = DrawMarker(mobId);
        var detector = new VisionAssistMarkerDetector();
        detector.LoadManifest("");

        var result = detector.DetectWithDiagnostics(frame);

        Assert.Equal(1, result.RawBoxes);
        Assert.Equal(1, result.Decoded);
        var marker = Assert.Single(result.Markers);
        Assert.Equal(mobId, marker.MobId);
        Assert.Equal("Poring", marker.Name);
    }

    [Fact]
    public void Emits_unknown_target_when_box_code_does_not_decode()
    {
        using var dir = new TempDir();
        var manifest = WriteManifest(dir.Path, 1002, "Poring");
        using var frame = DrawMarker(1002, garbageCode: true);
        var detector = new VisionAssistMarkerDetector();
        detector.LoadManifest(manifest);

        var result = detector.DetectWithDiagnostics(frame);

        Assert.Equal(1, result.RawBoxes);
        Assert.Equal(0, result.Decoded);
        Assert.Equal(1, result.NameUnknown);
        var marker = Assert.Single(result.Markers);
        Assert.Equal(-1, marker.MobId);
        Assert.Equal("Monster", marker.Name);
    }

    [Fact]
    public void Preconfirmed_marker_track_is_attackable_on_first_frame()
    {
        var tracker = new ByteTrackLite(
            trackThreshold: 0.35f,
            lowThreshold: 0.15f,
            matchThreshold: 0.25f,
            trackBuffer: 3,
            minHits: LiveScene.TrackMinHits);

        var tracks = tracker.Update(new[]
        {
            new SceneItem(40, 50, 36, 32, "Poring", 0.90f, Hits: LiveScene.TrackMinHits, Confirmed: true)
        });

        var track = Assert.Single(tracks);
        Assert.True(track.Confirmed);
        Assert.True(track.IsAttackable);
    }

    [Fact]
    public void LiveScene_returns_preconfirmed_grf_marker_as_attackable_target()
    {
        LiveScene.Instance.Clear();
        try
        {
            LiveScene.Instance.Active = true;
            LiveScene.Instance.SetEntities(new[]
            {
                new SceneItem(40, 50, 36, 32, "Poring", 0.90f, Hits: LiveScene.TrackMinHits, Confirmed: true)
            }, clientCoords: true);

            var target = LiveScene.Instance.Nearest(0, 0, label => !string.IsNullOrWhiteSpace(label));

            Assert.NotNull(target);
            Assert.True(target.Value.IsAttackable);
            Assert.Equal("Poring", target.Value.Label);
        }
        finally
        {
            LiveScene.Instance.Clear();
        }
    }

    [Fact]
    public void Marker_identity_survives_simulated_cave_lighting()
    {
        int[] mobIds = { 1002, 1005, 1049, 1113, 1613 };
        double[] factors = { 0.60, 0.75, 0.90 };

        foreach (int mobId in mobIds)
        {
            using var dir = new TempDir();
            var manifest = WriteManifest(dir.Path, mobId, $"mob_{mobId}");
            var detector = new VisionAssistMarkerDetector();
            detector.LoadManifest(manifest);

            foreach (double factor in factors)
            {
                using var frame = DrawMarker(mobId, brightness: factor);
                var result = detector.DetectWithDiagnostics(frame);

                Assert.Equal(1, result.RawBoxes);
                Assert.Equal(1, result.Decoded);
                Assert.Equal(mobId, Assert.Single(result.Markers).MobId);
            }
        }
    }

    [Fact]
    public void Marker_identity_uses_cell_neighborhood_not_single_noisy_pixel()
    {
        using var dir = new TempDir();
        int mobId = 1002;
        var manifest = WriteManifest(dir.Path, mobId, "Poring");
        using var frame = DrawMarker(mobId, corruptCodeCenters: true);
        var detector = new VisionAssistMarkerDetector();
        detector.LoadManifest(manifest);

        var result = detector.DetectWithDiagnostics(frame);

        Assert.Equal(1, result.RawBoxes);
        Assert.Equal(1, result.Decoded);
        Assert.Equal(mobId, Assert.Single(result.Markers).MobId);
    }

    private static string WriteManifest(string dir, int mobId, string name)
    {
        var path = Path.Combine(dir, "VisionAssist.manifest.json");
        var manifest = new
        {
            version = 1,
            codeCellPx = 5,
            boxPx = 2,
            mobs = new Dictionary<string, object>
            {
                [mobId.ToString()] = new
                {
                    name,
                    code = ColorCode(mobId).Select(c => new[] { c.R, c.G, c.B }).ToArray()
                }
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    private static Bitmap DrawMarker(int mobId, bool garbageCode = false, double brightness = 1.0, bool corruptCodeCenters = false)
    {
        var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(20, 20, 20));
        using var red = new SolidBrush(Scale(Color.Red, brightness));
        g.FillRectangle(red, 6, 6, 30, 2);
        g.FillRectangle(red, 6, 36, 30, 2);
        g.FillRectangle(red, 6, 6, 2, 32);
        g.FillRectangle(red, 34, 6, 2, 32);
        var cells = garbageCode
            ? new[] { (0, 0, 0), (0, 0, 0), (0, 0, 0) }
            : ColorCode(mobId);
        for (int i = 0; i < 3; i++)
        {
            var (r, gr, b) = cells[i];
            using var brush = new SolidBrush(Scale(Color.FromArgb(r, gr, b), brightness));
            g.FillRectangle(brush, 6 + 2 + i * 5, 6 + 2, 5, 5);
            if (corruptCodeCenters)
                bmp.SetPixel(6 + 2 + i * 5 + 2, 6 + 2 + 2, Color.Black);
        }
        return bmp;
    }

    private static Color Scale(Color color, double factor)
        => Color.FromArgb(
            Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.B * factor), 0, 255));

    private static (int R, int G, int B)[] ColorCode(int mobId)
    {
        int[] levels = { 48, 96, 144, 192, 240 };
        int[] digits = new int[6];
        int n = mobId;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            digits[i] = n % levels.Length;
            n /= levels.Length;
        }
        return new[]
        {
            (255, levels[digits[0]], levels[digits[1]]),
            (levels[digits[2]], 255, levels[digits[3]]),
            (levels[digits[4]], levels[digits[5]], 255)
        };
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "4vivi-vision-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
