namespace FourRVivi.Core.Ocr;

/// <summary>A labeled OCR region, stored as fractions of the game window (0..1) so it survives resizes.</summary>
public sealed class OcrMark
{
    public string Role { get; set; } = "HP";   // matches Roles.* (HP, MaxHP, SP, ... or "Name")
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public bool IsText { get; set; }            // Name/Class -> text
    public bool IsBar { get; set; }             // EXP/cast bars -> read fill %; HP/SP use percent text markers.
    public bool IsChar { get; set; }            // character sprite box -> motion/activity detection
    public bool IsIcons { get; set; }           // skill/buff bar -> icon-recognise each cell (+ buff timer)
    public string Expected { get; set; } = "";  // ground-truth value the user typed for verification calibration
    public bool Calibrated { get; set; }         // settings were locked to reproduce Expected
    public string Preprocess { get; set; } = "Auto";  // per-mark colour/filter layer chosen by auto-tune
    public double Sharpen { get; set; } = 1.0;          // per-mark unsharp strength chosen by auto-tune
    public double MinScore { get; set; }                // per-field confidence floor (0 = use global)
    public string Engine { get; set; } = "";            // per-field OCR engine override: ""=auto, "Paddle", "Windows"
}
