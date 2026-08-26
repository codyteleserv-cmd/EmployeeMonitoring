using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeMonitoring.Api.Services;

/// <summary>
/// Repository for agent operations.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Agent?> GetByAgentIdAsync(string agentId, CancellationToken cancellationToken = default);
    Task<List<Agent>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<Agent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default);
    Task<List<Agent>> GetByDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<List<Agent>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<Agent> CreateAsync(Agent agent, CancellationToken cancellationToken = default);
    Task<Agent> UpdateAsync(Agent agent, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> GetOnlineCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPausedCountAsync(CancellationToken cancellationToken = default);
}

public class AgentRepository : IAgentRepository
{
    private readonly MonitoringDbContext _db;
    private readonly ILogger<AgentRepository> _logger;

    public AgentRepository(MonitoringDbContext db, ILogger<AgentRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Agents.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<Agent?> GetByAgentIdAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return await _db.Agents.FirstOrDefaultAsync(a => a.AgentId == agentId, cancellationToken);
    }

    public async Task<List<Agent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Agents
            .Include(a => a.User)
            .Include(a => a.Department)
            .Include(a => a.Team)
            .OrderByDescending(a => a.LastHeartbeat)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Agent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default)
    {
        return await _db.Agents
            .Where(a => a.Status == status)
            .Include(a => a.User)
            .Include(a => a.Department)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Agent>> GetByDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        return await _db.Agents
            .Where(a => a.DepartmentId == departmentId)
            .Include(a => a.User)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Agent>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _db.Agents
            .Where(a => a.TeamId == teamId)
            .Include(a => a.User)
            .ToListAsync(cancellationToken);
    }

    public async Task<Agent> CreateAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(cancellationToken);
        return agent;
    }

    public async Task<Agent> UpdateAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        _db.Agents.Update(agent);
        await _db.SaveChangesAsync(cancellationToken);
        return agent;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await _db.Agents.FindAsync(new object[] { id }, cancellationToken);
        if (agent != null)
        {
            _db.Agents.Remove(agent);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetOnlineCountAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Agents.CountAsync(a => a.Status == AgentStatus.Online, cancellationToken);
    }

    public async Task<int> GetPausedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Agents.CountAsync(a => a.Status == AgentStatus.Paused, cancellationToken);
    }
}

/// <summary>
/// Repository for user operations.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByUserSidAsync(string userSid, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<List<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<User>> GetByDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<List<User>> GetByRoleAsync(string role, CancellationToken cancellationToken = default);
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);
    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
}

public class UserRepository : IUserRepository
{
    private readonly MonitoringDbContext _db;

    public UserRepository(MonitoringDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Users.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<User?> GetByUserSidAsync(string userSid, CancellationToken cancellationToken = default)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.UserSid == userSid, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<List<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Users
            .Include(u => u.Department)
            .Include(u => u.Team)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<User>> GetByDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        return await _db.Users
            .Where(u => u.DepartmentId == departmentId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<User>> GetByRoleAsync(string role, CancellationToken cancellationToken = default)
    {
        return await _db.Users
            .Where(u => u.Role == role)
            .ToListAsync(cancellationToken);
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(cancellationToken);
        return user;
    }
}