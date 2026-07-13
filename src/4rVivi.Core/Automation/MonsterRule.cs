namespace FourRVivi.Core.Automation;

/// <summary>Legacy profile DTO kept so old Smart Bot profiles still deserialize.</summary>
public sealed class MonsterRule
{
    public string Name { get; set; } = "";
    public bool Attack { get; set; } = true;
    public string Estimate { get; set; } = "";
    public string SkillKey { get; set; } = "";
    public int SkillCooldownMs { get; set; } = 2500;
}
