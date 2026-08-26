using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Common.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Background service for health reporting and self-monitoring.
/// </summary>
public class HealthReportingService : BackgroundService
{
    private readonly IAgentHealthProvider _healthProvider;
    private readonly ILogger<HealthReportingService> _logger;
    private readonly TimeSpan _reportInterval = TimeSpan.FromMinutes(5);

    public HealthReportingService(
        IAgentHealthProvider healthProvider,
        ILogger<HealthReportingService> logger)
    {
        _healthProvider = healthProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health reporting service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var health = _healthProvider.GetHealth();
                
                if (!health.Healthy)
                {
                    _logger.LogWarning("Agent health degraded: {Error}", health.LastError);
                }
                
                _logger.LogDebug("Health: CPU={Cpu}%, Mem={Mem}MB, Disk={Disk}%, Latency={Latency}ms",
                    health.CpuPercent, health.MemoryMb, health.DiskFreePercent, health.NetworkLatencyMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
            }

            try
            {
                await Task.Delay(_reportInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Health reporting service stopped");
    }
}

/// <summary>
/// Implementation of agent health provider.
/// </summary>
public class AgentHealthProvider : IAgentHealthProvider
{
    private readonly ILogger<AgentHealthProvider> _logger;
    private long _lastCpuTime;
    private DateTime _lastCpuCheck;

    public AgentHealthProvider(ILogger<AgentHealthProvider> logger)
    {
        _logger = logger;
    }

    public AgentHealthData GetHealth()
    {
        try
        {
            var cpuPercent = GetCpuUsage();
            var memoryMb = GC.GetTotalMemory(false) / 1024 / 1024;
            var diskFreePercent = GetDiskFreePercent();
            
            var healthy = cpuPercent < 80 && memoryMb < 500 && diskFreePercent > 10;
            var lastError = healthy ? null : GetHealthError(cpuPercent, memoryMb, diskFreePercent);

            return new AgentHealthData(
                Healthy: healthy,
                CpuPercent: cpuPercent,
                MemoryMb: memoryMb,
                DiskFreePercent: diskFreePercent,
                NetworkLatencyMs: 0,
                ScreenshotsPending: 0,
                ActivitiesPending: 0,
                DlpEventsPending: 0,
                LastError: lastError
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health data");
            return new AgentHealthData(
                Healthy: false,
                CpuPercent: 0,
                MemoryMb: 0,
                DiskFreePercent: 0,
                NetworkLatencyMs: 0,
                ScreenshotsPending: 0,
                ActivitiesPending: 0,
                DlpEventsPending: 0,
                LastError: ex.Message
            );
        }
    }

    private int GetCpuUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var cpuTime = process.TotalProcessorTime.TotalMilliseconds;
            
            if (_lastCpuCheck != default)
            {
                var timeElapsed = (now - _lastCpuCheck).TotalMilliseconds;
                var cpuElapsed = cpuTime - _lastCpuTime;
                var cpuPercent = (int)(cpuElapsed / (timeElapsed * Environment.ProcessorCount) * 100);
                
                _lastCpuTime = cpuTime;
                _lastCpuCheck = now;
                
                return Math.Clamp(cpuPercent, 0, 100);
            }
            
            _lastCpuTime = cpuTime;
            _lastCpuCheck = now;
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private int GetDiskFreePercent()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            return (int)((double)drive.AvailableFreeSpace / drive.TotalSize * 100);
        }
        catch
        {
            return 50;
        }
    }

    private string GetHealthError(int cpu, long mem, int disk)
    {
        var errors = new List<string>();
        if (cpu >= 80) errors.Add($"High CPU: {cpu}%");
        if (mem >= 500) errors.Add($"High Memory: {mem}MB");
        if (disk <= 10) errors.Add($"Low Disk: {disk}%");
        return string.Join("; ", errors);
    }
}