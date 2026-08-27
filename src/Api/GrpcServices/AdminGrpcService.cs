using EmployeeMonitoring.Api.Services;
using EmployeeMonitoring.Contracts;
using Grpc.Core;
using ActivitySummary = EmployeeMonitoring.Contracts.ActivitySummary;
using PauseStatistics = EmployeeMonitoring.Contracts.PauseStatistics;
using DlpStatistics = EmployeeMonitoring.Contracts.DlpStatistics;
using ReportJob = EmployeeMonitoring.Contracts.ReportJob;
using AgentConfiguration = EmployeeMonitoring.Contracts.AgentConfiguration;
using GlobalConfiguration = EmployeeMonitoring.Contracts.GlobalConfiguration;
using AlertRule = EmployeeMonitoring.Contracts.AlertRule;

namespace EmployeeMonitoring.Api.GrpcServices;

/// <summary>
/// gRPC service for admin dashboard communication.
/// </summary>
public class AdminGrpcService : AdminService.AdminServiceBase
{
    private readonly IAdminDashboardService _dashboardService;
    private readonly ILogger<AdminGrpcService> _logger;

    public AdminGrpcService(IAdminDashboardService dashboardService, ILogger<AdminGrpcService> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    public override async Task WatchAgents(WatchAgentsRequest request, IServerStreamWriter<AgentStatusUpdate> responseStream, ServerCallContext context)
    {
        var adminId = GetAdminId(context);
        _logger.LogInformation("Admin {AdminId} started watching agents", adminId);

        try
        {
            await _dashboardService.WatchAgentsAsync(request, responseStream, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WatchAgents for admin {AdminId}", adminId);
        }
    }

    public override async Task WatchAgentDetails(WatchAgentDetailsRequest request, IServerStreamWriter<AgentDetailUpdate> responseStream, ServerCallContext context)
    {
        await _dashboardService.WatchAgentDetailsAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task GetScreenshots(GetScreenshotsRequest request, IServerStreamWriter<ScreenshotData> responseStream, ServerCallContext context)
    {
        await _dashboardService.GetScreenshotsAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task<ScreenshotData> GetScreenshot(GetScreenshotRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetScreenshotAsync(request);
    }

    public override async Task<ActivitySummary> GetActivitySummary(GetActivitySummaryRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetActivitySummaryAsync(request);
    }

    public override async Task<ProductivityReport> GetProductivityReport(GetProductivityReportRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetProductivityReportAsync(request);
    }

    public override async Task<TeamProductivityReport> GetTeamProductivity(GetTeamProductivityRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetTeamProductivityAsync(request);
    }

    public override async Task GetPauseEvents(GetPauseEventsRequest request, IServerStreamWriter<PauseEventRecord> responseStream, ServerCallContext context)
    {
        await _dashboardService.GetPauseEventsAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task<PauseStatistics> GetPauseStatistics(GetPauseStatisticsRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetPauseStatisticsAsync(request);
    }

    public override async Task<ForceResumeResponse> ForceResumeAgent(ForceResumeRequest request, ServerCallContext context)
    {
        return await _dashboardService.ForceResumeAgentAsync(request);
    }

    public override async Task<SendPauseCommandResponse> SendPauseCommand(SendPauseCommandRequest request, ServerCallContext context)
    {
        return await _dashboardService.SendPauseCommandAsync(request);
    }

    public override async Task GetDlpEvents(GetDlpEventsRequest request, IServerStreamWriter<DlpEventRecord> responseStream, ServerCallContext context)
    {
        await _dashboardService.GetDlpEventsAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task<DlpStatistics> GetDlpStatistics(GetDlpStatisticsRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetDlpStatisticsAsync(request);
    }

    public override async Task<AcknowledgeDlpEventResponse> AcknowledgeDlpEvent(AcknowledgeDlpEventRequest request, ServerCallContext context)
    {
        return await _dashboardService.AcknowledgeDlpEventAsync(request);
    }

    public override async Task<AgentConfiguration> GetAgentConfiguration(GetAgentConfigRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetAgentConfigurationAsync(request);
    }

    public override async Task<UpdateAgentConfigResponse> UpdateAgentConfiguration(UpdateAgentConfigRequest request, ServerCallContext context)
    {
        return await _dashboardService.UpdateAgentConfigurationAsync(request);
    }

    public override async Task<GlobalConfiguration> GetGlobalConfiguration(GlobalConfigRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetGlobalConfigurationAsync(request);
    }

    public override async Task<UpdateGlobalConfigResponse> UpdateGlobalConfiguration(UpdateGlobalConfigRequest request, ServerCallContext context)
    {
        return await _dashboardService.UpdateGlobalConfigurationAsync(request);
    }

    public override async Task<DeployConfigResponse> DeployConfiguration(DeployConfigRequest request, ServerCallContext context)
    {
        return await _dashboardService.DeployConfigurationAsync(request);
    }

    public override async Task<UserList> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
        return await _dashboardService.ListUsersAsync(request);
    }

    public override async Task<UserProfile> GetUser(GetUserRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetUserAsync(request);
    }

    public override async Task<UpdateUserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        return await _dashboardService.UpdateUserAsync(request);
    }

    public override async Task<DeviceList> ListDevices(ListDevicesRequest request, ServerCallContext context)
    {
        return await _dashboardService.ListDevicesAsync(request);
    }

    public override async Task<DeviceDetail> GetDevice(GetDeviceRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetDeviceAsync(request);
    }

    public override async Task<DecommissionResponse> DecommissionDevice(DecommissionDeviceRequest request, ServerCallContext context)
    {
        return await _dashboardService.DecommissionDeviceAsync(request);
    }

    public override async Task GetConsentStatuses(GetConsentStatusesRequest request, IServerStreamWriter<ConsentStatusRecord> responseStream, ServerCallContext context)
    {
        await _dashboardService.GetConsentStatusesAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task<RequestConsentResponse> RequestConsent(RequestConsentRequest request, ServerCallContext context)
    {
        return await _dashboardService.RequestConsentAsync(request);
    }

    public override async Task GetAuditLog(GetAuditLogRequest request, IServerStreamWriter<AuditLogEntry> responseStream, ServerCallContext context)
    {
        await _dashboardService.GetAuditLogAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task<ExportAuditLogResponse> ExportAuditLog(ExportAuditLogRequest request, ServerCallContext context)
    {
        return await _dashboardService.ExportAuditLogAsync(request);
    }

    public override async Task<ComplianceReport> GetComplianceReport(GetComplianceReportRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetComplianceReportAsync(request);
    }

    public override async Task GetAlerts(GetAlertsRequest request, IServerStreamWriter<AlertRecord> responseStream, ServerCallContext context)
    {
        await _dashboardService.GetAlertsAsync(request, responseStream, context.CancellationToken);
    }

    public override async Task<AcknowledgeAlertResponse> AcknowledgeAlert(AcknowledgeAlertRequest request, ServerCallContext context)
    {
        return await _dashboardService.AcknowledgeAlertAsync(request);
    }

    public override async Task<AlertRule> CreateAlertRule(CreateAlertRuleRequest request, ServerCallContext context)
    {
        return await _dashboardService.CreateAlertRuleAsync(request);
    }

    public override async Task<AlertRule> UpdateAlertRule(UpdateAlertRuleRequest request, ServerCallContext context)
    {
        return await _dashboardService.UpdateAlertRuleAsync(request);
    }

    public override async Task<DeleteAlertRuleResponse> DeleteAlertRule(DeleteAlertRuleRequest request, ServerCallContext context)
    {
        return await _dashboardService.DeleteAlertRuleAsync(request);
    }

    public override async Task<AlertRuleList> ListAlertRules(ListAlertRulesRequest request, ServerCallContext context)
    {
        return await _dashboardService.ListAlertRulesAsync(request);
    }

    public override async Task<ReportJob> GenerateReport(GenerateReportRequest request, ServerCallContext context)
    {
        return await _dashboardService.GenerateReportAsync(request);
    }

    public override async Task<ReportJob> GetReportStatus(GetReportStatusRequest request, ServerCallContext context)
    {
        return await _dashboardService.GetReportStatusAsync(request);
    }

    public override async Task DownloadReport(DownloadReportRequest request, IServerStreamWriter<ReportChunk> responseStream, ServerCallContext context)
    {
        await _dashboardService.DownloadReportAsync(request, responseStream, context.CancellationToken);
    }

    private string GetAdminId(ServerCallContext context)
    {
        return context.GetHttpContext()?.User?.FindFirst("sub")?.Value ?? "unknown";
    }
}