using CommunityToolkit.Mvvm.ComponentModel;

namespace FourRVivi.App.ViewModels;

/// <summary>The single merged Bot tab: OCR reader + smart combat + basic farm + RCX overlay on one
/// surface. The child view-models are DI singletons (shared LiveStats/LiveScene), so everything is
/// already wired together; this just hosts them in one place.</summary>
public sealed partial class BotStudioViewModel : ViewModelBase
{
    public OcrReaderViewModel Reader { get; }
    public SmartBotViewModel Smart { get; }
    public BotFarmViewModel Basic { get; }
    public OverlayViewModel Overlay { get; }

    public BotStudioViewModel(OcrReaderViewModel reader, SmartBotViewModel smart,
                              BotFarmViewModel basic, OverlayViewModel overlay)
    {
        Reader = reader; Smart = smart; Basic = basic; Overlay = overlay;
    }
}
