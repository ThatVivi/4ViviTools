using FourRVivi.Core.Game;
using FourRVivi.Core.Input;
using Xunit;

namespace FourRVivi.Core.Tests;

public sealed class InputRouterTests
{
    [Fact]
    public void Tap_returns_blocked_and_does_not_deliver_when_gate_denies()
    {
        int deliveries = 0;
        var router = new InputRouter(
            () => new IntPtr(0x1234),
            Deny("not-foreground"),
            tapDelivery: (_, _, _) => { deliveries++; return true; });

        var result = router.Tap(new IntPtr(0x1234), KeyName.ToVk("F2"), 20, "test");

        Assert.False(result.Sent);
        Assert.Equal(InputRouteStatus.Blocked, result.Status);
        Assert.Equal("not-foreground", result.Reason);
        Assert.Equal(0, deliveries);
    }

    [Fact]
    public void Tap_returns_invalid_input_for_unknown_key_before_delivery()
    {
        int deliveries = 0;
        var router = new InputRouter(
            () => new IntPtr(0x1234),
            Allow(),
            tapDelivery: (_, _, _) => { deliveries++; return true; });

        var result = router.Tap(new IntPtr(0x1234), 0, 20, "test");

        Assert.False(result.Sent);
        Assert.Equal(InputRouteStatus.InvalidInput, result.Status);
        Assert.Equal("invalid-key", result.Reason);
        Assert.Equal(0, deliveries);
    }

    [Fact]
    public void Click_returns_invalid_target_when_hwnd_mismatches_selected_window()
    {
        int deliveries = 0;
        var router = new InputRouter(
            () => new IntPtr(0x1234),
            Allow(),
            clickDelivery: (_, _, _) => { deliveries++; return true; });

        var result = router.ClickAt(new IntPtr(0x5678), 10, 20, "test");

        Assert.False(result.Sent);
        Assert.Equal(InputRouteStatus.InvalidTarget, result.Status);
        Assert.Equal("window-mismatch", result.Reason);
        Assert.Equal(0, deliveries);
    }

    private static InputCanAct Allow() => Snapshot("ok", canAct: true);

    private static InputCanAct Deny(string reason) => Snapshot(reason, canAct: false);

    private static InputCanAct Snapshot(string reason, bool canAct)
        => (out FocusGateSnapshot snapshot) =>
        {
            snapshot = new FocusGateSnapshot(
                CanRead: true,
                CanAct: canAct,
                Reason: reason,
                SelectedPid: 100,
                ForegroundPid: canAct ? 100 : 200,
                WindowHandle: new IntPtr(0x1234),
                RectValid: true);
            return canAct;
        };
}
