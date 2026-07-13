namespace FourRVivi.Core.Ocr;

/// <summary>Stable vision gates shared by OCR, tracking, overlay, and the bot.
/// The detector feed must not change threshold every frame; per-frame confidence
/// formulas were causing boxes to appear/disappear as the scene moved.</summary>
public static class VisionConfig
{
    public const float DefaultTrackConfidence = 0.50f;
    public const float DefaultAttackConfidence = 0.55f;
    public const float TrackerLowConfidence = 0.25f;
    public const float TrackerMatchThreshold = 0.24f;
    public const int TrackerMinHits = 2;
    public const int TrackerMaxMisses = 2;
}
