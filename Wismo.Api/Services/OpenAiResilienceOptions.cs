namespace WismoAI.Core.Services;

public sealed class OpenAiResilienceOptions
{
    public int RetryCount { get; set; } = 3;
    public int BaseDelayMilliseconds { get; set; } = 400;
    public int JitterMilliseconds { get; set; } = 250;
    public int PerAttemptTimeoutSeconds { get; set; } = 20;
    public int CircuitBreakerFailures { get; set; } = 5;
    public int CircuitBreakSeconds { get; set; } = 30;
}
