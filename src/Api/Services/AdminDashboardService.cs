using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using EmployeeMonitoring.Api.Services;
using EmployeeMonitoring.Contracts;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using AgentConfiguration = EmployeeMonitoring.Contracts.AgentConfiguration;
using GlobalConfiguration = EmployeeMonitoring.Contracts.GlobalConfiguration;
using AlertRule = EmployeeMonitoring.Contracts.AlertRule;
using ActivitySummary = EmployeeMonitoring.Contracts.ActivitySummary;
using PauseStatistics = EmployeeMonitoring.Contracts.PauseStatistics;
using DlpStatistics = EmployeeMonitoring.Contracts.DlpStatistics;
using ReportJob = EmployeeMonitoring.Contracts.ReportJob;
using HealthStatus = EmployeeMonitoring.Contracts.HealthStatus;
using ProductivityLevel = EmployeeMonitoring.Contracts.ProductivityLevel;
using PauseAction = EmployeeMonitoring.Contracts.PauseAction;
using ReportStatus = EmployeeMonitoring.Contracts.ReportStatus;

namespace EmployeeMonitoring.Api.Services;

/// <summary>
/// Service for admin dashboard operations.
/// </summary>
public interface IAdminDashboardService
{
    Task WatchAgentsAsync(WatchAgentsRequest request, IServerStreamWriter<AgentStatusUpdate> responseStream, CancellationToken cancellationToken);
    Task WatchAgentDetailsAsync(WatchAgentDetailsRequest request, IServerStreamWriter<AgentDetailUpdate> responseStream, CancellationToken cancellationToken);
    Task GetScreenshotsAsync(GetScreenshotsRequest request, IServerStreamWriter<ScreenshotData> responseStream, CancellationToken cancellationToken);
    Task<ScreenshotData> GetScreenshotAsync(GetScreenshotRequest request);
    Task<ActivitySummary> GetActivitySummaryAsync(GetActivitySummaryRequest request);
    Task<ProductivityReport> GetProductivityReportAsync(GetProductivityReportRequest request);
    Task<TeamProductivityReport> GetTeamProductivityAsync(GetTeamProductivityRequest request);
    Task GetPauseEventsAsync(GetPauseEventsRequest request, IServerStreamWriter<PauseEventRecord> responseStream, CancellationToken cancellationToken);
    Task<PauseStatistics> GetPauseStatisticsAsync(GetPauseStatisticsRequest request);
    Task<ForceResumeResponse> ForceResumeAgentAsync(ForceResumeRequest request);
    Task<SendPauseCommandResponse> SendPauseCommandAsync(SendPauseCommandRequest request);
    Task GetDlpEventsAsync(GetDlpEventsRequest request, IServerStreamWriter<DlpEventRecord> responseStream, CancellationToken cancellationToken);
    Task<DlpStatistics> GetDlpStatisticsAsync(GetDlpStatisticsRequest request);
    Task<AcknowledgeDlpEventResponse> AcknowledgeDlpEventAsync(AcknowledgeDlpEventRequest request);
    Task<AgentConfiguration> GetAgentConfigurationAsync(GetAgentConfigRequest request);
    Task<UpdateAgentConfigResponse> UpdateAgentConfigurationAsync(UpdateAgentConfigRequest request);
    Task<GlobalConfiguration> GetGlobalConfigurationAsync(GlobalConfigRequest request);
    Task<UpdateGlobalConfigResponse> UpdateGlobalConfigurationAsync(UpdateGlobalConfigRequest request);
    Task<DeployConfigResponse> DeployConfigurationAsync(DeployConfigRequest request);
    Task<UserList> ListUsersAsync(ListUsersRequest request);
    Task<UserProfile> GetUserAsync(GetUserRequest request);
    Task<UpdateUserResponse> UpdateUserAsync(UpdateUserRequest request);
    Task<DeviceList> ListDevicesAsync(ListDevicesRequest request);
    Task<DeviceDetail> GetDeviceAsync(GetDeviceRequest request);
    Task<DecommissionResponse> DecommissionDeviceAsync(DecommissionDeviceRequest request);
    Task GetConsentStatusesAsync(GetConsentStatusesRequest request, IServerStreamWriter<ConsentStatusRecord> responseStream, CancellationToken cancellationToken);
    Task<RequestConsentResponse> RequestConsentAsync(RequestConsentRequest request);
    Task GetAuditLogAsync(GetAuditLogRequest request, IServerStreamWriter<AuditLogEntry> responseStream, CancellationToken cancellationToken);
    Task<ExportAuditLogResponse> ExportAuditLogAsync(ExportAuditLogRequest request);
    Task<ComplianceReport> GetComplianceReportAsync(GetComplianceReportRequest request);
    Task GetAlertsAsync(GetAlertsRequest request, IServerStreamWriter<AlertRecord> responseStream, CancellationToken cancellationToken);
    Task<AcknowledgeAlertResponse> AcknowledgeAlertAsync(AcknowledgeAlertRequest request);
    Task<AlertRule> CreateAlertRuleAsync(CreateAlertRuleRequest request);
    Task<AlertRule> UpdateAlertRuleAsync(UpdateAlertRuleRequest request);
    Task<DeleteAlertRuleResponse> DeleteAlertRuleAsync(DeleteAlertRuleRequest request);
    Task<AlertRuleList> ListAlertRulesAsync(ListAlertRulesRequest request);
    Task<ReportJob> GenerateReportAsync(GenerateReportRequest request);
    Task<ReportJob> GetReportStatusAsync(GetReportStatusRequest request);
    Task DownloadReportAsync(DownloadReportRequest request, IServerStreamWriter<ReportChunk> responseStream, CancellationToken cancellationToken);
}

public class AdminDashboardService : IAdminDashboardService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IScreenshotRepository _screenshotRepository;
    private readonly IActivityRepository _activityRepository;
    private readonly IDlpRepository _dlpRepository;
    private readonly IPauseEventRepository _pauseRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly IAgentConnectionManager _agentConnectionManager;
    private readonly IAdminConnectionManager _adminConnectionManager;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AdminDashboardService> _logger;

    public AdminDashboardService(
        IAgentRepository agentRepository,
        IUserRepository userRepository,
        IScreenshotRepository screenshotRepository,
        IActivityRepository activityRepository,
        IDlpRepository dlpRepository,
        IPauseEventRepository pauseRepository,
        IAuditRepository auditRepository,
        IAgentConnectionManager agentConnectionManager,
        IAdminConnectionManager adminConnectionManager,
        IAuditService auditService,
        INotificationService notificationService,
        ILogger<AdminDashboardService> logger)
    {
        _agentRepository = agentRepository;
        _userRepository = userRepository;
        _screenshotRepository = screenshotRepository;
        _activityRepository = activityRepository;
        _dlpRepository = dlpRepository;
        _pauseRepository = pauseRepository;
        _auditRepository = auditRepository;
        _agentConnectionManager = agentConnectionManager;
        _adminConnectionManager = adminConnectionManager;
        _auditService = auditService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task WatchAgentsAsync(WatchAgentsRequest request, IServerStreamWriter<AgentStatusUpdate> responseStream, CancellationToken cancellationToken)
    {
        // Initial snapshot
        var agents = await GetFilteredAgentsAsync(request);
        foreach (var agent in agents)
        {
            await responseStream.WriteAsync(MapToStatusUpdate(agent));
        }

        // Subscribe to updates (simplified - in production use SignalR groups)
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(5000, cancellationToken);
            
            agents = await GetFilteredAgentsAsync(request);
            foreach (var agent in agents)
            {
                await responseStream.WriteAsync(MapToStatusUpdate(agent));
            }
        }
    }

    private async Task<List<Agent>> GetFilteredAgentsAsync(WatchAgentsRequest request)
    {
        var agents = await _agentRepository.GetAllAsync();
        
        if (request.GroupIds.Count > 0)
            agents = agents.Where(a => a.TeamId.HasValue && request.GroupIds.Contains(a.TeamId.Value.ToString())).ToList();
        
        if (request.DepartmentIds.Count > 0)
            agents = agents.Where(a => a.DepartmentId.HasValue && request.DepartmentIds.Contains(a.DepartmentId.Value.ToString())).ToList();

        if (!request.IncludeOffline)
            agents = agents.Where(a => a.Status != AgentStatus.Offline && a.Status != AgentStatus.Unregistered).ToList();

        return agents;
    }

    private AgentStatusUpdate MapToStatusUpdate(Agent agent)
    {
        var pauseState = agent.IsPaused ? "paused" : "active";
        return new AgentStatusUpdate
        {
            AgentId = agent.AgentId,
            DeviceName = agent.DeviceName,
            UserName = agent.User?.DisplayName ?? "Unknown",
            Department = agent.Department?.Name ?? "Unknown",
            State = (AgentState)(int)agent.Status,
            LastHeartbeat = agent.LastHeartbeat?.ToUnixTimeMilliseconds() ?? 0,
            LastScreenshot = agent.LastScreenshot?.ToUnixTimeMilliseconds() ?? 0,
            LastActivity = agent.LastActivity?.ToUnixTimeMilliseconds() ?? 0,
            IsPaused = agent.IsPaused,
            CurrentPauseDurationSeconds = agent.CurrentPauseDurationSeconds,
            Health = (HealthStatus)(int)agent.Health,
            Tags = { ["role"] = agent.User?.Role ?? "employee" }
        };
    }

    public async Task WatchAgentDetailsAsync(WatchAgentDetailsRequest request, IServerStreamWriter<AgentDetailUpdate> responseStream, CancellationToken cancellationToken)
    {
        // In production, this would subscribe to real-time events via SignalR
        await Task.CompletedTask;
    }

    public async Task GetScreenshotsAsync(GetScreenshotsRequest request, IServerStreamWriter<ScreenshotData> responseStream, CancellationToken cancellationToken)
    {
        var screenshots = await _screenshotRepository.GetByAgentAsync(
            (await _agentRepository.GetByAgentIdAsync(request.AgentId))?.Id ?? Guid.Empty,
            DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime),
            DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime),
            request.Limit,
            cancellationToken);

        foreach (var screenshot in screenshots)
        {
            await responseStream.WriteAsync(new ScreenshotData
            {
                Id = screenshot.Id.ToString(),
                AgentId = request.AgentId,
                CapturedAt = screenshot.CapturedAt.ToUnixTimeMilliseconds(),
                MonitorIndex = screenshot.MonitorIndex,
                Width = screenshot.Width,
                Height = screenshot.Height,
                Thumbnail = Google.Protobuf.ByteString.CopyFrom(screenshot.ThumbnailData),
                FullImage = request.IncludeBlurred ? Google.Protobuf.ByteString.CopyFrom(screenshot.ImageData) : Google.Protobuf.ByteString.Empty,
                Format = screenshot.Format,
                Blurred = screenshot.Blurred,
                ActiveWindowTitle = screenshot.ActiveWindowTitle ?? string.Empty,
                ActiveProcessName = screenshot.ActiveProcessName ?? string.Empty,
                Productivity = (ProductivityLevel)(int)screenshot.Productivity
            });
        }
    }

    public async Task<ScreenshotData> GetScreenshotAsync(GetScreenshotRequest request)
    {
        var screenshot = await _screenshotRepository.GetByIdAsync(Guid.Parse(request.ScreenshotId));
        if (screenshot == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Screenshot not found"));

        return new ScreenshotData
        {
            Id = screenshot.Id.ToString(),
            AgentId = screenshot.Agent.AgentId,
            CapturedAt = screenshot.CapturedAt.ToUnixTimeMilliseconds(),
            MonitorIndex = screenshot.MonitorIndex,
            Width = screenshot.Width,
            Height = screenshot.Height,
            Thumbnail = Google.Protobuf.ByteString.CopyFrom(screenshot.ThumbnailData),
            FullImage = request.IncludeBlurred ? Google.Protobuf.ByteString.CopyFrom(screenshot.ImageData) : Google.Protobuf.ByteString.Empty,
            Format = screenshot.Format,
            Blurred = screenshot.Blurred,
            ActiveWindowTitle = screenshot.ActiveWindowTitle ?? string.Empty,
            ActiveProcessName = screenshot.ActiveProcessName ?? string.Empty,
            Productivity = (ProductivityLevel)(int)screenshot.Productivity
        };
    }

    public async Task<ActivitySummary> GetActivitySummaryAsync(GetActivitySummaryRequest request)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(request.AgentId);
        if (agent == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Agent not found"));

        var summary = await _activityRepository.GetSummaryAsync(
            agent.Id,
            DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime),
            DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime));

        return new ActivitySummary
        {
            AgentId = request.AgentId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Buckets = { /* Would build time buckets */ },
            Totals = new ActivityTotals
            {
                TotalSeconds = (int)summary.TotalSeconds,
                ProductiveSeconds = (int)summary.ProductiveSeconds,
                NeutralSeconds = (int)summary.NeutralSeconds,
                DistractingSeconds = (int)summary.DistractingSeconds,
                IdleSeconds = (int)summary.IdleSeconds,
                ProductivityScore = summary.ProductivityScore,
                ProcessBreakdown = { summary.ProcessBreakdown },
                CategoryBreakdown = { summary.CategoryBreakdown }
            }
        };
    }

    public async Task<ProductivityReport> GetProductivityReportAsync(GetProductivityReportRequest request)
    {
        // Implementation would aggregate across multiple agents
        return new ProductivityReport
        {
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TotalAgents = 0,
            Agents = { },
            Team = new TeamAggregates()
        };
    }

    public async Task<TeamProductivityReport> GetTeamProductivityAsync(GetTeamProductivityRequest request)
    {
        return new TeamProductivityReport { TeamId = request.TeamId };
    }

    public async Task GetPauseEventsAsync(GetPauseEventsRequest request, IServerStreamWriter<PauseEventRecord> responseStream, CancellationToken cancellationToken)
    {
        List<Guid>? agentIds = null;
        if (request.AgentIds.Count > 0)
        {
            agentIds = new List<Guid>();
            foreach (var id in request.AgentIds)
            {
                var a = await _agentRepository.GetByAgentIdAsync(id);
                agentIds.Add(a?.Id ?? Guid.Empty);
            }
        }

        Models.PauseAction? filterAction = request.FilterAction != PauseAction.Paused
            ? (Models.PauseAction)(int)request.FilterAction
            : null;

        var events = await _pauseRepository.GetByAgentAsync(
            agentIds?.FirstOrDefault() ?? Guid.Empty,
            request.StartTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime) : null,
            request.EndTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime) : null,
            filterAction,
            request.Limit);

        foreach (var evt in events)
        {
            await responseStream.WriteAsync(new PauseEventRecord
            {
                EventId = evt.Id.ToString(),
                AgentId = evt.Agent.AgentId,
                UserName = evt.Agent.User?.DisplayName ?? "Unknown",
                Department = evt.Agent.Department?.Name ?? "Unknown",
                Timestamp = evt.Timestamp.ToUnixTimeMilliseconds(),
                Action = (PauseAction)(int)evt.Action,
                Reason = evt.Reason,
                PauseDurationSeconds = evt.PauseDurationSeconds,
                AdminNotified = evt.AdminNotified,
                AdminNotificationId = evt.AdminNotificationId ?? string.Empty
            });
        }
    }

    public async Task<PauseStatistics> GetPauseStatisticsAsync(GetPauseStatisticsRequest request)
    {
        List<Guid>? agentIds = null;
        if (request.AgentIds.Count > 0)
        {
            agentIds = new List<Guid>();
            foreach (var id in request.AgentIds)
            {
                var a = await _agentRepository.GetByAgentIdAsync(id);
                agentIds.Add(a?.Id ?? Guid.Empty);
            }
        }

        var stats = await _pauseRepository.GetStatisticsAsync(agentIds, 
            request.StartTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime) : null,
            request.EndTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime) : null);

        // Map service DTO -> Contracts message
        return new PauseStatistics
        {
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TotalPauseEvents = stats.TotalPauseEvents,
            TotalPauseDurationSeconds = (int)stats.TotalPauseDurationSeconds,
            AvgPauseDurationSeconds = stats.AveragePauseDurationSeconds,
            MaxPauseDurationSeconds = stats.MaxPauseDurationSeconds
        };
    }

    public async Task<ForceResumeResponse> ForceResumeAgentAsync(ForceResumeRequest request)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(request.AgentId);
        if (agent == null)
            return new ForceResumeResponse { Success = false, Message = "Agent not found" };

        // Force resume via SignalR
        // await _agentConnectionManager.SendToAgentAsync(agent.AgentId, "ForceResume", new { });

        await _auditService.LogAdminActionAsync(
            request.AdminUserId,
            "FORCE_RESUME",
            "agent",
            agent.AgentId,
            $"Force resumed by admin. Reason: {request.Reason}",
            true);

        return new ForceResumeResponse { Success = true, Message = "Force resume command sent" };
    }

    public async Task<SendPauseCommandResponse> SendPauseCommandAsync(SendPauseCommandRequest request)
    {
        // Send pause command via SignalR
        return new SendPauseCommandResponse { Success = true };
    }

    public async Task GetDlpEventsAsync(GetDlpEventsRequest request, IServerStreamWriter<DlpEventRecord> responseStream, CancellationToken cancellationToken)
    {
        // Implementation would filter and stream events
        await Task.CompletedTask;
    }

    public async Task<DlpStatistics> GetDlpStatisticsAsync(GetDlpStatisticsRequest request)
    {
        List<Guid>? agentIds = null;

        if (request.AgentIds.Count > 0)

        {

            agentIds = new List<Guid>();

            foreach (var id in request.AgentIds)

            {

                var a = await _agentRepository.GetByAgentIdAsync(id);

                agentIds.Add(a?.Id ?? Guid.Empty);

            }

        }

        var dlpStats = await _dlpRepository.GetStatisticsAsync(agentIds?.FirstOrDefault(),
            request.StartTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime) : null,
            request.EndTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime) : null);

        return new DlpStatistics
        {
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TotalEvents = dlpStats.TotalEvents,
            BlockedCount = dlpStats.BlockedCount,
            AcknowledgedCount = dlpStats.AcknowledgedCount
        };
    }

    public async Task<AcknowledgeDlpEventResponse> AcknowledgeDlpEventAsync(AcknowledgeDlpEventRequest request)
    {
        // Implementation
        return new AcknowledgeDlpEventResponse { Success = true };
    }

    public async Task<AgentConfiguration> GetAgentConfigurationAsync(GetAgentConfigRequest request)
    {
        return new AgentConfiguration();
    }

    public async Task<UpdateAgentConfigResponse> UpdateAgentConfigurationAsync(UpdateAgentConfigRequest request)
    {
        return new UpdateAgentConfigResponse { Success = true };
    }

    public async Task<GlobalConfiguration> GetGlobalConfigurationAsync(GlobalConfigRequest request)
    {
        return new GlobalConfiguration { Scope = request.Scope };
    }

    public async Task<UpdateGlobalConfigResponse> UpdateGlobalConfigurationAsync(UpdateGlobalConfigRequest request)
    {
        return new UpdateGlobalConfigResponse { Success = true };
    }

    public async Task<DeployConfigResponse> DeployConfigurationAsync(DeployConfigRequest request)
    {
        return new DeployConfigResponse { Success = true };
    }

    public async Task<UserList> ListUsersAsync(ListUsersRequest request)
    {
        var users = await _userRepository.GetAllAsync();
        
        if (!string.IsNullOrEmpty(request.Department))
            users = users.Where(u => u.Department?.Name == request.Department).ToList();

        if (!string.IsNullOrEmpty(request.Team))
            users = users.Where(u => u.Team?.Name == request.Team).ToList();

        if (request.ActiveOnly)
            users = users.Where(u => u.Active).ToList();

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var total = users.Count;
        var paged = users.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new UserList
        {
            Users = { paged.Select(MapToProfile) },
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private UserProfile MapToProfile(User user)
    {
        return new UserProfile
        {
            UserId = user.Id.ToString(),
            UserSid = user.UserSid,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Department = user.Department?.Name ?? string.Empty,
            Team = user.Team?.Name ?? string.Empty,
            Role = user.Role,
            Active = user.Active,
            LastSeen = user.LastLogin?.ToUnixTimeMilliseconds() ?? 0,
            AgentIds = { user.Agents.Select(a => a.AgentId) }
        };
    }

    public async Task<UserProfile> GetUserAsync(GetUserRequest request)
    {
        var user = await _userRepository.GetByIdAsync(Guid.Parse(request.UserId));
        if (user == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User not found"));
        
        return MapToProfile(user);
    }

    public async Task<UpdateUserResponse> UpdateUserAsync(UpdateUserRequest request)
    {
        return new UpdateUserResponse { Success = true };
    }

    public async Task<DeviceList> ListDevicesAsync(ListDevicesRequest request)
    {
        var agents = await _agentRepository.GetAllAsync();
        
        if (!string.IsNullOrEmpty(request.UserId))
            agents = agents.Where(a => a.UserId == Guid.Parse(request.UserId)).ToList();

        if (request.State != AgentState.AgentUnknown)
            agents = agents.Where(a => (AgentState)(int)a.Status == request.State).ToList();

        return new DeviceList
        {
            Devices = { agents.Select(MapToDeviceDetail) }
        };
    }

    private DeviceDetail MapToDeviceDetail(Agent agent)
    {
        return new DeviceDetail
        {
            AgentId = agent.AgentId,
            DeviceId = agent.Id.ToString(),
            DeviceName = agent.DeviceName,
            UserId = agent.UserId?.ToString() ?? string.Empty,
            UserName = agent.User?.DisplayName ?? "Unknown",
            OsVersion = agent.OsVersion ?? string.Empty,
            AgentVersion = agent.AgentVersion ?? string.Empty,
            State = (AgentState)(int)agent.Status,
            EnrolledAt = agent.EnrolledAt.ToUnixTimeMilliseconds(),
            LastHeartbeat = agent.LastHeartbeat?.ToUnixTimeMilliseconds() ?? 0,
            LastScreenshot = agent.LastScreenshot?.ToUnixTimeMilliseconds() ?? 0,
            LastActivity = agent.LastActivity?.ToUnixTimeMilliseconds() ?? 0,
            IsPaused = agent.IsPaused,
            CurrentPauseDurationSeconds = agent.CurrentPauseDurationSeconds,
            Health = (HealthStatus)(int)agent.Health
        };
    }

    public async Task<DeviceDetail> GetDeviceAsync(GetDeviceRequest request)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(request.AgentId);
        if (agent == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Device not found"));
        
        return MapToDeviceDetail(agent);
    }

    public async Task<DecommissionResponse> DecommissionDeviceAsync(DecommissionDeviceRequest request)
    {
        var agent = await _agentRepository.GetByAgentIdAsync(request.AgentId);
        if (agent == null)
            return new DecommissionResponse { Success = false, Message = "Agent not found" };

        agent.Status = AgentStatus.Unregistered;
        await _agentRepository.UpdateAsync(agent);

        await _auditService.LogAdminActionAsync(
            request.AdminUserId,
            "DECOMMISSION_DEVICE",
            "agent",
            agent.AgentId,
            request.Reason,
            true);

        return new DecommissionResponse { Success = true };
    }

    public async Task GetConsentStatusesAsync(GetConsentStatusesRequest request, IServerStreamWriter<ConsentStatusRecord> responseStream, CancellationToken cancellationToken)
    {
        // Implementation
        await Task.CompletedTask;
    }

    public async Task<RequestConsentResponse> RequestConsentAsync(RequestConsentRequest request)
    {
        return new RequestConsentResponse { Success = true };
    }

    public async Task GetAuditLogAsync(GetAuditLogRequest request, IServerStreamWriter<AuditLogEntry> responseStream, CancellationToken cancellationToken)
    {
        var logs = await _auditRepository.GetAsync(
            request.ActorIds.Count > 0 ? request.ActorIds : null,
            request.Actions.Count > 0 ? request.Actions.Select(a => a.ToString()) : null,
            request.StartTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime) : null,
            request.EndTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime) : null,
            request.TargetTypes.Count > 0 ? request.TargetTypes : null,
            request.Limit);

        foreach (var log in logs)
        {
            await responseStream.WriteAsync(new AuditLogEntry
            {
                LogId = log.Id.ToString(),
                Timestamp = log.Timestamp.ToUnixTimeMilliseconds(),
                ActorId = log.ActorId,
                ActorName = log.ActorName,
                ActorRole = log.ActorRole,
                Action = Enum.TryParse<AuditAction>(log.Action, out var action) ? action : AuditAction.Login,
                TargetType = log.TargetType,
                TargetId = log.TargetId,
                TargetName = log.TargetName,
                Details = log.Details,
                IpAddress = log.IpAddress,
                UserAgent = log.UserAgent,
                Success = log.Success,
                ErrorMessage = log.ErrorMessage ?? string.Empty
            });
        }
    }

    public async Task<ExportAuditLogResponse> ExportAuditLogAsync(ExportAuditLogRequest request)
    {
        return new ExportAuditLogResponse { DownloadUrl = "/api/exports/audit-log.csv" };
    }

    public async Task<ComplianceReport> GetComplianceReportAsync(GetComplianceReportRequest request)
    {
        return new ComplianceReport
        {
            GeneratedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PeriodStart = request.StartTime,
            PeriodEnd = request.EndTime
        };
    }

    public async Task GetAlertsAsync(GetAlertsRequest request, IServerStreamWriter<AlertRecord> responseStream, CancellationToken cancellationToken)
    {
        // Implementation
        await Task.CompletedTask;
    }

    public async Task<AcknowledgeAlertResponse> AcknowledgeAlertAsync(AcknowledgeAlertRequest request)
    {
        return new AcknowledgeAlertResponse { Success = true };
    }

    public async Task<AlertRule> CreateAlertRuleAsync(CreateAlertRuleRequest request)
    {
        return new AlertRule { RuleId = Guid.NewGuid().ToString() };
    }

    public async Task<AlertRule> UpdateAlertRuleAsync(UpdateAlertRuleRequest request)
    {
        return new AlertRule { RuleId = request.RuleId };
    }

    public async Task<DeleteAlertRuleResponse> DeleteAlertRuleAsync(DeleteAlertRuleRequest request)
    {
        return new DeleteAlertRuleResponse { Success = true };
    }

    public async Task<AlertRuleList> ListAlertRulesAsync(ListAlertRulesRequest request)
    {
        return new AlertRuleList { Rules = { } };
    }

    public async Task<ReportJob> GenerateReportAsync(GenerateReportRequest request)
    {
        return new ReportJob { JobId = Guid.NewGuid().ToString(), Status = EmployeeMonitoring.Contracts.ReportStatus.Queued };
    }

    public async Task<ReportJob> GetReportStatusAsync(GetReportStatusRequest request)
    {
        return new ReportJob { JobId = request.JobId, Status = EmployeeMonitoring.Contracts.ReportStatus.Completed };
    }

    public async Task DownloadReportAsync(DownloadReportRequest request, IServerStreamWriter<ReportChunk> responseStream, CancellationToken cancellationToken)
    {
        // Stream report file
        await Task.CompletedTask;
    }
}