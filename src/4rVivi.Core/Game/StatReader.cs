namespace FourRVivi.Core.Game;

/// <summary>Convenience reads over the session's bound role addresses. -1 when unknown.</summary>
public sealed class StatReader
{
    private readonly GameSession _s;
    public StatReader(GameSession s) => _s = s;

    public int Hp => _s.ReadRole(Roles.Hp) ?? -1;
    public int MaxHp => _s.ReadRole(Roles.MaxHp) ?? -1;
    public int Sp => _s.ReadRole(Roles.Sp) ?? -1;
    public int MaxSp => _s.ReadRole(Roles.MaxSp) ?? -1;
    public int Exp => _s.ReadRole(Roles.Exp) ?? -1;
    public int Zeny => _s.ReadRole(Roles.Zeny) ?? -1;
    public int Weight => _s.ReadRole(Roles.Weight) ?? -1;
    public int MaxWeight => _s.ReadRole(Roles.MaxWeight) ?? -1;
    public int PosX => _s.ReadRole(Roles.PosX) ?? -1;
    public int PosY => _s.ReadRole(Roles.PosY) ?? -1;

    public double HpPercent => Pct(Hp, MaxHp);
    public double SpPercent => Pct(Sp, MaxSp);
    public double WeightPercent => Pct(Weight, MaxWeight);
    private static double Pct(int c, int m) => m > 0 ? Math.Clamp(c * 100.0 / m, 0, 100) : -1;
}
