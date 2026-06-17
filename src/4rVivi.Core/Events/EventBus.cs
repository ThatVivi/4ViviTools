namespace FourRVivi.Core.Events;

public sealed record NotificationEvent(string Title, string Message, bool IsError = false);

public interface IEventBus
{
    void Publish<T>(T evt);
    IDisposable Subscribe<T>(Action<T> handler);
}

/// <summary>Thread-safe in-process pub/sub so modules/plugins communicate without direct references.</summary>
public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _gate = new();

    public void Publish<T>(T evt)
    {
        List<Delegate> snapshot;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            snapshot = list.ToList();
        }
        foreach (var d in snapshot) ((Action<T>)d)(evt);
    }

    public IDisposable Subscribe<T>(Action<T> handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list)) { list = new(); _handlers[typeof(T)] = list; }
            list.Add(handler);
        }
        return new Subscription(() => { lock (_gate) { if (_handlers.TryGetValue(typeof(T), out var l)) l.Remove(handler); } });
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
