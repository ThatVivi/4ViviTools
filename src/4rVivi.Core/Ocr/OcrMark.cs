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
    public bool IsBar { get; set; }             // HP/SP/EXP bars -> read fill % (no numbers)
    public bool IsChar { get; set; }            // character sprite box -> motion/activity detection
}
