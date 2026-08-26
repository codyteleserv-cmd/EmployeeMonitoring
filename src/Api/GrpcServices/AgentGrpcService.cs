using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using EmployeeMonitoring.Api.Services;
using EmployeeMonitoring.Contracts;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace EmployeeMonitoring.Api.GrpcServices;

/// <summary>
/// gRPC service for agent communication.
/// </summary>
public class AgentGrpcService : AgentService.AgentServiceBase
{
    private readonly IAgentRepository _agentRepository;
    private readonly IConsentRepository _consentRepository;
    private readonly IAgentConnectionManager _connectionManager;
    private readonly IMessageQueueService _messageQueue;
    private readonly ILogger<AgentGrpcService> _logger;

    public AgentGrpcService(
        IAgentRepository agentRepository,
        IConsentRepository consentRepository,
        IAgentConnectionManager connectionManager,
        IMessageQueueService messageQueue,
        ILogger<AgentGrpcService> logger)
    {
        _agentRepository = agentRepository;
        _consentRepository = consentRepository;
        _connectionManager = connectionManager;
        _messageQueue = messageQueue;
        _logger = logger;
    }

    public override async Task Connect(IAsyncStreamReader<AgentMessage> requestStream, IServerStreamWriter<ServerMessage> responseStream, ServerCallContext context)
    {
        var agentId = context.GetHttpContext()?.User?.FindFirst("agent_id")?.Value;
        
        if (string.IsNullOrEmpty(agentId))
        {
            _logger.LogWarning("Agent connection attempt without agent_id");
            return;
        }

        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        if (agent == null)
        {
            _logger.LogWarning("Unknown agent attempted connection: {AgentId}", agentId);
            return;
        }

        // Update agent status
        agent.Status = AgentStatus.Online;
        agent.LastHeartbeat = DateTimeOffset.UtcNow;
        await _agentRepository.UpdateAsync(agent);

        // Register connection
        var connectionId = context.Peer; // Or use a proper connection ID
        await _connectionManager.RegisterAgentAsync(agentId, connectionId);

        _logger.LogInformation("Agent {AgentId} connected via gRPC", agentId);

        try
        {
            // Send initial config
            var config = await GetAgentConfigurationInternal(agentId);
            await responseStream.WriteAsync(new ServerMessage
            {
                ConfigUpdate = new ConfigUpdate
                {
                    Configuration = config
                }
            });

            // Process incoming messages
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                await HandleAgentMessageAsync(agentId, message, responseStream, context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in agent gRPC stream for {AgentId}", agentId);
        }
        finally
        {
            agent.Status = AgentStatus.Offline;
            await _agentRepository.UpdateAsync(agent);
            await _connectionManager.UnregisterAgentAsync(agentId);
            _logger.LogInformation("Agent {AgentId} disconnected", agentId);
        }
    }

    private async Task HandleAgentMessageAsync(string agentId, AgentMessage message, IServerStreamWriter<ServerMessage> responseStream, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.PayloadCase)
            {
                case AgentMessage.PayloadOneofCase.Screenshots:
                    await HandleScreenshotsAsync(agentId, message.Screenshots);
                    break;
                case AgentMessage.PayloadOneofCase.Activities:
                    await HandleActivitiesAsync(agentId, message.Activities);
                    break;
                case AgentMessage.PayloadOneofCase.PauseEvent:
                    await HandlePauseEventAsync(agentId, message.PauseEvent);
                    break;
                case AgentMessage.PayloadOneofCase.DlpEvent:
                    await HandleDlpEventAsync(agentId, message.DlpEvent);
                    break;
                case AgentMessage.PayloadOneofCase.Heartbeat:
                    await HandleHeartbeatAsync(agentId, message.Heartbeat);
                    break;
                case AgentMessage.PayloadOneofCase.Diagnostics:
                    await HandleDiagnosticsAsync(agentId, message.Diagnostics);
                    break;
                case AgentMessage.PayloadOneofCase.ConsentAck:
                    await HandleConsentAckAsync(agentId, message.ConsentAck);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle message from agent {AgentId}", agentId);
        }
    }

    private Task HandleScreenshotsAsync(string agentId, ScreenshotBatch batch)
    {
        // Queue for processing
        return _messageQueue.EnqueueScreenshotsAsync(agentId, batch.Screenshots);
    }

    private Task HandleActivitiesAsync(string agentId, ActivityBatch batch)
    {
        return _messageQueue.EnqueueActivitiesAsync(agentId, batch.Samples);
    }

    private Task HandlePauseEventAsync(string agentId, PauseEvent pauseEvent)
    {
        return _messageQueue.EnqueuePauseEventAsync(agentId, pauseEvent);
    }

    private Task HandleDlpEventAsync(string agentId, DlpEvent dlpEvent)
    {
        return _messageQueue.EnqueueDlpEventAsync(agentId, dlpEvent);
    }

    private async Task HandleHeartbeatAsync(string agentId, HeartbeatRequest heartbeat)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        if (agent != null)
        {
            agent.LastHeartbeat = DateTimeOffset.UtcNow;
            agent.Status = AgentStatus.Online;
            // Update health metrics
            await _agentRepository.UpdateAsync(agent);
        }
    }

    private Task HandleDiagnosticsAsync(string agentId, DiagnosticInfo diagnostics)
    {
        // Log diagnostics
        return Task.CompletedTask;
    }

    private Task HandleConsentAckAsync(string agentId, ConsentAck ack)
    {
        // Handle consent acknowledgment
        return Task.CompletedTask;
    }

    public override Task<AgentConfiguration> GetConfiguration(ConfigRequest request, ServerCallContext context)
    {
        var config = GetAgentConfigurationInternal(request.AgentId);
        return Task.FromResult(config.Result);
    }

    public override Task<ConfigResponse> UpdateConfiguration(AgentConfiguration request, ServerCallContext context)
    {
        // Configuration updates come from admin, not agent
        return Task.FromResult(new ConfigResponse { Success = false, Message = "Not allowed" });
    }

    public override Task<ConsentStatus> GetConsentStatus(ConsentRequest request, ServerCallContext context)
    {
        var consent = _consentRepository.GetByAgentId(request.AgentId).Result;
        return Task.FromResult(new ConsentStatus
        {
            ConsentGiven = consent?.ConsentGiven ?? false,
            ConsentTimestamp = consent?.ConsentTimestamp.ToUnixTimeMilliseconds() ?? 0,
            ConsentVersion = consent?.ConsentVersion ?? string.Empty,
            GrantedModules = { consent?.GrantedModules ?? new List<string>() },
            RequiresRenewal = consent?.RequiresRenewal ?? true,
            RenewalDeadline = consent?.RenewalDeadline.ToUnixTimeMilliseconds() ?? 0
        });
    }

    public override Task<ConsentResponse> RecordConsent(ConsentRecord request, ServerCallContext context)
    {
        // Agent doesn't record consent, just acknowledges
        return Task.FromResult(new ConsentResponse { Success = true });
    }

    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HeartbeatResponse
        {
            Acknowledged = true,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    public override Task<DiagnosticsResponse> GetDiagnostics(DiagnosticsRequest request, ServerCallContext context)
    {
        return Task.FromResult(new DiagnosticsResponse
        {
            AgentId = request.AgentId,
            GeneratedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private async Task<AgentConfiguration> GetAgentConfigurationInternal(string agentId)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(agentId);
        // Build configuration based on agent, department, global settings
        return new AgentConfiguration(); // Simplified
    }
}

/// <summary>
/// Repository for consent records.
/// </summary>
public interface IConsentRepository
{
    Task<ConsentRecord?> GetByAgentIdAsync(string agentId, CancellationToken cancellationToken = default);
    Task<ConsentRecord> CreateAsync(ConsentRecord record, CancellationToken cancellationToken = default);
    Task<ConsentRecord> UpdateAsync(ConsentRecord record, CancellationToken cancellationToken = default);
}

public class ConsentRepository : IConsentRepository
{
    private readonly MonitoringDbContext _db;

    public ConsentRepository(MonitoringDbContext db)
    {
        _db = db;
    }

    public async Task<ConsentRecord?> GetByAgentIdAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return await _db.ConsentRecords
            .FirstOrDefaultAsync(c => c.Agent.AgentId == agentId, cancellationToken);
    }

    public async Task<ConsentRecord> CreateAsync(ConsentRecord record, CancellationToken cancellationToken = default)
    {
        _db.ConsentRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<ConsentRecord> UpdateAsync(ConsentRecord record, CancellationToken cancellationToken = default)
    {
        _db.ConsentRecords.Update(record);
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }
}

/// <summary>
/// Message queue service for decoupling gRPC from processing.
/// </summary>
public interface IMessageQueueService
{
    Task EnqueueScreenshotsAsync(string agentId, IEnumerable<Screenshot> screenshots, CancellationToken cancellationToken = default);
    Task EnqueueActivitiesAsync(string agentId, IEnumerable<ActivitySample> activities, CancellationToken cancellationToken = default);
    Task EnqueuePauseEventAsync(string agentId, PauseEvent pauseEvent, CancellationToken cancellationToken = default);
    Task EnqueueDlpEventAsync(string agentId, DlpEvent dlpEvent, CancellationToken cancellationToken = default);
}

public class MessageQueueService : IMessageQueueService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageQueueService> _logger;

    public MessageQueueService(IServiceScopeFactory scopeFactory, ILogger<MessageQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task EnqueueScreenshotsAsync(string agentId, IEnumerable<Screenshot> screenshots, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScreenshotRepository>();
        
        foreach (var screenshot in screenshots)
        {
            var entity = new Models.Screenshot
            {
                AgentId = (await scope.ServiceProvider.GetRequiredService<IAgentRepository>().GetByAgentIdAsync(agentId))?.Id ?? Guid.Empty,
                CapturedAt = DateTimeOffset.FromUnixTimeMilliseconds(screenshot.CapturedAt),
                MonitorIndex = screenshot.MonitorIndex,
                Width = screenshot.Width,
                Height = screenshot.Height,
                ImageData = screenshot.ImageData.ToByteArray(),
                ThumbnailData = Array.Empty<byte>(), // Would generate thumbnail
                Format = screenshot.Format,
                Blurred = screenshot.Blurred,
                BlurRegionsJson = System.Text.Json.JsonSerializer.Serialize(screenshot.BlurRegions),
                ActiveWindowTitle = screenshot.ActiveWindowTitle,
                ActiveProcessName = screenshot.ActiveProcessName,
                Productivity = (ProductivityLevel)screenshot.Productivity
            };
            await repo.CreateAsync(entity);
        }
    }

    public async Task EnqueueActivitiesAsync(string agentId, IEnumerable<ActivitySample> activities, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IActivityRepository>();
        
        var entities = activities.Select(a => new Models.ActivitySample
        {
            AgentId = (await scope.ServiceProvider.GetRequiredService<IAgentRepository>().GetByAgentIdAsync(agentId))?.Id ?? Guid.Empty,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(a.Timestamp),
            DurationSeconds = a.DurationSeconds,
            ProcessName = a.ProcessName,
            WindowTitle = a.WindowTitle,
            WindowClass = a.WindowClass,
            Domain = a.Domain,
            Productivity = (ProductivityLevel)a.Productivity,
            IsIdle = a.IsIdle,
            IdleSeconds = a.IdleSeconds,
            ActiveSeconds = a.ActiveSeconds,
            InputLevel = (InputActivityLevel)a.InputLevel
        }).ToList();

        await repo.CreateBatchAsync(entities);
    }

    public async Task EnqueuePauseEventAsync(string agentId, PauseEvent pauseEvent, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPauseEventRepository>();
        
        var entity = new Models.PauseEvent
        {
            AgentId = (await scope.ServiceProvider.GetRequiredService<IAgentRepository>().GetByAgentIdAsync(agentId))?.Id ?? Guid.Empty,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(pauseEvent.Timestamp),
            Action = (PauseAction)pauseEvent.Action,
            Reason = pauseEvent.Reason,
            PauseDurationSeconds = pauseEvent.PauseDurationSeconds,
            AdminNotified = pauseEvent.AdminNotified,
            AdminNotificationId = pauseEvent.AdminNotificationId
        };
        await repo.CreateAsync(entity);
    }

    public async Task EnqueueDlpEventAsync(string agentId, DlpEvent dlpEvent, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDlpRepository>();
        
        var entity = new Models.DlpEvent
        {
            AgentId = (await scope.ServiceProvider.GetRequiredService<IAgentRepository>().GetByAgentIdAsync(agentId))?.Id ?? Guid.Empty,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dlpEvent.Timestamp),
            Type = (DlpEventType)dlpEvent.Type,
            Severity = (Severity)dlpEvent.Severity,
            ProcessName = dlpEvent.ProcessName,
            FilePath = dlpEvent.FilePath,
            Details = dlpEvent.Details,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(dlpEvent.Metadata),
            Blocked = dlpEvent.Blocked
        };
        await repo.CreateAsync(entity);
    }
}