using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeMonitoring.Api.Services;

/// <summary>
/// Repository for screenshot operations.
/// </summary>
public interface IScreenshotRepository
{
    Task<Screenshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Screenshot>> GetByAgentAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<Screenshot> CreateAsync(Screenshot screenshot, CancellationToken cancellationToken = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task<long> GetCountAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default);
}

public class ScreenshotRepository : IScreenshotRepository
{
    private readonly MonitoringDbContext _db;

    public ScreenshotRepository(MonitoringDbContext db)
    {
        _db = db;
    }

    public async Task<Screenshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Screenshots.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<List<Screenshot>> GetByAgentAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = _db.Screenshots.Where(s => s.AgentId == agentId);

        if (startTime.HasValue)
            query = query.Where(s => s.CapturedAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(s => s.CapturedAt <= endTime.Value);

        return await query
            .OrderByDescending(s => s.CapturedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Screenshot> CreateAsync(Screenshot screenshot, CancellationToken cancellationToken = default)
    {
        _db.Screenshots.Add(screenshot);
        await _db.SaveChangesAsync(cancellationToken);
        return screenshot;
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = await _db.Screenshots
            .Where(s => s.CapturedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (toDelete.Count > 0)
        {
            _db.Screenshots.RemoveRange(toDelete);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return toDelete.Count;
    }

    public async Task<long> GetCountAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Screenshots.Where(s => s.AgentId == agentId);

        if (startTime.HasValue)
            query = query.Where(s => s.CapturedAt >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(s => s.CapturedAt <= endTime.Value);

        return await query.LongCountAsync(cancellationToken);
    }
}

/// <summary>
/// Repository for activity sample operations.
/// </summary>
public interface IActivityRepository
{
    Task<List<ActivitySample>> GetByAgentAsync(Guid agentId, DateTimeOffset startTime, DateTimeOffset endTime, int limit = 1000, CancellationToken cancellationToken = default);
    Task<ActivitySample> CreateAsync(ActivitySample sample, CancellationToken cancellationToken = default);
    Task<int> CreateBatchAsync(List<ActivitySample> samples, CancellationToken cancellationToken = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task<ActivitySummaryDto> GetSummaryAsync(Guid agentId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default);
}

public class ActivityRepository : IActivityRepository
{
    private readonly MonitoringDbContext _db;

    public ActivityRepository(MonitoringDbContext db)
    {
        _db = db;
    }

    public async Task<List<ActivitySample>> GetByAgentAsync(Guid agentId, DateTimeOffset startTime, DateTimeOffset endTime, int limit = 1000, CancellationToken cancellationToken = default)
    {
        return await _db.ActivitySamples
            .Where(s => s.AgentId == agentId && s.Timestamp >= startTime && s.Timestamp <= endTime)
            .OrderByDescending(s => s.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<ActivitySample> CreateAsync(ActivitySample sample, CancellationToken cancellationToken = default)
    {
        _db.ActivitySamples.Add(sample);
        await _db.SaveChangesAsync(cancellationToken);
        return sample;
    }

    public async Task<int> CreateBatchAsync(List<ActivitySample> samples, CancellationToken cancellationToken = default)
    {
        _db.ActivitySamples.AddRange(samples);
        return await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = await _db.ActivitySamples
            .Where(s => s.Timestamp < cutoff)
            .ToListAsync(cancellationToken);

        if (toDelete.Count > 0)
        {
            _db.ActivitySamples.RemoveRange(toDelete);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return toDelete.Count;
    }

    public async Task<ActivitySummaryDto> GetSummaryAsync(Guid agentId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
    {
        var samples = await _db.ActivitySamples
            .Where(s => s.AgentId == agentId && s.Timestamp >= startTime && s.Timestamp <= endTime)
            .ToListAsync(cancellationToken);

        var totalSeconds = samples.Sum(s => (long)s.DurationSeconds);
        var productiveSeconds = samples.Where(s => s.Productivity == ProductivityLevel.Productive).Sum(s => (long)s.DurationSeconds);
        var neutralSeconds = samples.Where(s => s.Productivity == ProductivityLevel.Neutral).Sum(s => (long)s.DurationSeconds);
        var distractingSeconds = samples.Where(s => s.Productivity == ProductivityLevel.Distracting).Sum(s => (long)s.DurationSeconds);
        var idleSeconds = samples.Sum(s => (long)s.IdleSeconds);

        var processBreakdown = samples
            .GroupBy(s => s.ProcessName)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationSeconds));

        var categoryBreakdown = samples
            .GroupBy(s => s.Productivity.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationSeconds));

        return new ActivitySummaryDto
        {
            AgentId = agentId,
            StartTime = startTime,
            EndTime = endTime,
            TotalSeconds = totalSeconds,
            ProductiveSeconds = productiveSeconds,
            NeutralSeconds = neutralSeconds,
            DistractingSeconds = distractingSeconds,
            IdleSeconds = idleSeconds,
            ProductivityScore = totalSeconds > 0 ? (double)productiveSeconds / totalSeconds * 100 : 0,
            ProcessBreakdown = processBreakdown,
            CategoryBreakdown = categoryBreakdown
        };
    }
}

public class ActivitySummaryDto
{
    public Guid AgentId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public long TotalSeconds { get; set; }
    public long ProductiveSeconds { get; set; }
    public long NeutralSeconds { get; set; }
    public long DistractingSeconds { get; set; }
    public long IdleSeconds { get; set; }
    public double ProductivityScore { get; set; }
    public Dictionary<string, int> ProcessBreakdown { get; set; } = new();
    public Dictionary<string, int> CategoryBreakdown { get; set; } = new();
}

/// <summary>
/// Repository for DLP event operations.
/// </summary>
public interface IDlpRepository
{
    Task<DlpEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<DlpEvent>> GetByAgentAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, DlpEventType? type = null, Severity? severity = null, bool? acknowledged = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<DlpEvent> CreateAsync(DlpEvent dlpEvent, CancellationToken cancellationToken = default);
    Task<DlpEvent> UpdateAsync(DlpEvent dlpEvent, CancellationToken cancellationToken = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task<DlpStatisticsDto> GetStatisticsAsync(Guid? agentId = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default);
}

public class DlpRepository : IDlpRepository
{
    private readonly MonitoringDbContext _db;

    public DlpRepository(MonitoringDbContext db)
    {
        _db = db;
    }

    public async Task<DlpEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.DlpEvents.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<List<DlpEvent>> GetByAgentAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, DlpEventType? type = null, Severity? severity = null, bool? acknowledged = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = _db.DlpEvents.Where(d => d.AgentId == agentId);

        if (startTime.HasValue)
            query = query.Where(d => d.Timestamp >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(d => d.Timestamp <= endTime.Value);

        if (type.HasValue)
            query = query.Where(d => d.Type == type.Value);

        if (severity.HasValue)
            query = query.Where(d => d.Severity == severity.Value);

        if (acknowledged.HasValue)
            query = query.Where(d => d.Acknowledged == acknowledged.Value);

        return await query
            .OrderByDescending(d => d.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<DlpEvent> CreateAsync(DlpEvent dlpEvent, CancellationToken cancellationToken = default)
    {
        _db.DlpEvents.Add(dlpEvent);
        await _db.SaveChangesAsync(cancellationToken);
        return dlpEvent;
    }

    public async Task<DlpEvent> UpdateAsync(DlpEvent dlpEvent, CancellationToken cancellationToken = default)
    {
        _db.DlpEvents.Update(dlpEvent);
        await _db.SaveChangesAsync(cancellationToken);
        return dlpEvent;
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = await _db.DlpEvents
            .Where(d => d.Timestamp < cutoff)
            .ToListAsync(cancellationToken);

        if (toDelete.Count > 0)
        {
            _db.DlpEvents.RemoveRange(toDelete);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return toDelete.Count;
    }

    public async Task<DlpStatisticsDto> GetStatisticsAsync(Guid? agentId = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
    {
        var query = _db.DlpEvents.AsQueryable();

        if (agentId.HasValue)
            query = query.Where(d => d.AgentId == agentId.Value);

        if (startTime.HasValue)
            query = query.Where(d => d.Timestamp >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(d => d.Timestamp <= endTime.Value);

        var events = await query.ToListAsync(cancellationToken);

        return new DlpStatisticsDto
        {
            TotalEvents = events.Count,
            ByType = events.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count()),
            BySeverity = events.GroupBy(e => e.Severity).ToDictionary(g => g.Key, g => g.Count()),
            BlockedCount = events.Count(e => e.Blocked),
            AcknowledgedCount = events.Count(e => e.Acknowledged),
            TopSources = events
                .GroupBy(e => e.ProcessName)
                .Select(g => new TopDlpSource { Source = g.Key, EventCount = g.Count() })
                .OrderByDescending(s => s.EventCount)
                .Take(10)
                .ToList(),
            TopUsers = events
                .Where(e => e.AgentId != Guid.Empty)
                .GroupBy(e => e.AgentId)
                .Select(g => new TopDlpUser 
                { 
                    AgentId = g.Key, 
                    EventCount = g.Count(),
                    HighSeverityCount = g.Count(e => e.Severity >= Severity.High)
                })
                .OrderByDescending(s => s.EventCount)
                .Take(10)
                .ToList()
        };
    }
}

public class DlpStatisticsDto
{
    public int TotalEvents { get; set; }
    public Dictionary<DlpEventType, int> ByType { get; set; } = new();
    public Dictionary<Severity, int> BySeverity { get; set; } = new();
    public int BlockedCount { get; set; }
    public int AcknowledgedCount { get; set; }
    public List<TopDlpSource> TopSources { get; set; } = new();
    public List<TopDlpUser> TopUsers { get; set; } = new();
}

public class TopDlpSource
{
    public string Source { get; set; } = string.Empty;
    public int EventCount { get; set; }
}

public class TopDlpUser
{
    public Guid AgentId { get; set; }
    public int EventCount { get; set; }
    public int HighSeverityCount { get; set; }
}

/// <summary>
/// Repository for pause event operations.
/// </summary>
public interface IPauseEventRepository
{
    Task<List<PauseEvent>> GetByAgentAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, PauseAction? action = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<PauseEvent> CreateAsync(PauseEvent pauseEvent, CancellationToken cancellationToken = default);
    Task<PauseStatisticsDto> GetStatisticsAsync(IEnumerable<Guid>? agentIds = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default);
}

public class PauseEventRepository : IPauseEventRepository
{
    private readonly MonitoringDbContext _db;

    public PauseEventRepository(MonitoringDbContext db)
    {
        _db = db;
    }

    public async Task<List<PauseEvent>> GetByAgentAsync(Guid agentId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, PauseAction? action = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = _db.PauseEvents.Where(p => p.AgentId == agentId);

        if (startTime.HasValue)
            query = query.Where(p => p.Timestamp >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(p => p.Timestamp <= endTime.Value);

        if (action.HasValue)
            query = query.Where(p => p.Action == action.Value);

        return await query
            .OrderByDescending(p => p.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<PauseEvent> CreateAsync(PauseEvent pauseEvent, CancellationToken cancellationToken = default)
    {
        _db.PauseEvents.Add(pauseEvent);
        await _db.SaveChangesAsync(cancellationToken);
        return pauseEvent;
    }

    public async Task<PauseStatisticsDto> GetStatisticsAsync(IEnumerable<Guid>? agentIds = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
    {
        var query = _db.PauseEvents.AsQueryable();

        if (agentIds != null && agentIds.Any())
            query = query.Where(p => agentIds.Contains(p.AgentId));

        if (startTime.HasValue)
            query = query.Where(p => p.Timestamp >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(p => p.Timestamp <= endTime.Value);

        var events = await query.ToListAsync(cancellationToken);

        var totalDuration = events.Where(e => e.Action == PauseAction.Resumed).Sum(e => (long)e.PauseDurationSeconds);
        var pauseEvents = events.Where(e => e.Action == PauseAction.Paused).ToList();

        return new PauseStatisticsDto
        {
            TotalPauseEvents = pauseEvents.Count,
            TotalPauseDurationSeconds = totalDuration,
            AveragePauseDurationSeconds = pauseEvents.Count > 0 ? totalDuration / (double)pauseEvents.Count : 0,
            MaxPauseDurationSeconds = pauseEvents.Max(e => e.PauseDurationSeconds),
            AgentBreakdown = events
                .Where(e => e.Action == PauseAction.Resumed)
                .GroupBy(e => e.AgentId)
                .Select(g => new PauseByAgent
                {
                    AgentId = g.Key,
                    PauseCount = g.Count(),
                    TotalPauseSeconds = g.Sum(e => e.PauseDurationSeconds),
                    AveragePauseSeconds = g.Average(e => e.PauseDurationSeconds)
                })
                .ToList(),
            ReasonBreakdown = pauseEvents
                .GroupBy(e => e.Reason)
                .Select(g => new PauseByReason { Reason = g.Key, Count = g.Count(), TotalSeconds = g.Sum(e => e.PauseDurationSeconds) })
                .ToList(),
            TimeBreakdown = pauseEvents
                .GroupBy(e => e.Timestamp.Hour)
                .Select(g => new PauseByTime { HourBucket = $"{g.Key:D2}:00", PauseCount = g.Count() })
                .ToList()
        };
    }
}

public class PauseStatisticsDto
{
    public int TotalPauseEvents { get; set; }
    public long TotalPauseDurationSeconds { get; set; }
    public double AveragePauseDurationSeconds { get; set; }
    public int MaxPauseDurationSeconds { get; set; }
    public List<PauseByAgent> AgentBreakdown { get; set; } = new();
    public List<PauseByReason> ReasonBreakdown { get; set; } = new();
    public List<PauseByTime> TimeBreakdown { get; set; } = new();
}

public class PauseByAgent
{
    public Guid AgentId { get; set; }
    public int PauseCount { get; set; }
    public long TotalPauseSeconds { get; set; }
    public double AveragePauseSeconds { get; set; }
}

public class PauseByReason
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
    public long TotalSeconds { get; set; }
}

public class PauseByTime
{
    public string HourBucket { get; set; } = string.Empty;
    public int PauseCount { get; set; }
}