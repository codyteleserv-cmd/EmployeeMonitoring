namespace EmployeeMonitoring.Dashboard.Models;

/// <summary>
/// Agent view model for dashboard display.
/// </summary>
public class AgentViewModel
{
    public string AgentId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public AgentState Status { get; set; } = AgentState.Unknown;
    public bool IsPaused { get; set; }
    public HealthStatus Health { get; set; } = HealthStatus.Unknown;
    public DateTimeOffset? LastHeartbeat { get; set; }
    public DateTimeOffset? LastScreenshot { get; set; }
    public DateTimeOffset? LastActivity { get; set; }
    public int PauseMinutesToday { get; set; }
    public int DlpEventsToday { get; set; }
    public double ProductivityScore { get; set; }
    public string? CurrentPauseReason { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}

public enum AgentState
{
    Unknown = 0,
    Online = 1,
    Paused = 2,
    Offline = 3,
    Unregistered = 4,
    Error = 5
}

public enum HealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2,
    Unknown = 3
}

/// <summary>
/// Screenshot view model.
/// </summary>
public class ScreenshotViewModel
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public int MonitorIndex { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public bool Blurred { get; set; }
    public List<BlurRegionViewModel> BlurRegions { get; set; } = new();
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public string ActiveProcessName { get; set; } = string.Empty;
    public ProductivityLevel Productivity { get; set; } = ProductivityLevel.Unknown;
    public string ThumbnailBase64 { get; set; } = string.Empty;
}

public class BlurRegionViewModel
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public BlurReason Reason { get; set; }
}

public enum BlurReason
{
    PasswordField = 0,
    CreditCardField = 1,
    SsnField = 2,
    CustomPattern = 3,
    UserRequested = 4
}

public enum ProductivityLevel
{
    Unknown = 0,
    Productive = 1,
    Neutral = 2,
    Distracting = 3
}

/// <summary>
/// Activity view model.
/// </summary>
public class ActivityViewModel
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public int DurationSeconds { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string WindowClass { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public ProductivityLevel Productivity { get; set; } = ProductivityLevel.Unknown;
    public bool IsIdle { get; set; }
    public int IdleSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public InputActivityLevel InputLevel { get; set; } = InputActivityLevel.None;
}

public enum InputActivityLevel
{
    None = 0,
    Low = 1,
    Moderate = 2,
    High = 3
}

/// <summary>
/// DLP event view model.
/// </summary>
public class DlpEventViewModel
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public DlpEventType Type { get; set; }
    public Severity Severity { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public bool Blocked { get; set; }
    public bool Acknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}

public enum DlpEventType
{
    FileAccess = 0,
    FileCopy = 1,
    FileUpload = 2,
    ClipboardPii = 3,
    CrmBulkExport = 4,
    UsbDevice = 5,
    CloudUpload = 6,
    PrintJob = 7,
    EmailAttachment = 8
}

public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Pause event view model.
/// </summary>
public class PauseEventViewModel
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public PauseAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int PauseDurationSeconds { get; set; }
    public bool AdminNotified { get; set; }
    public string? AdminNotificationId { get; set; }
}

public enum PauseAction
{
    Paused = 0,
    Resumed = 1,
    ForceResumed = 2,
    Expired = 3
}

/// <summary>
/// Pause statistics view model.
/// </summary>
public class PauseStatisticsViewModel
{
    public long TotalPauseEvents { get; set; }
    public long TotalPauseDurationSeconds { get; set; }
    public double AveragePauseDurationSeconds { get; set; }
    public int MaxPauseDurationSeconds { get; set; }
    public List<PauseByAgentViewModel> AgentBreakdown { get; set; } = new();
    public List<PauseByReasonViewModel> ReasonBreakdown { get; set; } = new();
    public List<PauseByTimeViewModel> TimeBreakdown { get; set; } = new();
}

public class PauseByAgentViewModel
{
    public string AgentId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int PauseCount { get; set; }
    public long TotalPauseSeconds { get; set; }
    public double AveragePauseSeconds { get; set; }
}

public class PauseByReasonViewModel
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
    public long TotalSeconds { get; set; }
}

public class PauseByTimeViewModel
{
    public string HourBucket { get; set; } = string.Empty;
    public int PauseCount { get; set; }
}