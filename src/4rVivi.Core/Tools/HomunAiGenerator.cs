using System.Text;

namespace FourRVivi.Core.Tools;

public sealed record HomunAiOptions(bool Aggressive, int FollowDistance, int SkillHpPercent, string SkillName);

/// <summary>Generates a simple USER_AI Lua config (drop into /AI/USER_AI/). Based on common ai-tricks templates.</summary>
public static class HomunAiGenerator
{
    public static string Generate(HomunAiOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- 4rVivi generated homunculus/mercenary AI");
        sb.AppendLine("-- place under <RO>/AI/USER_AI/ and enable @hoai");
        sb.AppendLine($"local MODE_AGGRESSIVE = {(o.Aggressive ? "true" : "false")}");
        sb.AppendLine($"local FOLLOW_DISTANCE = {Math.Clamp(o.FollowDistance, 1, 5)}");
        sb.AppendLine($"local SKILL_HP_PERCENT = {Math.Clamp(o.SkillHpPercent, 0, 100)}");
        sb.AppendLine($"local SKILL_NAME = \"{o.SkillName}\"");
        sb.AppendLine();
        sb.AppendLine("function AI(myid)");
        sb.AppendLine("    local owner = GetV(V_OWNER, myid)");
        sb.AppendLine("    -- stay near the owner");
        sb.AppendLine("    if (GetDistance2(myid, owner) > FOLLOW_DISTANCE) then");
        sb.AppendLine("        MoveToOwner(myid); return");
        sb.AppendLine("    end");
        sb.AppendLine("    if (MODE_AGGRESSIVE) then");
        sb.AppendLine("        local target = GetClosestEnemy(myid)");
        sb.AppendLine("        if (target ~= nil) then Attack(myid, target); return end");
        sb.AppendLine("    end");
        sb.AppendLine("end");
        return sb.ToString();
    }
}
