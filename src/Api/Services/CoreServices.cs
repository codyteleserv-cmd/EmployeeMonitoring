using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using EmployeeMonitoring.Contracts;
using DlpEventType = EmployeeMonitoring.Api.Models.DlpEventType;
using Severity = EmployeeMonitoring.Api.Models.Severity;

namespace EmployeeMonitoring.Api.Services;


/// <summary>Internal audit entry (not the protobuf type).</summary>
public sealed record AuditEntry(
    string LogId,
    long Timestamp,
    string ActorId,
    string ActorName,
    string ActorRole,
    string Action,
    string TargetType,
    string TargetId,
    string TargetName,
    string Details,
    string IpAddress,
    string UserAgent,
    bool Success,
    string? ErrorMessage);

/// <summary>
/// Audit logging service.
/// </summary>
public interface IAuditService
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task LogAgentActionAsync(string agentId, string action, string details, bool success, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task LogAdminActionAsync(string adminUserId, string action, string targetType, string targetId, string details, bool success, CancellationToken cancellationToken = default);
    Task LogPauseEventAsync(string agentId, string userName, string action, string reason, int durationSeconds, bool adminNotified, CancellationToken cancellationToken = default);
    Task LogDlpEventAsync(string agentId, string userName, string eventType, string severity, string details, bool blocked, CancellationToken cancellationToken = default);
    Task LogScreenshotAccessAsync(string adminUserId, string agentId, string screenshotId, CancellationToken cancellationToken = default);
}

public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IAuditRepository auditRepository, IAgentRepository agentRepository, ILogger<AuditService> logger)
    {
        _auditRepository = auditRepository;
        _agentRepository = agentRepository;
        _logger = logger;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var auditLog = new AuditLog
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(entry.Timestamp),
            ActorId = entry.ActorId,
            ActorName = entry.ActorName,
            ActorRole = entry.ActorRole,
            Action = entry.Action.ToString(),
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            TargetName = entry.TargetName,
            Details = entry.Details,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            Success = entry.Success,
            ErrorMessage = entry.ErrorMessage
        };

        await _auditRepository.CreateAsync(auditLog, cancellationToken);
    }

    public async Task LogAgentActionAsync(string agentId, string action, string details, bool success, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        if (agent == null) return;

        var entry = new AuditEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActorId: agent.User?.UserSid ?? agent.AgentId,
            ActorName: agent.User?.DisplayName ?? agent.DeviceName,
            ActorRole: agent.User?.Role ?? "employee",
            Action: action,
            TargetType: "agent",
            TargetId: agentId,
            TargetName: agent.DeviceName,
            Details: details,
            IpAddress: "127.0.0.1", // Would get from context
            UserAgent: "EmployeeMonitoring.Agent/1.0",
            Success: success,
            ErrorMessage: errorMessage
        );

        await LogAsync(entry, cancellationToken);
    }

    public async Task LogAdminActionAsync(string adminUserId, string action, string targetType, string targetId, string details, bool success, CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActorId: adminUserId,
            ActorName: adminUserId,
            ActorRole: "admin",
            Action: action,
            TargetType: targetType,
            TargetId: targetId,
            TargetName: targetId,
            Details: details,
            IpAddress: "127.0.0.1",
            UserAgent: "EmployeeMonitoring.Admin/1.0",
            Success: success,
            ErrorMessage: null
        );

        await LogAsync(entry, cancellationToken);
    }

    public async Task LogPauseEventAsync(string agentId, string userName, string action, string reason, int durationSeconds, bool adminNotified, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        if (agent == null) return;

        var entry = new AuditEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActorId: agent.User?.UserSid ?? agent.AgentId,
            ActorName: userName,
            ActorRole: agent.User?.Role ?? "employee",
            Action: $"PAUSE_{action.ToUpper()}",
            TargetType: "agent",
            TargetId: agentId,
            TargetName: agent.DeviceName,
            Details: $"Reason: {reason}; Duration: {durationSeconds}s; AdminNotified: {adminNotified}",
            IpAddress: "127.0.0.1",
            UserAgent: "EmployeeMonitoring.Agent/1.0",
            Success: true,
            ErrorMessage: null
        );

        await LogAsync(entry, cancellationToken);
    }

    public async Task LogDlpEventAsync(string agentId, string userName, string eventType, string severity, string details, bool blocked, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        if (agent == null) return;

        var entry = new AuditEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActorId: agent.User?.UserSid ?? agent.AgentId,
            ActorName: userName,
            ActorRole: agent.User?.Role ?? "employee",
            Action: $"DLP_{eventType.ToUpper()}",
            TargetType: "dlp",
            TargetId: agentId,
            TargetName: agent.DeviceName,
            Details: $"Type: {eventType}; Severity: {severity}; Blocked: {blocked}; {details}",
            IpAddress: "127.0.0.1",
            UserAgent: "EmployeeMonitoring.Agent/1.0",
            Success: true,
            ErrorMessage: null
        );

        await LogAsync(entry, cancellationToken);
    }

    public async Task LogScreenshotAccessAsync(string adminUserId, string agentId, string screenshotId, CancellationToken cancellationToken = default)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        if (agent == null) return;

        var entry = new AuditEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActorId: adminUserId,
            ActorName: adminUserId,
            ActorRole: "admin",
            Action: "VIEW_SCREENSHOT",
            TargetType: "screenshot",
            TargetId: screenshotId,
            TargetName: screenshotId,
            Details: $"Admin viewed screenshot from agent {agentId}",
            IpAddress: "127.0.0.1",
            UserAgent: "EmployeeMonitoring.Admin/1.0",
            Success: true,
            ErrorMessage: null
        );

        await LogAsync(entry, cancellationToken);
    }
}

/// <summary>
/// Notification service for alerts and admin notifications.
/// </summary>
public interface INotificationService
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    Task SendTeamsAsync(string webhookUrl, string message, CancellationToken cancellationToken = default);
    Task SendSlackAsync(string webhookUrl, string message, CancellationToken cancellationToken = default);
    Task SendWebhookAsync(string url, object payload, CancellationToken cancellationToken = default);
    Task NotifyPauseAsync(string agentId, string userName, string department, string reason, CancellationToken cancellationToken = default);
    Task NotifyDlpAsync(string agentId, string userName, DlpEventType type, Severity severity, string details, CancellationToken cancellationToken = default);
    Task NotifyAgentOfflineAsync(string agentId, string userName, TimeSpan offlineDuration, CancellationToken cancellationToken = default);
}

public class NotificationService : INotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<NotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        // Implementation would use SMTP or email service
        _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        await Task.CompletedTask;
    }

    public async Task SendTeamsAsync(string webhookUrl, string message, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var payload = new { text = message };
        
        await client.PostAsJsonAsync(webhookUrl, payload, cancellationToken);
    }

    public async Task SendSlackAsync(string webhookUrl, string message, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var payload = new { text = message };
        
        await client.PostAsJsonAsync(webhookUrl, payload, cancellationToken);
    }

    public async Task SendWebhookAsync(string url, object payload, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        await client.PostAsJsonAsync(url, payload, cancellationToken);
    }

    public async Task NotifyPauseAsync(string agentId, string userName, string department, string reason, CancellationToken cancellationToken = default)
    {
        var message = $"⏸️ Monitoring paused for {userName} ({department}): {reason}";
        
        var teamsUrl = _configuration["Notifications:Teams:WebhookUrl"];
        if (!string.IsNullOrEmpty(teamsUrl))
            await SendTeamsAsync(teamsUrl, message, cancellationToken);

        var slackUrl = _configuration["Notifications:Slack:WebhookUrl"];
        if (!string.IsNullOrEmpty(slackUrl))
            await SendSlackAsync(slackUrl, message, cancellationToken);
    }

    public async Task NotifyDlpAsync(string agentId, string userName, DlpEventType type, Severity severity, string details, CancellationToken cancellationToken = default)
    {
        var emoji = severity switch
        {
            Severity.Critical => "🔴",
            Severity.High => "🟠",
            Severity.Medium => "🟡",
            _ => "🔵"
        };

        var message = $"{emoji} DLP Alert: {type} for {userName} - {details}";
        
        var teamsUrl = _configuration["Notifications:Teams:WebhookUrl"];
        if (!string.IsNullOrEmpty(teamsUrl))
            await SendTeamsAsync(teamsUrl, message, cancellationToken);
    }

    public async Task NotifyAgentOfflineAsync(string agentId, string userName, TimeSpan offlineDuration, CancellationToken cancellationToken = default)
    {
        var message = $"📴 Agent offline: {userName} ({agentId}) for {offlineDuration.TotalMinutes:F0} minutes";
        
        var teamsUrl = _configuration["Notifications:Teams:WebhookUrl"];
        if (!string.IsNullOrEmpty(teamsUrl))
            await SendTeamsAsync(teamsUrl, message, cancellationToken);
    }
}

/// <summary>
/// Report generation service.
/// </summary>
public interface IReportService
{
    Task<ReportJobInfo> GenerateAsync(ReportType type, DateTimeOffset startTime, DateTimeOffset endTime, IEnumerable<Guid>? agentIds = null, string? requestedBy = null, CancellationToken cancellationToken = default);
    Task<ReportJobInfo> GetStatusAsync(string jobId, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string jobId, CancellationToken cancellationToken = default);
}

public class ReportService : IReportService
{
    private readonly ILogger<ReportService> _logger;

    public ReportService(ILogger<ReportService> logger)
    {
        _logger = logger;
    }

    public async Task<ReportJobInfo> GenerateAsync(ReportType type, DateTimeOffset startTime, DateTimeOffset endTime, IEnumerable<Guid>? agentIds = null, string? requestedBy = null, CancellationToken cancellationToken = default)
    {
        var job = new ReportJobInfo
        {
            JobId = Guid.NewGuid().ToString(),
            Type = type,
            Status = ReportJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // In production, this would be queued to a background job
        _logger.LogInformation("Report job {JobId} created for {Type}", job.JobId, type);
        
        return job;
    }

    public async Task<ReportJobInfo> GetStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return new ReportJobInfo
        {
            JobId = jobId,
            Status = ReportJobStatus.Completed,
            DownloadUrl = $"/api/reports/{jobId}/download"
        };
    }

    public async Task<Stream> DownloadAsync(string jobId, CancellationToken cancellationToken = default)
    {
        // Return report file stream
        return new MemoryStream();
    }
}

public enum ReportType
{
    ProductivitySummary = 0,
    DlpIncidents = 1,
    PauseAnalysis = 2,
    Compliance = 3,
    AgentHealth = 4,
    UserActivity = 5
}

public enum ReportJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4
}

public class ReportJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public ReportType Type { get; set; }
    public ReportJobStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public double Progress { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ErrorMessage { get; set; }
}