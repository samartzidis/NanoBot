using Microsoft.Extensions.Logging;

namespace NanoBot.Services;

public interface IEvent
{
    DateTime Timestamp { get; }
    object Sender { get; }

    bool SkipLogging { get; }
}

public class EventBase : IEvent
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public object Sender { get; }

    public bool SkipLogging { get; }

    public EventBase()
    {
    }

    public EventBase(object sender)
    {
        Sender = sender;
    }

    public EventBase(object sender, bool skipLogging) : this(sender)
    {
        SkipLogging = skipLogging;
    }
}


public interface IEventBus
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IEvent;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IEvent;
    void Publish<TEvent>(TEvent @event) where TEvent : class, IEvent;
    void Publish<TEvent>(object sender) where TEvent : class, IEvent;
}

public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);

        lock (_lock)
        {
            if (!_handlers.ContainsKey(eventType))
                _handlers[eventType] = new List<Delegate>();

            var handlers = _handlers[eventType];

            // Prevent duplicate handlers
            if (!handlers.Contains(handler))
                handlers.Add(handler);
        }
    }


    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);

        lock (_lock)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(handler);

                if (handlers.Count == 0)
                    _handlers.Remove(eventType);
            }
        }
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);

        if (@event != null && !@event.SkipLogging)
            _logger.LogDebug($"Publishing {eventType.Name} from sender {@event?.Sender}.");

        List <Delegate> handlersCopy;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return;

            handlersCopy = [..handlers];
        }

        foreach (var handler in handlersCopy)
            (handler as Action<TEvent>)?.Invoke(@event);
    }

    public void Publish<TEvent>(object sender) where TEvent : class, IEvent
    {
        if (Activator.CreateInstance(typeof(TEvent), sender) is TEvent eventInstance)
            Publish(eventInstance);
        else
            throw new InvalidOperationException($"Unable to create an instance of {typeof(TEvent)}.");
    }



}