using EmployeeMonitoring.Api.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace EmployeeMonitoring.Api.Services;

/// <summary>
/// Manages agent SignalR connections.
/// </summary>
public interface IAgentConnectionManager
{
    Task RegisterAgentAsync(string agentId, string connectionId, CancellationToken cancellationToken = default);
    Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default);
    string? GetConnectionId(string agentId);
    bool IsConnected(string agentId);
    IReadOnlyDictionary<string, string> GetAllConnections();
    Task SendToAgentAsync(string agentId, string method, object payload, CancellationToken cancellationToken = default);
    Task BroadcastToAgentsAsync(IEnumerable<string> agentIds, string method, object payload, CancellationToken cancellationToken = default);
}

public class AgentConnectionManager : IAgentConnectionManager
{
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ConcurrentDictionary<string, string> _connections = new();
    private readonly ILogger<AgentConnectionManager> _logger;

    public AgentConnectionManager(IHubContext<AgentHub> hubContext, ILogger<AgentConnectionManager> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task RegisterAgentAsync(string agentId, string connectionId, CancellationToken cancellationToken = default)
    {
        _connections.AddOrUpdate(agentId, connectionId, (_, _) => connectionId);
        _logger.LogDebug("Agent {AgentId} registered with connection {ConnectionId}", agentId, connectionId);
        return Task.CompletedTask;
    }

    public Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _connections.TryRemove(agentId, out _);
        _logger.LogDebug("Agent {AgentId} unregistered", agentId);
        return Task.CompletedTask;
    }

    public string? GetConnectionId(string agentId)
    {
        return _connections.TryGetValue(agentId, out var connId) ? connId : null;
    }

    public bool IsConnected(string agentId)
    {
        return _connections.ContainsKey(agentId);
    }

    public IReadOnlyDictionary<string, string> GetAllConnections()
    {
        return _connections;
    }

    public async Task SendToAgentAsync(string agentId, string method, object payload, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(agentId, out var connectionId))
        {
            await _hubContext.Clients.Client(connectionId).SendAsync(method, payload, cancellationToken);
        }
    }

    public async Task BroadcastToAgentsAsync(IEnumerable<string> agentIds, string method, object payload, CancellationToken cancellationToken = default)
    {
        var connectionIds = agentIds
            .Select(id => _connections.TryGetValue(id, out var conn) ? conn : null)
            .Where(id => id != null)
            .Cast<string>()
            .ToList();

        if (connectionIds.Count > 0)
        {
            await _hubContext.Clients.Clients(connectionIds).SendAsync(method, payload, cancellationToken);
        }
    }
}

/// <summary>
/// Manages admin dashboard SignalR connections.
/// </summary>
public interface IAdminConnectionManager
{
    Task RegisterAdminAsync(string adminId, string connectionId, CancellationToken cancellationToken = default);
    Task UnregisterAdminAsync(string adminId, CancellationToken cancellationToken = default);
    bool IsConnected(string adminId);
    Task SendToAdminAsync(string adminId, string method, object payload, CancellationToken cancellationToken = default);
    Task BroadcastToAdminsAsync(string method, object payload, CancellationToken cancellationToken = default);
    Task BroadcastToRoleAsync(string role, string method, object payload, CancellationToken cancellationToken = default);
}

public class AdminConnectionManager : IAdminConnectionManager
{
    private readonly IHubContext<AdminHub> _hubContext;
    private readonly ConcurrentDictionary<string, string> _adminConnections = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _roleConnections = new();
    private readonly ILogger<AdminConnectionManager> _logger;

    public AdminConnectionManager(IHubContext<AdminHub> hubContext, ILogger<AdminConnectionManager> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task RegisterAdminAsync(string adminId, string connectionId, CancellationToken cancellationToken = default)
    {
        _adminConnections.AddOrUpdate(adminId, connectionId, (_, _) => connectionId);
        _logger.LogDebug("Admin {AdminId} registered with connection {ConnectionId}", adminId, connectionId);
        return Task.CompletedTask;
    }

    public Task RegisterAdminWithRoleAsync(string adminId, string connectionId, string role, CancellationToken cancellationToken = default)
    {
        _adminConnections.AddOrUpdate(adminId, connectionId, (_, _) => connectionId);
        
        _roleConnections.AddOrUpdate(role,
            _ => new HashSet<string> { connectionId },
            (_, existing) => { existing.Add(connectionId); return existing; });
        
        return Task.CompletedTask;
    }

    public Task UnregisterAdminAsync(string adminId, CancellationToken cancellationToken = default)
    {
        if (_adminConnections.TryRemove(adminId, out var connectionId))
        {
            // Remove from role connections
            foreach (var roleConns in _roleConnections.Values)
            {
                roleConns.Remove(connectionId);
            }
        }
        _logger.LogDebug("Admin {AdminId} unregistered", adminId);
        return Task.CompletedTask;
    }

    public bool IsConnected(string adminId)
    {
        return _adminConnections.ContainsKey(adminId);
    }

    public async Task SendToAdminAsync(string adminId, string method, object payload, CancellationToken cancellationToken = default)
    {
        if (_adminConnections.TryGetValue(adminId, out var connectionId))
        {
            await _hubContext.Clients.Client(connectionId).SendAsync(method, payload, cancellationToken);
        }
    }

    public async Task BroadcastToAdminsAsync(string method, object payload, CancellationToken cancellationToken = default)
    {
        var connectionIds = _adminConnections.Values.ToList();
        if (connectionIds.Count > 0)
        {
            await _hubContext.Clients.Clients(connectionIds).SendAsync(method, payload, cancellationToken);
        }
    }

    public async Task BroadcastToRoleAsync(string role, string method, object payload, CancellationToken cancellationToken = default)
    {
        if (_roleConnections.TryGetValue(role, out var connectionIds))
        {
            await _hubContext.Clients.Clients(connectionIds).SendAsync(method, payload, cancellationToken);
        }
    }
}