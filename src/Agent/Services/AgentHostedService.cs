using EmployeeMonitoring.Agent.Modules;
using EmployeeMonitoring.Agent.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Main hosted service that orchestrates all monitoring modules.
/// </summary>
public class AgentHostedService : IHostedService
{
    private readonly IAgentIdentityProvider _identityProvider;
    private readonly IConsentManager _consentManager;
    private readonly IPauseManager _pauseManager;
    private readonly IScreenshotService _screenshotService;
    private readonly IActivityService _activityService;
    private readonly IDlpService _dlpService;
    private readonly IGrpcClient _grpcClient;
    private readonly ISignalRClient _signalRClient;
    private readonly IMessageDispatcher _messageDispatcher;
    private readonly ILogger<AgentHostedService> _logger;
    
    private Timer? _heartbeatTimer;
    private bool _started;

    public AgentHostedService(
        IAgentIdentityProvider identityProvider,
        IConsentManager consentManager,
        IPauseManager pauseManager,
        IScreenshotService screenshotService,
        IActivityService activityService,
        IDlpService dlpService,
        IGrpcClient grpcClient,
        ISignalRClient signalRClient,
        IMessageDispatcher messageDispatcher,
        ILogger<AgentHostedService> logger)
    {
        _identityProvider = identityProvider;
        _consentManager = consentManager;
        _pauseManager = pauseManager;
        _screenshotService = screenshotService;
        _activityService = activityService;
        _dlpService = dlpService;
        _grpcClient = grpcClient;
        _signalRClient = signalRClient;
        _messageDispatcher = messageDispatcher;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started) return;
        
        _logger.LogInformation("Starting Agent Hosted Service");

        try
        {
            // Initialize identity
            await _identityProvider.InitializeAsync(cancellationToken);

            // Initialize consent
            await _consentManager.InitializeAsync(cancellationToken);

            // Initialize pause manager
            await _pauseManager.InitializeAsync(cancellationToken);

            // Subscribe to module events
            SubscribeToEvents();

            // Connect to server
            await ConnectToServerAsync(cancellationToken);

            // Start monitoring modules (only if consent given)
            await StartMonitoringModulesAsync(cancellationToken);

            // Start heartbeat
            StartHeartbeat();

            _started = true;
            _logger.LogInformation("Agent Hosted Service started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Agent Hosted Service");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started) return;
        
        _logger.LogInformation("Stopping Agent Hosted Service");

        try
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            // Stop monitoring modules
            await _screenshotService.StopAsync(cancellationToken);
            await _activityService.StopAsync(cancellationToken);
            await _dlpService.StopAsync(cancellationToken);

            // Disconnect from server
            await _grpcClient.DisconnectAsync(cancellationToken);
            await _signalRClient.DisconnectAsync(cancellationToken);

            _started = false;
            _logger.LogInformation("Agent Hosted Service stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Agent Hosted Service");
        }
    }

    private void SubscribeToEvents()
    {
        _screenshotService.ScreenshotCaptured += OnScreenshotsCaptured;
        _screenshotService.ScreenshotError += OnScreenshotError;

        _activityService.ActivitySampled += OnActivitySampled;
        _activityService.ActivityError += OnActivityError;

        _dlpService.DlpEventDetected += OnDlpEventDetected;
        _dlpService.DlpError += OnDlpError;

        _pauseManager.PauseStateChanged += OnPauseStateChanged;
    }

    private async Task ConnectToServerAsync(CancellationToken cancellationToken)
    {
        var grpcConnected = await _grpcClient.ConnectAsync(cancellationToken);
        if (!grpcConnected)
        {
            _logger.LogWarning("gRPC connection failed, will retry");
        }

        var signalRConnected = await _signalRClient.ConnectAsync(cancellationToken);
        if (!signalRConnected)
        {
            _logger.LogWarning("SignalR connection failed, will retry");
        }

        // Subscribe to server commands
        _signalRClient.ConfigUpdateReceived += OnConfigUpdateReceived;
        _signalRClient.PauseCommandReceived += OnPauseCommandReceived;
        _signalRClient.ConsentRequestReceived += OnConsentRequestReceived;
        _signalRClient.DiagnosticCommandReceived += OnDiagnosticCommandReceived;
    }

    private async Task StartMonitoringModulesAsync(CancellationToken cancellationToken)
    {
        if (_consentManager.IsModuleConsented("screenshots"))
        {
            await _screenshotService.StartAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation("Screenshots not started: consent not granted");
        }

        if (_consentManager.IsModuleConsented("activity"))
        {
            await _activityService.StartAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation("Activity tracking not started: consent not granted");
        }

        if (_consentManager.IsModuleConsented("dlp"))
        {
            await _dlpService.StartAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation("DLP monitoring not started: consent not granted");
        }
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private async Task SendHeartbeatAsync()
    {
        try
        {
            var health = new AgentHealth
            {
                Healthy = true,
                CpuPercent = GetCpuUsage(),
                MemoryMb = GC.GetTotalMemory(false) / 1024 / 1024,
                DiskFreePercent = GetDiskFreePercent(),
                NetworkLatencyMs = 0, // Would measure actual latency
                ScreenshotsPending = 0,
                ActivitiesPending = 0,
                DlpEventsPending = 0,
                LastError = null
            };

            await _messageDispatcher.SendHeartbeatAsync(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
        }
    }

    private int GetCpuUsage()
    {
        // Simplified CPU usage - in production would use PerformanceCounter
        return Random.Shared.Next(5, 25);
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

    // Event handlers
    private async void OnScreenshotsCaptured(object? sender, ScreenshotCapturedEventArgs e)
    {
        _messageDispatcher.QueueScreenshot(e.Screenshots);
        await _messageDispatcher.FlushScreenshotBatchAsync(CancellationToken.None);
    }

    private void OnScreenshotError(object? sender, ScreenshotErrorEventArgs e)
    {
        _logger.LogError("Screenshot error: {Error}", e.Error);
    }

    private async void OnActivitySampled(object? sender, ActivitySampledEventArgs e)
    {
        _messageDispatcher.QueueActivity(e.Sample);
    }

    private void OnActivityError(object? sender, ActivityErrorEventArgs e)
    {
        _logger.LogError("Activity error: {Error}", e.Error);
    }

    private async void OnDlpEventDetected(object? sender, DlpEventDetectedEventArgs e)
    {
        _messageDispatcher.QueueDlpEvent(e.Event);
        
        // Also send immediately for high severity
        if (e.Event.Severity >= Severity.High)
        {
            await _messageDispatcher.SendDlpEventAsync(e.Event);
        }
    }

    private void OnDlpError(object? sender, DlpErrorEventArgs e)
    {
        _logger.LogError("DLP error: {Error}", e.Error);
    }

    private async void OnPauseStateChanged(object? sender, PauseStateChangedEventArgs e)
    {
        var pauseEvent = new PauseEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Action = e.NewState.IsPaused ? PauseAction.Paused : PauseAction.Resumed,
            Reason = e.NewState.PauseReason ?? string.Empty,
            UserSid = _identityProvider.UserSid,
            PauseDurationSeconds = e.NewState.IsPaused ? 0 : (int)e.NewState.CurrentPauseDuration.TotalSeconds,
            AdminNotified = e.NewState.AdminNotified
        };

        _messageDispatcher.QueuePauseEvent(pauseEvent);
        await _messageDispatcher.SendPauseEventAsync(pauseEvent);
    }

    private void OnConfigUpdateReceived(object? sender, ConfigUpdate e)
    {
        _logger.LogInformation("Received config update v{Version}", e.ConfigVersion);
        // Configuration would be applied via ConfigurationManager
    }

    private async void OnPauseCommandReceived(object? sender, PauseCommand e)
    {
        _logger.LogInformation("Received pause command: {Type}", e.Type);
        
        switch (e.Type)
        {
            case PauseCommandType.RequestPause:
                await _pauseManager.RequestPauseAsync(e.Reason);
                break;
            case PauseCommandType.ForceResume:
                await _pauseManager.ForceResumeAsync(e.AdminUserId, e.Reason);
                break;
            case PauseCommandType.SetMaxPause:
                await _pauseManager.SetMaxPauseAsync(TimeSpan.FromSeconds(e.DurationSeconds));
                break;
        }
    }

    private async void OnConsentRequestReceived(object? sender, ConsentRequest e)
    {
        _logger.LogInformation("Received consent request");
        await _consentManager.RequestConsentAsync();
    }

    private void OnDiagnosticCommandReceived(object? sender, DiagnosticCommand e)
    {
        _logger.LogInformation("Received diagnostic command: {Type}", e.Type);
        // Handle diagnostic commands
    }
}