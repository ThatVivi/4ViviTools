namespace FourRVivi.Core.Common;

/// <summary>Lightweight success/diagnostic wrapper used across the engine.</summary>
public readonly record struct OpResult(bool Ok, string? Error = null)
{
    public static OpResult Success => new(true);
    public static OpResult Fail(string error) => new(false, error);
    public static implicit operator bool(OpResult r) => r.Ok;
}
