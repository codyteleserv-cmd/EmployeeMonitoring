using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmployeeMonitoring.Api.Models;

/// <summary>
/// Monitoring agent entity.
/// </summary>
public class Agent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string AgentId { get; set; } = string.Empty; // Unique agent identifier

    [Required]
    [MaxLength(200)]
    public string DeviceName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? OsVersion { get; set; }

    [MaxLength(50)]
    public string? AgentVersion { get; set; }

    public AgentStatus Status { get; set; } = AgentStatus.Offline;

    public DateTimeOffset? LastHeartbeat { get; set; }
    public DateTimeOffset? LastScreenshot { get; set; }
    public DateTimeOffset? LastActivity { get; set; }
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsPaused { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public string? PauseReason { get; set; }
    public int CurrentPauseDurationSeconds { get; set; }

    public HealthStatus Health { get; set; } = HealthStatus.Unknown;

    // Foreign keys
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    // Navigation
    public ICollection<Screenshot> Screenshots { get; set; } = new List<Screenshot>();
    public ICollection<ActivitySample> ActivitySamples { get; set; } = new List<ActivitySample>();
    public ICollection<DlpEvent> DlpEvents { get; set; } = new List<DlpEvent>();
    public ICollection<PauseEvent> PauseEvents { get; set; } = new List<PauseEvent>();
    public AgentConfiguration? Configuration { get; set; }
    public ConsentRecord? ConsentRecord { get; set; }
}

public enum AgentStatus
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
/// User entity.
/// </summary>
public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string UserSid { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "employee"; // employee, team_lead, admin, security, hr

    public bool Active { get; set; } = true;
    public DateTimeOffset? LastLogin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Foreign keys
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    // Navigation
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
}

/// <summary>
/// Department entity.
/// </summary>
public class Department
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool Active { get; set; } = true;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
    public ICollection<Team> Teams { get; set; } = new List<Team>();
}

/// <summary>
/// Team entity.
/// </summary>
public class Team
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
}

/// <summary>
/// Screenshot entity.
/// </summary>
public class Screenshot
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public DateTimeOffset CapturedAt { get; set; }
    public int MonitorIndex { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    
    [Column(TypeName = "bytea")]
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    
    [Column(TypeName = "bytea")]
    public byte[] ThumbnailData { get; set; } = Array.Empty<byte>();

    [MaxLength(20)]
    public string Format { get; set; } = "jpeg";

    public bool Blurred { get; set; }
    
    [Column(TypeName = "jsonb")]
    public string BlurRegionsJson { get; set; } = "[]";

    [MaxLength(500)]
    public string? ActiveWindowTitle { get; set; }

    [MaxLength(200)]
    public string? ActiveProcessName { get; set; }

    public ProductivityLevel Productivity { get; set; } = ProductivityLevel.Unknown;
}

public enum ProductivityLevel
{
    Unknown = 0,
    Productive = 1,
    Neutral = 2,
    Distracting = 3
}

/// <summary>
/// Activity sample entity.
/// </summary>
public class ActivitySample
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }
    public int DurationSeconds { get; set; }

    [MaxLength(200)]
    public string ProcessName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string WindowTitle { get; set; } = string.Empty;

    [MaxLength(200)]
    public string WindowClass { get; set; } = string.Empty;

    [MaxLength(200)]
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
/// DLP event entity.
/// </summary>
public class DlpEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }
    public DlpEventType Type { get; set; }
    public Severity Severity { get; set; } = Severity.Info;

    [MaxLength(200)]
    public string ProcessName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Details { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string MetadataJson { get; set; } = "{}";

    public bool Blocked { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public User? AcknowledgedByUser { get; set; }
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
/// Pause event entity.
/// </summary>
public class PauseEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }
    public PauseAction Action { get; set; }

    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public int PauseDurationSeconds { get; set; }
    public bool AdminNotified { get; set; }

    [MaxLength(100)]
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
/// Agent configuration entity.
/// </summary>
public class AgentConfiguration
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public string ConfigurationJson { get; set; } = "{}";

    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Global configuration entity.
/// </summary>
public class GlobalConfiguration
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Scope { get; set; } = string.Empty; // "global", "department:IT", "team:Sales"

    [Column(TypeName = "jsonb")]
    public string ConfigurationJson { get; set; } = "{}";

    [Column(TypeName = "jsonb")]
    public string OverridesJson { get; set; } = "{}";

    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Consent record entity.
/// </summary>
public class ConsentRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    [MaxLength(100)]
    public string UserSid { get; set; } = string.Empty;

    public bool ConsentGiven { get; set; }
    public DateTimeOffset ConsentTimestamp { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(50)]
    public string ConsentVersion { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string GrantedModulesJson { get; set; } = "[]";

    public bool RequiresRenewal { get; set; }
    public DateTimeOffset? RenewalDeadline { get; set; }

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(500)]
    public string UserAgent { get; set; } = string.Empty;
}

/// <summary>
/// Alert rule entity.
/// </summary>
public class AlertRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }

    [Column(TypeName = "jsonb")]
    public string ConditionJson { get; set; } = "{}";

    [Column(TypeName = "jsonb")]
    public string TargetGroupsJson { get; set; } = "[]";

    [Column(TypeName = "jsonb")]
    public string TargetDepartmentsJson { get; set; } = "[]";

    [Column(TypeName = "jsonb")]
    public string ChannelsJson { get; set; } = "[]";

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum AlertType
{
    AgentOffline = 0,
    AgentError = 1,
    PauseExceeded = 2,
    PauseWithoutReason = 3,
    DlpHighSeverity = 4,
    DlpCritical = 5,
    ConsentExpired = 6,
    ConsentRevoked = 7,
    ConfigDrift = 8,
    ProductivityLow = 9,
    UnauthorizedAccess = 10,
    HeartbeatMissed = 11
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Alert entity.
/// </summary>
public class Alert
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? AgentId { get; set; }
    public Agent? Agent { get; set; }

    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Acknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public User? AcknowledgedByUser { get; set; }

    [Column(TypeName = "jsonb")]
    public string MetadataJson { get; set; } = "{}";
}

/// <summary>
/// Audit log entity (immutable).
/// </summary>
public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [MaxLength(100)]
    public string ActorId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string ActorName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string ActorRole { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string TargetType { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string TargetId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string TargetName { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Details { get; set; } = string.Empty;

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(500)]
    public string UserAgent { get; set; } = string.Empty;

    public bool Success { get; set; } = true;

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}