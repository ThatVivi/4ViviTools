namespace FourRVivi.Core.Automation;

/// <summary>Per-monster bot behaviour: whether to attack it, and an optional skill (with cooldown)
/// to use on it. Matched against the vision embedder label by case-insensitive substring.</summary>
public sealed class MonsterRule
{
    public string Name { get; set; } = "";
    public bool Attack { get; set; } = true;
    public string SkillKey { get; set; } = "";
    public int SkillCooldownMs { get; set; } = 2500;
}
