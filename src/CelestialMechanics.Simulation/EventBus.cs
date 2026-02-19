namespace CelestialMechanics.Simulation;

public class SimulationEvent
{
    public string Type { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double Time { get; init; }
}

public class EventBus
{
    private readonly List<Action<SimulationEvent>> _subscribers = new();

    public void Subscribe(Action<SimulationEvent> handler)
    {
        _subscribers.Add(handler);
    }

    public void Unsubscribe(Action<SimulationEvent> handler)
    {
        _subscribers.Remove(handler);
    }

    public void Publish(SimulationEvent evt)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber(evt);
        }
    }

    public void Clear()
    {
        _subscribers.Clear();
    }
}
