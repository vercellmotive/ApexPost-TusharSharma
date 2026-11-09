namespace ApexPost.Courier.Core.Resilience;

/// <summary>The retry, circuit breaker and timeout settings. RESILIENCE.md explains each value.</summary>
public sealed record ApexResilienceOptions
{
    /// <summary>Retries after the first attempt, so a request is tried at most four times.</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>The first backoff. Exponential with jitter: about 1 s, 2 s, then 4 s.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest one attempt may take.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The longest a whole request may take, retries and waits included.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The share of failed attempts in the sampling window that opens the circuit.</summary>
    public double FailureRatio { get; init; } = 0.5;

    /// <summary>The fewest attempts in the window before the circuit can open.</summary>
    public int MinimumThroughput { get; init; } = 4;

    /// <summary>The window the failure ratio is measured over.</summary>
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before one trial request is let through.</summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
}
