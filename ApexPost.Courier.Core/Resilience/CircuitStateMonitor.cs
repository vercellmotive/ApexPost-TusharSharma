namespace ApexPost.Courier.Core.Resilience;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen,
}

/// <summary>
/// The circuit breaker's current state, for the screens. While it is open the app shows the offline banner and
/// keeps saving scans on the device.
/// </summary>
public sealed class CircuitStateMonitor
{
    private int _state;

    public CircuitState State => (CircuitState)Volatile.Read(ref _state);

    public bool IsOpen => State == CircuitState.Open;

    public event EventHandler<CircuitState>? Changed;

    internal void Set(CircuitState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state)
        {
            Changed?.Invoke(this, state);
        }
    }
}
