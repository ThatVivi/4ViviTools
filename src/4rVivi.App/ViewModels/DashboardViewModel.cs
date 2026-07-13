using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FourRVivi.App.Services;
using FourRVivi.Core.Game;

namespace FourRVivi.App.ViewModels;

/// <summary>Home page: points new users at the OCR + Smart Bot flow while keeping shared toggles handy.</summary>
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
        Autopot = autopot;
        Buffs = buffs;
        SkillSpammer = skillSpammer;
        SmartBot = smartBot;
        BasicBot = basicBot;
        Overlay = overlay;
        Macros = macros;

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        t.Tick += (_, _) =>
        {
            OcrActive = LiveStats.Instance.IsFresh;
            OcrStatus = OcrActive
                ? "OCR is running and feeding values to Smart Bot, autopot, Discord, and trackers."
                : "Open OCR Reader, calibrate your markers, and press Start. That powers Smart Bot and the rest of the tool.";
        };
        t.Start();
    }

    [RelayCommand] private void Go(string key) => _nav.GoTo(key);
    [RelayCommand] private void OpenOcr() => _nav.GoTo("OCR Reader");
}
