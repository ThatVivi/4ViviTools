using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.Core.Game;
using FourRVivi.App.Services;

namespace FourRVivi.App.ViewModels;

/// <summary>Home page: tells the user to start in OCR Reader, exposes every feature toggle, and gives
/// quick navigation. Toggles bind to the same automation VMs used by the sub-tabs (single source).</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly NavigationService _nav;

    public AutopotViewModel Autopot { get; }
    public BuffsViewModel Buffs { get; }
    public ClassSkillsViewModel SkillSpammer { get; }
    public SmartBotViewModel SmartBot { get; }
    public BotFarmViewModel BasicBot { get; }
    public OverlayViewModel Overlay { get; }
    public MacrosViewModel Macros { get; }

    [ObservableProperty] private bool _ocrActive;
    [ObservableProperty] private string _ocrStatus = "OCR is not running yet.";

    public DashboardViewModel(NavigationService nav,
        AutopotViewModel autopot, BuffsViewModel buffs, ClassSkillsViewModel skillSpammer,
        SmartBotViewModel smartBot, BotFarmViewModel basicBot, OverlayViewModel overlay, MacrosViewModel macros)
    {
        _nav = nav;
        Autopot = autopot; Buffs = buffs; SkillSpammer = skillSpammer;
        SmartBot = smartBot; BasicBot = basicBot; Overlay = overlay; Macros = macros;

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        t.Tick += (_, _) =>
        {
            OcrActive = LiveStats.Instance.IsFresh;
            OcrStatus = OcrActive
                ? "OCR is running and feeding values to every feature. ✓"
                : "① Open OCR Reader, calibrate your stats, and press Start — that powers autopot, Discord, trackers and everything else.";
        };
        t.Start();
    }

    [RelayCommand] private void Go(string key) => _nav.GoTo(key);
    [RelayCommand] private void OpenOcr() => _nav.GoTo("OCR Reader");
}
