using Polly;
using Polly.Retry;

namespace EmployeeMonitoring.Common.Extensions;

/// <summary>
/// Standard retry policies for the monitoring platform.
/// </summary>
public static class RetryPolicies
{
    /// <summary>
    /// Standard exponential backoff for transient failures.
    /// </summary>
    public static AsyncRetryPolicy StandardRetry => Policy
        .Handle<HttpRequestException>()
        .Or<TimeoutException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(
            retryCount: 5,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 60)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
            onRetry: (exception, delay, attempt, context) =>
            {
                // Log retry attempt
            });

    /// <summary>
    /// Aggressive retry for critical operations (heartbeat, registration).
    /// </summary>
    public static AsyncRetryPolicy CriticalRetry => Policy
        .Handle<Exception>()
        .WaitAndRetryForeverAsync(
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 300)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 5000)),
            onRetry: (exception, delay, attempt, context) =>
            {
                // Log critical retry
            });

    /// <summary>
    /// Fast retry for real-time operations (screenshots, activities).
    /// </summary>
    public static AsyncRetryPolicy FastRetry => Policy
        .Handle<HttpRequestException>()
        .Or<TimeoutException>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)),
            onRetry: (exception, delay, attempt, context) => { });

    /// <summary>
    /// Circuit breaker for external dependencies.
    /// </summary>
    public static AsyncCircuitBreakerPolicy CircuitBreaker => Policy
        .Handle<HttpRequestException>()
        .Or<TimeoutException>()
        .CircuitBreakerAsync(
            exceptionsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromMinutes(1),
            onBreak: (exception, duration) => { /* Log circuit open */ },
            onReset: () => { /* Log circuit closed */ },
            onHalfOpen: () => { /* Log circuit half-open */ });
}