namespace FourRVivi.Core.Game;

/// <summary>Well-known address roles the user discovers in the Scanner and binds per profile.</summary>
public static class Roles
{
    public const string Hp = "HP", MaxHp = "MaxHP", Sp = "SP", MaxSp = "MaxSP";
    public const string Exp = "BaseEXP", JobExp = "JobEXP", Zeny = "Zeny";
    public const string Weight = "Weight", MaxWeight = "MaxWeight";
    public const string BaseLevel = "BaseLevel", JobLevel = "JobLevel";
    public const string PosX = "PosX", PosY = "PosY";

    public static readonly string[] All =
    {
        Hp, MaxHp, Sp, MaxSp, Exp, JobExp, Zeny, Weight, MaxWeight, BaseLevel, JobLevel, PosX, PosY
    };
}
