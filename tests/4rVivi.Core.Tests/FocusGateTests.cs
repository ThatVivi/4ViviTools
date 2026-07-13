using FourRVivi.Core.Game;
using Xunit;

namespace FourRVivi.Core.Tests;

public class FocusGateTests
{
    [Fact]
    public void Act_is_false_when_foreground_pid_differs_from_selected_pid()
    {
        Assert.False(FocusGate.EvaluateCanAct(selectedPid: 100, foregroundPid: 200, canRead: true));
    }

    [Fact]
    public void Act_is_true_only_for_selected_foreground_pid_and_readable_client()
    {
        Assert.True(FocusGate.EvaluateCanAct(selectedPid: 100, foregroundPid: 100, canRead: true));
        Assert.False(FocusGate.EvaluateCanAct(selectedPid: 100, foregroundPid: 100, canRead: false));
    }
}
