using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace EmployeeMonitoring.Common.Health;

/// <summary>
/// Health check for the monitoring agent itself.
/// </summary>
public class AgentHealthCheck : IHealthCheck
{
    private readonly IAgentHealthProvider _healthProvider;

    public AgentHealthCheck(IAgentHealthProvider healthProvider)
    {
        _healthProvider = healthProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = _healthProvider.GetHealth();
        
        var data = new Dictionary<string, object>
        {
            ["cpu_percent"] = health.CpuPercent,
            ["memory_mb"] = health.MemoryMb,
            ["disk_free_percent"] = health.DiskFreePercent,
            ["network_latency_ms"] = health.NetworkLatencyMs,
            ["screenshots_pending"] = health.ScreenshotsPending,
            ["activities_pending"] = health.ActivitiesPending,
            ["dlp_events_pending"] = health.DlpEventsPending,
            ["last_error"] = health.LastError ?? "none",
            ["uptime_seconds"] = (DateTimeOffset.UtcNow - Process.GetCurrentProcess().StartTime).TotalSeconds
        };

        return Task.FromResult(health.Healthy 
            ? HealthCheckResult.Healthy("Agent is healthy", data)
            : HealthCheckResult.Unhealthy("Agent is unhealthy", null, data));
    }
}

/// <summary>
/// Interface for providing agent health metrics.
/// </summary>
public interface IAgentHealthProvider
{
    AgentHealthData GetHealth();
}

/// <summary>
/// Health data structure.
/// </summary>
public record AgentHealthData(
    bool Healthy,
    int CpuPercent,
    long MemoryMb,
    int DiskFreePercent,
    int NetworkLatencyMs,
    int ScreenshotsPending,
    int ActivitiesPending,
    int DlpEventsPending,
    string? LastError
);