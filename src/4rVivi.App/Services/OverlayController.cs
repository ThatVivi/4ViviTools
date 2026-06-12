using FourRVivi.Core.Game;
using FourRVivi.App.Overlay;

namespace FourRVivi.App.Services;

/// <summary>Owns the single overlay window instance and forwards configuration.</summary>
public sealed class OverlayController
{
    private readonly GameSession _session;
    private OverlayWindow? _win;
    public int CastRange { get; set; }
    public int Aoe { get; set; }
    public bool Gutter { get; set; }

    public OverlayController(GameSession session) => _session = session;

    public void Show()
    {
        _win ??= new OverlayWindow(_session);
        _win.Configure(CastRange, Aoe, Gutter);
        _win.Show();
    }
    public void Apply() => _win?.Configure(CastRange, Aoe, Gutter);
    public void Hide() => _win?.Hide();
}
