using System.Security.Cryptography;
using System.Text.Json;
using FourRVivi.Core.Common;

namespace FourRVivi.App.Services;

public static class ModelManifestLogger
{
    private static int _logged;

    public static void LogOnce()
    {
        if (Interlocked.Exchange(ref _logged, 1) == 1)
            return;

        try
        {
            string baseDir = AppContext.BaseDirectory;
            DebugTrace.Write("ModelManifest", $"baseDir='{baseDir}'.");

            foreach (var root in CandidateModelRoots(baseDir))
            {
                LogFile(root, "models/yolo/entity.onnx", "yolo.entity");
                LogJson(root, "models/yolo/entity_meta.json", "yolo.meta", DescribeEntityMeta);
                LogFile(root, "models/icons/icon_embedder.onnx", "icons.embedder");
                LogFile(root, "models/icons/icon_refs.bin", "icons.refs");
                LogLabels(root, "models/icons/labels.txt", "icons.labels");
                LogJson(root, "models/icons/icon_meta.json", "icons.meta", DescribeIconMeta);
                LogFile(root, "models/v5/ch_PP-OCRv5_mobile_det.onnx", "ppocr.det");
                LogFile(root, "models/v5/ch_ppocr_mobile_v2.0_cls_infer.onnx", "ppocr.cls");
                LogFile(root, "models/v5/latin_PP-OCRv5_rec_mobile_infer.onnx", "ppocr.rec");
                LogFile(root, "models/v5/ppocrv5_latin_dict.txt", "ppocr.dict");
            }
        }
        catch (Exception ex)
        {
            DebugTrace.Write("ModelManifest", "manifest log failed.", ex);
        }
    }

    private static IEnumerable<string> CandidateModelRoots(string baseDir)
    {
        var roots = new[]
        {
            Path.Combine(baseDir, "OcrServer"),
            Path.Combine(baseDir, "OcrServerCuda"),
            baseDir,
        };

        return roots
            .Select(p => Path.GetFullPath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void LogFile(string root, string relative, string tag)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            DebugTrace.Write("ModelManifest", $"{tag} LOAD FAIL missing path='{path}'.");
            return;
        }

        var info = new FileInfo(path);
        DebugTrace.Write("ModelManifest",
            $"{tag} LOAD OK path='{path}' bytes={info.Length} modifiedUtc={info.LastWriteTimeUtc:O} sha256={Sha256(path)}.");
    }

    private static void LogJson(string root, string relative, string tag, Func<JsonElement, string> describe)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            DebugTrace.Write("ModelManifest", $"{tag} LOAD FAIL missing path='{path}'.");
            return;
        }

        var info = new FileInfo(path);
        string details = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            details = describe(doc.RootElement);
        }
        catch (Exception ex)
        {
            details = "jsonError=" + ex.GetType().Name;
        }

        DebugTrace.Write("ModelManifest",
            $"{tag} LOAD OK path='{path}' bytes={info.Length} modifiedUtc={info.LastWriteTimeUtc:O} sha256={Sha256(path)} {details}.");
    }

    private static void LogLabels(string root, string relative, string tag)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            DebugTrace.Write("ModelManifest", $"{tag} LOAD FAIL missing path='{path}'.");
            return;
        }

        var info = new FileInfo(path);
        int rows = 0;
        int maxIndex = -1;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int idx))
                {
                    rows++;
                    if (idx > maxIndex) maxIndex = idx;
                }
            }
        }
        catch { }

        DebugTrace.Write("ModelManifest",
            $"{tag} LOAD OK path='{path}' bytes={info.Length} modifiedUtc={info.LastWriteTimeUtc:O} sha256={Sha256(path)} rows={rows} maxIndex={maxIndex}.");
    }

    private static string DescribeEntityMeta(JsonElement root)
    {
        int imgsz = TryInt(root, "imgsz");
        int classes = 0;
        if (root.TryGetProperty("classes", out var cls) && cls.ValueKind == JsonValueKind.Array)
            classes = cls.GetArrayLength();
        return $"imgsz={imgsz} classes={classes}";
    }

    private static string DescribeIconMeta(JsonElement root)
    {
        int n = TryInt(root, "n");
        int emb = TryInt(root, "emb");
        int img = TryInt(root, "img");
        return $"n={n} emb={emb} img={img}";
    }

    private static int TryInt(JsonElement root, string name)
    {
        try
        {
            return root.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : 0;
        }
        catch { return 0; }
    }

    private static string Sha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return "unavailable";
        }
    }
}
