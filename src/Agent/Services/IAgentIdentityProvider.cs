namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Provides agent identity information.
/// </summary>
public interface IAgentIdentityProvider
{
    string AgentId { get; }
    string DeviceId { get; }
    string DeviceName { get; }
    string UserSid { get; }
    string UserDisplayName { get; }
    string Department { get; }
    Dictionary<string, string> Tags { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages agent configuration with hot-reload support.
/// </summary>
public interface IConfigurationManager
{
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
    
    T GetConfiguration<T>() where T : class, new();
    Task<T> GetConfigurationAsync<T>(CancellationToken cancellationToken = default) where T : class, new();
    Task UpdateConfigurationAsync<T>(T configuration, string updatedBy, CancellationToken cancellationToken = default) where T : class;
    Task<bool> ValidateConfigurationAsync<T>(T configuration, CancellationToken cancellationToken = default) where T : class;
}

public class ConfigurationChangedEventArgs : EventArgs
{
    public string ConfigurationType { get; set; } = string.Empty;
    public object? OldConfiguration { get; set; }
    public object? NewConfiguration { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// Manages user consent for monitoring.
/// </summary>
public interface IConsentManager
{
    event EventHandler<ConsentChangedEventArgs>? ConsentChanged;
    
    ConsentStatus GetConsentStatus();
    Task<bool> RequestConsentAsync(CancellationToken cancellationToken = default);
    Task<bool> RecordConsentAsync(ConsentRecord record, CancellationToken cancellationToken = default);
    Task<bool> RevokeConsentAsync(string reason, CancellationToken cancellationToken = default);
    bool IsConsentValid();
    bool IsModuleConsented(string module);
}

public class ConsentStatus
{
    public bool ConsentGiven { get; set; }
    public DateTimeOffset? ConsentTimestamp { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public List<string> GrantedModules { get; set; } = new();
    public bool RequiresRenewal { get; set; }
    public DateTimeOffset? RenewalDeadline { get; set; }
}

public class ConsentRecord
{
    public string AgentId { get; set; } = string.Empty;
    public string UserSid { get; set; } = string.Empty;
    public bool ConsentGiven { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public List<string> GrantedModules { get; set; } = new();
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public class ConsentChangedEventArgs : EventArgs
{
    public ConsentStatus OldStatus { get; set; } = new();
    public ConsentStatus NewStatus { get; set; } = new();
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// Manages pause/resume state with admin notifications.
/// </summary>
public interface IPauseManager
{
    event EventHandler<PauseStateChangedEventArgs>? PauseStateChanged;
    
    PauseState GetPauseState();
    Task<PauseResult> RequestPauseAsync(string reason, CancellationToken cancellationToken = default);
    Task<PauseResult> RequestResumeAsync(CancellationToken cancellationToken = default);
    Task<PauseResult> ForceResumeAsync(string adminUserId, string reason, CancellationToken cancellationToken = default);
    Task<PauseResult> SetMaxPauseAsync(TimeSpan maxPause, CancellationToken cancellationToken = default);
    bool CanPause();
    TimeSpan GetRemainingPauseTime();
    DateTimeOffset? GetPauseStartTime();
}

public class PauseState
{
    public bool IsPaused { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public string? PauseReason { get; set; }
    public string? PausedBy { get; set; }
    public TimeSpan TotalPauseDuration { get; set; }
    public TimeSpan CurrentPauseDuration { get; set; }
    public TimeSpan MaxPausePerDay { get; set; }
    public DateTimeOffset? MaxPauseResetTime { get; set; }
    public bool AdminNotified { get; set; }
    public string? AdminNotificationId { get; set; }
}

public class PauseResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public PauseState? NewState { get; set; }
    public string? CommandId { get; set; }
}

public class PauseStateChangedEventArgs : EventArgs
{
    public PauseState OldState { get; set; } = new();
    public PauseState NewState { get; set; } = new();
    public string TriggeredBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// Audit logger for compliance.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task LogAgentActionAsync(string action, string details, bool success, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task LogAdminActionAsync(string adminUserId, string action, string targetType, string targetId, string details, bool success, CancellationToken cancellationToken = default);
    Task LogPauseEventAsync(string agentId, string userName, string action, string reason, int durationSeconds, bool adminNotified, CancellationToken cancellationToken = default);
    Task LogDlpEventAsync(string agentId, string userName, string eventType, string severity, string details, bool blocked, CancellationToken cancellationToken = default);
    Task LogScreenshotAccessAsync(string adminUserId, string agentId, string screenshotId, CancellationToken cancellationToken = default);
}

public record AuditLogEntry(
    string LogId,
    DateTimeOffset Timestamp,
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
    string? ErrorMessage
);