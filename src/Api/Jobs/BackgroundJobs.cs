using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using EmployeeMonitoring.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace EmployeeMonitoring.Api.Jobs;

/// <summary>
/// Job for monitoring agent health and sending alerts.
/// </summary>
[DisallowConcurrentExecution]
public class AgentHealthMonitoringService : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentHealthMonitoringService> _logger;

    public AgentHealthMonitoringService(IServiceScopeFactory scopeFactory, ILogger<AgentHealthMonitoringService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var threshold = TimeSpan.FromMinutes(5); // Alert if no heartbeat for 5 minutes
        var now = DateTimeOffset.UtcNow;

        var agents = await agentRepository.GetByStatusAsync(AgentStatus.Online);
        var offlineThreshold = now.Subtract(threshold);

        foreach (var agent in agents)
        {
            if (agent.LastHeartbeat.HasValue && agent.LastHeartbeat.Value < offlineThreshold)
            {
                _logger.LogWarning("Agent {AgentId} ({UserName}) missed heartbeat", agent.AgentId, agent.User?.DisplayName);
                
                agent.Status = AgentStatus.Offline;
                agent.Health = HealthStatus.Unhealthy;
                await agentRepository.UpdateAsync(agent);

                if (agent.User != null)
                {
                    await notificationService.NotifyAgentOfflineAsync(
                        agent.AgentId, 
                        agent.User.DisplayName, 
                        now - agent.LastHeartbeat.Value);

                    await auditService.LogAdminActionAsync(
                        "system",
                        "HEARTBEAT_MISSED",
                        "agent",
                        agent.AgentId,
                        $"Agent missed heartbeat for {threshold.TotalMinutes} minutes",
                        true);
                }
            }
            else if (agent.LastHeartbeat.HasValue && now - agent.LastHeartbeat.Value > TimeSpan.FromMinutes(2))
            {
                agent.Health = HealthStatus.Degraded;
                await agentRepository.UpdateAsync(agent);
            }
        }
    }
}

/// <summary>
/// Job for data retention and cleanup.
/// </summary>
[DisallowConcurrentExecution]
public class DataRetentionJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionJob> _logger;
    private readonly IConfiguration _configuration;

    public DataRetentionJob(IServiceScopeFactory scopeFactory, ILogger<DataRetentionJob> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var screenshotRepository = scope.ServiceProvider.GetRequiredService<IScreenshotRepository>();
        var activityRepository = scope.ServiceProvider.GetRequiredService<IActivityRepository>();
        var dlpRepository = scope.ServiceProvider.GetRequiredService<IDlpRepository>();
        var pauseRepository = scope.ServiceProvider.GetRequiredService<IPauseEventRepository>();

        var screenshotsDays = _configuration.GetValue("Retention:ScreenshotsDays", 30);
        var activityDays = _configuration.GetValue("Retention:ActivityDays", 90);
        var dlpDays = _configuration.GetValue("Retention:DlpEventsDays", 365);

        var screenshotCutoff = DateTimeOffset.UtcNow.AddDays(-screenshotsDays);
        var activityCutoff = DateTimeOffset.UtcNow.AddDays(-activityDays);
        var dlpCutoff = DateTimeOffset.UtcNow.AddDays(-dlpDays);

        _logger.LogInformation("Starting data retention cleanup");

        try
        {
            var deletedScreenshots = await screenshotRepository.DeleteOlderThanAsync(screenshotCutoff);
            _logger.LogInformation("Deleted {Count} old screenshots", deletedScreenshots);

            var deletedActivity = await activityRepository.DeleteOlderThanAsync(activityCutoff);
            _logger.LogInformation("Deleted {Count} old activity samples", deletedActivity);

            var deletedDlp = await dlpRepository.DeleteOlderThanAsync(dlpCutoff);
            _logger.LogInformation("Deleted {Count} old DLP events", deletedDlp);

            _logger.LogInformation("Data retention cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data retention cleanup failed");
        }
    }
}

/// <summary>
/// Job for deploying configuration changes to agents.
/// </summary>
[DisallowConcurrentExecution]
public class ConfigurationDeploymentJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConfigurationDeploymentJob> _logger;

    public ConfigurationDeploymentJob(IServiceScopeFactory scopeFactory, ILogger<ConfigurationDeploymentJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var agentConnectionManager = scope.ServiceProvider.GetRequiredService<IAgentConnectionManager>();

        // In production, this would check for pending config deployments
        // and push them to agents via SignalR/gRPC
        
        _logger.LogDebug("Checking for configuration deployments");
        
        await Task.CompletedTask;
    }
}