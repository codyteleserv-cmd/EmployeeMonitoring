using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeMonitoring.Api.Services;

/// <summary>
/// Repository for audit log operations (write-only, immutable).
/// </summary>
public interface IAuditRepository
{
    Task<AuditLog> CreateAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
    Task<List<AuditLog>> GetAsync(
        IEnumerable<string>? actorIds = null,
        IEnumerable<string>? actions = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        IEnumerable<string>? targetTypes = null,
        int limit = 1000,
        CancellationToken cancellationToken = default);
    Task<long> GetCountAsync(
        IEnumerable<string>? actorIds = null,
        IEnumerable<string>? actions = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken cancellationToken = default);
}

public class AuditRepository : IAuditRepository
{
    private readonly AuditDbContext _db;

    public AuditRepository(AuditDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLog> CreateAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(cancellationToken);
        return auditLog;
    }

    public async Task<List<AuditLog>> GetAsync(
        IEnumerable<string>? actorIds = null,
        IEnumerable<string>? actions = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        IEnumerable<string>? targetTypes = null,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (actorIds != null && actorIds.Any())
            query = query.Where(a => actorIds.Contains(a.ActorId));

        if (actions != null && actions.Any())
            query = query.Where(a => actions.Contains(a.Action));

        if (startTime.HasValue)
            query = query.Where(a => a.Timestamp >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(a => a.Timestamp <= endTime.Value);

        if (targetTypes != null && targetTypes.Any())
            query = query.Where(a => targetTypes.Contains(a.TargetType));

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetCountAsync(
        IEnumerable<string>? actorIds = null,
        IEnumerable<string>? actions = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (actorIds != null && actorIds.Any())
            query = query.Where(a => actorIds.Contains(a.ActorId));

        if (actions != null && actions.Any())
            query = query.Where(a => actions.Contains(a.Action));

        if (startTime.HasValue)
            query = query.Where(a => a.Timestamp >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(a => a.Timestamp <= endTime.Value);

        return await query.LongCountAsync(cancellationToken);
    }
}