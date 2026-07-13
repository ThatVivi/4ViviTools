using FourRVivi.Core.Automation;
using FourRVivi.Core.Game;
using Xunit;

namespace FourRVivi.Core.Tests;

public sealed class SmartBotCombatDecisionTests
{
    [Fact]
    public void Skill_without_sp_requirement_can_be_attempted_when_sp_unknown()
    {
        var gate = SmartBotCombatDecisions.CheckSpForSkill(
            "F2",
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            _ => (false, 0));

        Assert.Equal(SkillSpGateKind.NotRequired, gate.Kind);
        Assert.True(gate.CanAttempt);
    }

    [Fact]
    public void Skill_with_sp_requirement_is_unknown_without_trusted_sp()
    {
        var gate = SmartBotCombatDecisions.CheckSpForSkill(
            "F2",
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["F2"] = 24 },
            _ => (false, 0));

        Assert.Equal(SkillSpGateKind.Unknown, gate.Kind);
        Assert.False(gate.CanAttempt);
    }

    [Fact]
    public void Skill_with_sp_requirement_uses_trusted_sp_percent_and_max_sp()
    {
        var gate = SmartBotCombatDecisions.CheckSpForSkill(
            "F2",
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["F2"] = 24 },
            role => role switch
            {
                Roles.SpPercent => (true, 50),
                Roles.MaxSp => (true, 100),
                _ => (false, 0)
            });

        Assert.Equal(SkillSpGateKind.Enough, gate.Kind);
        Assert.Equal(50, gate.Current);
        Assert.True(gate.CanAttempt);
    }

    [Fact]
    public void Lost_grace_target_is_not_attackable_even_if_confirmed()
    {
        var item = new SceneItem(
            10, 20, 30, 40,
            "Poring",
            0.99f,
            TrackId: 7,
            Hits: 3,
            Misses: 1,
            State: SceneTrackState.LostGrace,
            Confirmed: true);

        Assert.False(SmartBotCombatDecisions.CanAttackTarget(item));
    }
}
