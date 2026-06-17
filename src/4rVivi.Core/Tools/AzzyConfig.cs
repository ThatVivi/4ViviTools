namespace FourRVivi.Core.Tools;

public enum AzzyType { Bool, Int, Enum }

public sealed class AzzyOption
{
    public string Key { get; init; } = "";
    public string Category { get; init; } = "Basic";
    public AzzyType Type { get; init; } = AzzyType.Bool;
    public string Description { get; init; } = "";
    public bool BoolValue { get; set; }
    public int IntValue { get; set; }
    public string StringValue { get; set; } = "";
    public string[] EnumValues { get; init; } = Array.Empty<string>();
    public bool IsBool => Type == AzzyType.Bool;
    public bool IsInt => Type == AzzyType.Int;
    public bool IsEnum => Type == AzzyType.Enum;
}

/// <summary>Curated AzzyAI homunculus settings, editable in-app and written to the AI config file.</summary>
public static class AzzyConfig
{
    public static List<AzzyOption> Defaults() => new()
    {
        new() { Key="AggroHP", Type=AzzyType.Int, IntValue=60, Description="Seek/attack while HP% is above this (100 = never auto-aggro)." },
        new() { Key="AggroSP", Type=AzzyType.Int, IntValue=0, Description="Only auto-aggro while SP% is above this." },
        new() { Key="AssumeHomun", Type=AzzyType.Bool, BoolValue=true, Description="Assume control of the homunculus on load." },
        new() { Key="AttackTimeLimit", Type=AzzyType.Int, IntValue=10000, Description="Give up on a target after this many ms." },
        new() { Key="DoNotChase", Type=AzzyType.Bool, BoolValue=false, Description="Never chase fleeing monsters." },
        new() { Key="KiteMonsters", Type=AzzyType.Bool, BoolValue=false, Description="Keep distance from melee monsters." },
        new() { Key="MobileAggroDist", Type=AzzyType.Int, IntValue=7, Description="Aggro range while moving with the owner." },
        new() { Key="StationaryAggroDist", Type=AzzyType.Int, IntValue=12, Description="Aggro range while the owner is still." },
        new() { Key="RescueOwnerLowHP", Type=AzzyType.Int, IntValue=0, Description="Return to owner when its HP% drops below this (0 = off)." },
        new() { Key="TankMonsterLimit", Type=AzzyType.Int, IntValue=4, Description="Max monsters to tank at once." },
        new() { Key="UseAttackSkill", Type=AzzyType.Bool, BoolValue=true, Description="Use offensive skills." },
        new() { Key="OpportunisticTargeting", Type=AzzyType.Bool, BoolValue=false, Description="Switch to nearer/easier targets opportunistically." },
        new() { Key="UseSkillOnly", Type=AzzyType.Enum, StringValue="Chasing", EnumValues=new[]{"Always","Chasing","Never"}, Description="When the homunculus may cast skills." },
        new() { Key="UseBerserkAttack", Category="Berserk", Type=AzzyType.Bool, BoolValue=false, Description="Use the berserk melee skill." },
        new() { Key="UseBerserkSkill", Category="Berserk", Type=AzzyType.Bool, BoolValue=false, Description="Use the berserk active skill." },
        new() { Key="UseBerserkMobbed", Category="Berserk", Type=AzzyType.Int, IntValue=0, Description="Trigger berserk when mobbed by N+ monsters (0 = off)." },
        new() { Key="MirAIFriending", Category="Friending", Type=AzzyType.Bool, BoolValue=true, Description="Cooperate with other AzzyAI homunculi." },
        new() { Key="PainkillerFriends", Category="Friending", Type=AzzyType.Bool, BoolValue=true, Description="Cast Painkiller on friends (Eira)." },
        new() { Key="FleeHP", Category="Kiting", Type=AzzyType.Int, IntValue=0, Description="Flee when HP% drops below this (0 = off)." },
        new() { Key="DanceMinSP", Category="Kiting", Type=AzzyType.Int, IntValue=0, Description="Minimum SP% before dancing/kiting skills." },
        new() { Key="SuperPassive", Category="Kiting", Type=AzzyType.Bool, BoolValue=false, Description="Only act when explicitly told (fully passive)." },
        new() { Key="LagReduction", Category="Kiting", Type=AzzyType.Int, IntValue=0, Description="Throttle AI ticks to reduce lag." },
    };

    public static string ToLua(IEnumerable<AzzyOption> opts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- 4rVivi-generated AzzyAI config");
        sb.AppendLine("AzzyConfig = {");
        foreach (var o in opts)
        {
            string v = o.Type switch { AzzyType.Bool => o.BoolValue ? "true" : "false", AzzyType.Int => o.IntValue.ToString(), _ => "\"" + o.StringValue + "\"" };
            sb.AppendLine($"    {o.Key} = {v},");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
