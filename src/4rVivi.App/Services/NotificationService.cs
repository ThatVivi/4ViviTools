using FourRVivi.Core.Events;

namespace FourRVivi.App.Services;

/// <summary>Raises app notifications through the EventBus so any view can show them.</summary>
public sealed class NotificationService
{
    private readonly IEventBus _bus;
    public NotificationService(IEventBus bus) => _bus = bus;
    public void Notify(string title, string message) => _bus.Publish(new NotificationEvent(title, message));
    public void Error(string title, string message) => _bus.Publish(new NotificationEvent(title, message, true));
}
