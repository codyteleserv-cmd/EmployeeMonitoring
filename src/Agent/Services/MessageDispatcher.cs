using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Contracts;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Message dispatcher for batching and sending messages to server.
/// </summary>
public class MessageDispatcher : IMessageDispatcher, IDisposable
{
    private readonly IGrpcClient _grpcClient;
    private readonly IAgentIdentityProvider _identityProvider;
    private readonly IOptionsMonitor<ScreenshotConfiguration> _screenshotConfig;
    private readonly IOptionsMonitor<ActivityConfiguration> _activityConfig;
    private readonly ILogger<MessageDispatcher> _logger;
    
    // Batching queues
    private readonly Channel<Screenshot> _screenshotQueue = Channel.CreateUnbounded<Screenshot>();
    private readonly Channel<ActivitySample> _activityQueue = Channel.CreateUnbounded<ActivitySample>();
    private readonly Channel<PauseEvent> _pauseQueue = Channel.CreateUnbounded<PauseEvent>();
    private readonly Channel<DlpEvent> _dlpQueue = Channel.CreateUnbounded<DlpEvent>();
    
    private Timer? _screenshotBatchTimer;
    private Timer? _activityBatchTimer;
    private readonly SemaphoreSlim _dispatchLock = new(1, 1);
    private bool _disposed;

    public MessageDispatcher(
        IGrpcClient grpcClient,
        IAgentIdentityProvider identityProvider,
        IOptionsMonitor<ScreenshotConfiguration> screenshotConfig,
        IOptionsMonitor<ActivityConfiguration> activityConfig,
        ILogger<MessageDispatcher> logger)
    {
        _grpcClient = grpcClient;
        _identityProvider = identityProvider;
        _screenshotConfig = screenshotConfig;
        _activityConfig = activityConfig;
        _logger = logger;

        // Start batch timers
        _screenshotBatchTimer = new Timer(
            async _ => await FlushScreenshotBatchAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        _activityBatchTimer = new Timer(
            async _ => await FlushActivityBatchAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    public async Task<bool> SendScreenshotBatchAsync(List<Screenshot> screenshots, CancellationToken cancellationToken = default)
    {
        if (screenshots.Count == 0) return true;

        try
        {
            var batch = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Screenshots = new ScreenshotBatch { Screenshots = { screenshots } }
            };

            return await _grpcClient.SendAsync(batch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send screenshot batch");
            return false;
        }
    }

    public async Task<bool> SendActivityBatchAsync(List<ActivitySample> activities, CancellationToken cancellationToken = default)
    {
        if (activities.Count == 0) return true;

        try
        {
            var batch = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Activities = new ActivityBatch { Samples = { activities } }
            };

            return await _grpcClient.SendAsync(batch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send activity batch");
            return false;
        }
    }

    public async Task<bool> SendPauseEventAsync(PauseEvent pauseEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PauseEvent = pauseEvent
            };

            return await _grpcClient.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send pause event");
            return false;
        }
    }

    public async Task<bool> SendDlpEventAsync(DlpEvent dlpEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DlpEvent = dlpEvent
            };

            return await _grpcClient.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DLP event");
            return false;
        }
    }

    public async Task<bool> SendHeartbeatAsync(AgentHealth health, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Heartbeat = new HeartbeatRequest
                {
                    AgentId = _identityProvider.AgentId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Health = health
                }
            };

            return await _grpcClient.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
            return false;
        }
    }

    public async Task<bool> SendDiagnosticInfoAsync(DiagnosticInfo info, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Diagnostics = info
            };

            return await _grpcClient.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send diagnostic info");
            return false;
        }
    }

    public async Task<bool> SendConsentAckAsync(ConsentAck ack, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ConsentAck = ack
            };

            return await _grpcClient.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send consent ack");
            return false;
        }
    }

    public async Task<bool> SendAdminNotificationAsync(object notification, CancellationToken cancellationToken = default)
    {
        // Would serialize and send via gRPC or SignalR
        _logger.LogInformation("Admin notification queued: {Type}", notification.GetType().Name);
        return true;
    }

    // Queue methods for batching
    public void QueueScreenshot(Screenshot screenshot)
    {
        _screenshotQueue.Writer.TryWrite(screenshot);
    }

    public void QueueActivity(ActivitySample activity)
    {
        _activityQueue.Writer.TryWrite(activity);
    }

    public void QueuePauseEvent(PauseEvent pauseEvent)
    {
        _pauseQueue.Writer.TryWrite(pauseEvent);
    }

    public void QueueDlpEvent(DlpEvent dlpEvent)
    {
        _dlpQueue.Writer.TryWrite(dlpEvent);
    }

    private async Task FlushScreenshotBatchAsync(CancellationToken cancellationToken)
    {
        var batch = new List<Screenshot>();
        var maxBatchSize = _screenshotConfig.CurrentValue.MaxBatchSize;

        while (batch.Count < maxBatchSize && _screenshotQueue.Reader.TryRead(out var screenshot))
        {
            batch.Add(screenshot);
        }

        if (batch.Count > 0)
        {
            await SendScreenshotBatchAsync(batch, cancellationToken);
        }
    }

    private async Task FlushActivityBatchAsync(CancellationToken cancellationToken)
    {
        var batch = new List<ActivitySample>();
        const int maxBatchSize = 100;

        while (batch.Count < maxBatchSize && _activityQueue.Reader.TryRead(out var activity))
        {
            batch.Add(activity);
        }

        if (batch.Count > 0)
        {
            await SendActivityBatchAsync(batch, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _screenshotBatchTimer?.Dispose();
        _activityBatchTimer?.Dispose();
        _screenshotQueue.Writer.Complete();
        _activityQueue.Writer.Complete();
        _pauseQueue.Writer.Complete();
        _dlpQueue.Writer.Complete();
        _dispatchLock.Dispose();
        _disposed = true;
    }
}