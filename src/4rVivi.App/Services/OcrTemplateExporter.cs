using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FourRVivi.Core.Ocr;

namespace FourRVivi.App.Services;

/// <summary>Exports the OCR Reader marks (role -> normalized box) to tools/ocr-train/reference/template.json
/// plus a reference screenshot, so the Python trainer crops the right field from each training image.</summary>
public static class OcrTemplateExporter
{
    public static string ToolDir()
        => Path.Combine(System.AppContext.BaseDirectory, "tools", "ocr-train");

    public static string Export(IEnumerable<OcrMark> marks, byte[]? referencePng)
    {
        var refDir = Path.Combine(ToolDir(), "reference");
        Directory.CreateDirectory(refDir);
        var payload = new { marks };
        File.WriteAllText(Path.Combine(refDir, "template.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        if (referencePng is { Length: > 0 })
            File.WriteAllBytes(Path.Combine(refDir, "reference.png"), referencePng);
        return refDir;
    }
}
