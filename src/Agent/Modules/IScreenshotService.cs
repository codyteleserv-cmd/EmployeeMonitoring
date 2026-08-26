using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Contracts;

namespace EmployeeMonitoring.Agent.Modules;

/// <summary>
/// Screenshot capture service with smart blur.
/// </summary>
public interface IScreenshotService
{
    event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;
    event EventHandler<ScreenshotErrorEventArgs>? ScreenshotError;
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<ScreenshotResult> CaptureNowAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

public class ScreenshotCapturedEventArgs : EventArgs
{
    public List<Screenshot> Screenshots { get; set; } = new();
    public DateTimeOffset CapturedAt { get; set; }
}

public class ScreenshotErrorEventArgs : EventArgs
{
    public string Error { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public class ScreenshotResult
{
    public bool Success { get; set; }
    public List<Screenshot> Screenshots { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// Activity tracking service.
/// </summary>
public interface IActivityService
{
    event EventHandler<ActivitySampledEventArgs>? ActivitySampled;
    event EventHandler<ActivityErrorEventArgs>? ActivityError;
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<ActivitySample> SampleNowAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

public class ActivitySampledEventArgs : EventArgs
{
    public ActivitySample Sample { get; set; } = new();
}

public class ActivityErrorEventArgs : EventArgs
{
    public string Error { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

/// <summary>
/// DLP monitoring service.
/// </summary>
public interface IDlpService
{
    event EventHandler<DlpEventDetectedEventArgs>? DlpEventDetected;
    event EventHandler<DlpErrorEventArgs>? DlpError;
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<List<DlpEvent>> ScanNowAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

public class DlpEventDetectedEventArgs : EventArgs
{
    public DlpEvent Event { get; set; } = new();
}

public class DlpErrorEventArgs : EventArgs
{
    public string Error { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}