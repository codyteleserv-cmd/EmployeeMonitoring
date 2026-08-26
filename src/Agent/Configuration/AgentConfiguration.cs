using System.Text.Json.Serialization;

namespace EmployeeMonitoring.Agent.Configuration;

/// <summary>
/// Main agent configuration.
/// </summary>
public class AgentConfiguration
{
    public string GrpcEndpoint { get; set; } = "https://localhost:5001";
    public string SignalRHubUrl { get; set; } = "https://localhost:5001/hubs/agent";
    public string ApiBaseUrl { get; set; } = "https://localhost:5001/api";
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int ReconnectBaseDelaySeconds { get; set; } = 5;
    public int MaxReconnectDelaySeconds { get; set; } = 300;
    public string DeviceId { get; set; } = string.Empty;
    public string UserSid { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
}

/// <summary>
/// Screenshot capture configuration.
/// </summary>
public class ScreenshotConfiguration
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 600; // 10 minutes
    public int Quality { get; set; } = 75;
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;
    public bool SmartBlurEnabled { get; set; } = true;
    public List<string> BlurRegions { get; set; } = new()
    {
        "Chrome_WidgetWin_1",
        "MozillaWindowClass",
        "ApplicationFrameWindow",
        "Windows.UI.Core.CoreWindow"
    };
    public bool MultiMonitor { get; set; } = true;
    public int MaxBatchSize { get; set; } = 10;
}

/// <summary>
/// Activity tracking configuration.
/// </summary>
public class ActivityConfiguration
{
    public bool Enabled { get; set; } = true;
    public int SampleIntervalSeconds { get; set; } = 60;
    public bool TrackForegroundWindow { get; set; } = true;
    public bool TrackIdleTime { get; set; } = true;
    public bool TrackInputActivity { get; set; } = true;
    public List<ProductivityCategoryConfig> Categories { get; set; } = new();
}

/// <summary>
/// Productivity category configuration.
/// </summary>
public class ProductivityCategoryConfig
{
    public string Name { get; set; } = string.Empty;
    public List<string> WindowTitlePatterns { get; set; } = new();
    public List<string> ProcessNames { get; set; } = new();
    public List<string> DomainPatterns { get; set; } = new();
    public int Weight { get; set; } = 5;
}

/// <summary>
/// DLP configuration.
/// </summary>
public class DlpConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool FileAuditEnabled { get; set; } = true;
    public bool ClipboardPiiEnabled { get; set; } = true;
    public bool CrmExportMonitoring { get; set; } = true;
    public List<string> MonitoredPaths { get; set; } = new();
    public List<PiiPatternConfig> PiiPatterns { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
    public List<string> BlockedExtensions { get; set; } = new();
}

/// <summary>
/// PII pattern configuration.
/// </summary>
public class PiiPatternConfig
{
    public string Name { get; set; } = string.Empty;
    public string Regex { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PiiType Type { get; set; }
    public bool RedactInLogs { get; set; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PiiType
{
    Email,
    Phone,
    SSN,
    CreditCard,
    ApiKey,
    Custom
}

/// <summary>
/// Work schedule configuration.
/// </summary>
public class WorkScheduleConfiguration
{
    public string Timezone { get; set; } = "America/New_York";
    public List<WorkDayScheduleConfig> Days { get; set; } = new();
    public bool RespectUserTimezone { get; set; } = true;
}

public class WorkDayScheduleConfig
{
    public int DayOfWeek { get; set; } // 0=Sunday
    public string StartTime { get; set; } = "09:00";
    public string EndTime { get; set; } = "18:00";
    public bool MonitoringEnabled { get; set; } = true;
}

/// <summary>
/// Privacy configuration.
/// </summary>
public class PrivacyConfiguration
{
    public bool SmartBlurEnabled { get; set; } = true;
    public List<string> BlurWindowClasses { get; set; } = new();
    public List<string> BlurFieldTypes { get; set; } = new();
    public bool AnonymizeUserInAggregates { get; set; } = true;
    public int DataRetentionDays { get; set; } = 90;
    public bool AllowUserPause { get; set; } = true;
    public int MaxPauseMinutesPerDay { get; set; } = 60;
    public bool NotifyAdminOnPause { get; set; } = true;
    public bool NotifyAdminOnExit { get; set; } = true;
}

/// <summary>
/// Consent configuration.
/// </summary>
public class ConsentConfiguration
{
    public string PolicyUrl { get; set; } = string.Empty;
    public string ConsentVersion { get; set; } = "1.0";
    public List<string> RequiredModules { get; set; } = new();
    public int RenewalDays { get; set; } = 365;
}