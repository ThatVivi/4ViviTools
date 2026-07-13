using FourRVivi.Core.Game;

namespace FourRVivi.Core.Input;

public sealed record InputRouteResult(
    InputRouteStatus Status,
    InputActionKind Action,
    InputMethod Method,
    string Backend,
    string Reason,
    long LatencyMs,
    IntPtr WindowHandle,
    int? ClientX,
    int? ClientY,
    bool Ok,
    FocusGateSnapshot? Focus)
{
    public bool Sent => Status == InputRouteStatus.Sent && Ok;
}
