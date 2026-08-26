using EmployeeMonitoring.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace EmployeeMonitoring.Api.Hubs;

/// <summary>
/// SignalR hub for agent real-time communication.
/// </summary>
public class AgentHub : Hub
{
    private readonly IAgentConnectionManager _connectionManager;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(IAgentConnectionManager connectionManager, ILogger<AgentHub> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var agentId = Context.User?.FindFirst("agent_id")?.Value;
        if (!string.IsNullOrEmpty(agentId))
        {
            await _connectionManager.RegisterAgentAsync(agentId, Context.ConnectionId);
            _logger.LogInformation("Agent {AgentId} connected via SignalR: {ConnectionId}", agentId, Context.ConnectionId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var agentId = Context.User?.FindFirst("agent_id")?.Value;
        if (!string.IsNullOrEmpty(agentId))
        {
            await _connectionManager.UnregisterAgentAsync(agentId);
            _logger.LogInformation("Agent {AgentId} disconnected from SignalR: {ConnectionId}", agentId, Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    // Server-to-agent methods (called from admin dashboard)
    public async Task SendConfigUpdate(string agentId, ConfigUpdate update)
    {
        await Clients.Client(_connectionManager.GetConnectionId(agentId)!).SendAsync("ConfigUpdate", update);
    }

    public async Task SendPauseCommand(string agentId, PauseCommand command)
    {
        await Clients.Client(_connectionManager.GetConnectionId(agentId)!).SendAsync("PauseCommand", command);
    }

    public async Task SendConsentRequest(string agentId, ConsentRequest request)
    {
        await Clients.Client(_connectionManager.GetConnectionId(agentId)!).SendAsync("ConsentRequest", request);
    }

    public async Task SendDiagnosticCommand(string agentId, DiagnosticCommand command)
    {
        await Clients.Client(_connectionManager.GetConnectionId(agentId)!).SendAsync("DiagnosticCommand", command);
    }
}

/// <summary>
/// SignalR hub for admin dashboard real-time updates.
/// </summary>
public class AdminHub : Hub
{
    private readonly IAdminConnectionManager _connectionManager;
    private readonly ILogger<AdminHub> _logger;

    public AdminHub(IAdminConnectionManager connectionManager, ILogger<AdminHub> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var adminId = Context.User?.FindFirst("sub")?.Value ?? Context.ConnectionId;
        var role = Context.User?.FindFirst("role")?.Value ?? "admin";
        
        await _connectionManager.RegisterAdminAsync(adminId, Context.ConnectionId);
        await _connectionManager.RegisterAdminWithRoleAsync(adminId, Context.ConnectionId, role);
        
        _logger.LogInformation("Admin {AdminId} ({Role}) connected via SignalR: {ConnectionId}", adminId, role, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var adminId = Context.User?.FindFirst("sub")?.Value ?? Context.ConnectionId;
        await _connectionManager.UnregisterAdminAsync(adminId);
        _logger.LogInformation("Admin {AdminId} disconnected from SignalR: {ConnectionId}", adminId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // Admin subscription methods
    public async Task SubscribeToAgents(WatchAgentsRequest request)
    {
        // Store subscription in connection metadata
        await Groups.AddToGroupAsync(Context.ConnectionId, "agents_watch");
    }

    public async Task SubscribeToAgentDetails(string agentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent_details_{agentId}");
    }

    public async Task UnsubscribeFromAgentDetails(string agentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent_details_{agentId}");
    }
}