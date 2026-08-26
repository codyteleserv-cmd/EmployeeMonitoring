using System.Text.Json;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Audit logger for compliance tracking.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly IAgentIdentityProvider _identityProvider;
    private readonly ILogger<AuditLogger> _logger;
    private readonly string _auditLogPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public AuditLogger(
        IAgentIdentityProvider identityProvider,
        ILogger<AuditLogger> logger)
    {
        _identityProvider = identityProvider;
        _logger = logger;
        
        _auditLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EmployeeMonitoring",
            "audit",
            $"audit-{DateTime.UtcNow:yyyy-MM-dd}.log");
        
        Directory.CreateDirectory(Path.GetDirectoryName(_auditLogPath)!);
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        await WriteLogAsync(entry, cancellationToken);
    }

    public async Task LogAgentActionAsync(string action, string details, bool success, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            ActorId: _identityProvider.UserSid,
            ActorName: _identityProvider.UserDisplayName,
            ActorRole: "employee",
            Action: action,
            TargetType: "agent",
            TargetId: _identityProvider.AgentId,
            TargetName: _identityProvider.DeviceName,
            Details: details,
            IpAddress: GetLocalIpAddress(),
            UserAgent: "EmployeeMonitoring.Agent/1.0",
            Success: success,
            ErrorMessage: errorMessage
        );

        await WriteLogAsync(entry, cancellationToken);
    }

    public async Task LogAdminActionAsync(string adminUserId, string action, string targetType, string targetId, string details, bool success, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            ActorId: adminUserId,
            ActorName: adminUserId,
            ActorRole: "admin",
            Action: action,
            TargetType: targetType,
            TargetId: targetId,
            TargetName: targetId,
            Details: details,
            IpAddress: GetLocalIpAddress(),
            UserAgent: "EmployeeMonitoring.Admin/1.0",
            Success: success,
            ErrorMessage: null
        );

        await WriteLogAsync(entry, cancellationToken);
    }

    public async Task LogPauseEventAsync(string agentId, string userName, string action, string reason, int durationSeconds, bool adminNotified, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            ActorId: _identityProvider.UserSid,
            ActorName: userName,
            ActorRole: "employee",
            Action: $"PAUSE_{action}",
            TargetType: "agent",
            TargetId: agentId,
            TargetName: _identityProvider.DeviceName,
            Details: $"Reason: {reason}; Duration: {durationSeconds}s; AdminNotified: {adminNotified}",
            IpAddress: GetLocalIpAddress(),
            UserAgent: "EmployeeMonitoring.Agent/1.0",
            Success: true,
            ErrorMessage: null
        );

        await WriteLogAsync(entry, cancellationToken);
    }

    public async Task LogDlpEventAsync(string agentId, string userName, string eventType, string severity, string details, bool blocked, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            ActorId: _identityProvider.UserSid,
            ActorName: userName,
            ActorRole: "employee",
            Action: $"DLP_{eventType.ToUpper()}",
            TargetType: "dlp",
            TargetId: agentId,
            TargetName: _identityProvider.DeviceName,
            Details: $"Type: {eventType}; Severity: {severity}; Blocked: {blocked}; {details}",
            IpAddress: GetLocalIpAddress(),
            UserAgent: "EmployeeMonitoring.Agent/1.0",
            Success: true,
            ErrorMessage: null
        );

        await WriteLogAsync(entry, cancellationToken);
    }

    public async Task LogScreenshotAccessAsync(string adminUserId, string agentId, string screenshotId, CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(
            LogId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            ActorId: adminUserId,
            ActorName: adminUserId,
            ActorRole: "admin",
            Action: "VIEW_SCREENSHOT",
            TargetType: "screenshot",
            TargetId: screenshotId,
            TargetName: screenshotId,
            Details: $"Admin viewed screenshot from agent {agentId}",
            IpAddress: GetLocalIpAddress(),
            UserAgent: "EmployeeMonitoring.Admin/1.0",
            Success: true,
            ErrorMessage: null
        );

        await WriteLogAsync(entry, cancellationToken);
    }

    private async Task WriteLogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            
            await _fileLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(_auditLogPath, json + Environment.NewLine, cancellationToken);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log");
        }
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}