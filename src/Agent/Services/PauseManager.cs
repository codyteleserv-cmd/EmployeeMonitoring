using EmployeeMonitoring.Agent.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Manages pause/resume state with admin notifications and daily limits.
/// </summary>
public class PauseManager : IPauseManager
{
    private readonly IOptionsMonitor<PrivacyConfiguration> _privacyConfig;
    private readonly IAgentIdentityProvider _identityProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IMessageDispatcher _messageDispatcher;
    private readonly ILogger<PauseManager> _logger;
    
    private PauseState _currentState = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly string _pauseStateFilePath;
    private Timer? _pauseTimer;
    private Timer? _dailyResetTimer;

    public event EventHandler<PauseStateChangedEventArgs>? PauseStateChanged;

    public PauseManager(
        IOptionsMonitor<PrivacyConfiguration> privacyConfig,
        IAgentIdentityProvider identityProvider,
        IAuditLogger auditLogger,
        IMessageDispatcher messageDispatcher,
        ILogger<PauseManager> logger)
    {
        _privacyConfig = privacyConfig;
        _identityProvider = identityProvider;
        _auditLogger = auditLogger;
        _messageDispatcher = messageDispatcher;
        _logger = logger;
        
        _pauseStateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EmployeeMonitoring",
            "pause_state.json");
        
        Directory.CreateDirectory(Path.GetDirectoryName(_pauseStateFilePath)!);
        
        _currentState.MaxPausePerDay = TimeSpan.FromMinutes(_privacyConfig.CurrentValue.MaxPauseMinutesPerDay);
        _currentState.MaxPauseResetTime = GetNextMidnight();
    }

    public PauseState GetPauseState()
    {
        lock (_stateLock)
        {
            UpdateCurrentPauseDuration();
            return _currentState with { }; // Return copy
        }
    }

    public async Task<PauseResult> RequestPauseAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (!_privacyConfig.CurrentValue.AllowUserPause)
        {
            return new PauseResult
            {
                Success = false,
                Message = "Pausing is not allowed by policy"
            };
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_currentState.IsPaused)
            {
                return new PauseResult
                {
                    Success = false,
                    Message = "Already paused",
                    NewState = _currentState with { }
                };
            }

            // Check daily limit
            if (!await CheckDailyLimitAsync(cancellationToken))
            {
                return new PauseResult
                {
                    Success = false,
                    Message = $"Daily pause limit of {_currentState.MaxPausePerDay.TotalMinutes} minutes exceeded"
                };
            }

            var oldState = _currentState with { };
            var now = DateTimeOffset.UtcNow;
            
            _currentState = _currentState with
            {
                IsPaused = true,
                PausedAt = now,
                PauseReason = reason,
                PausedBy = _identityProvider.UserSid,
                CurrentPauseDuration = TimeSpan.Zero,
                AdminNotified = false
            };

            // Start pause duration timer
            StartPauseTimer();

            // Persist state
            await PersistStateAsync(cancellationToken);

            // Notify admin if configured
            if (_privacyConfig.CurrentValue.NotifyAdminOnPause)
            {
                await NotifyAdminOfPauseAsync(reason, cancellationToken);
            }

            // Log audit
            await _auditLogger.LogPauseEventAsync(
                _identityProvider.AgentId,
                _identityProvider.UserDisplayName,
                "PAUSED",
                reason,
                0,
                _currentState.AdminNotified,
                cancellationToken);

            // Fire event
            PauseStateChanged?.Invoke(this, new PauseStateChangedEventArgs
            {
                OldState = oldState,
                NewState = _currentState with { },
                TriggeredBy = _identityProvider.UserSid,
                ChangedAt = now
            });

            _logger.LogInformation("Monitoring paused: {Reason}", reason);

            return new PauseResult
            {
                Success = true,
                Message = "Monitoring paused",
                NewState = _currentState with { }
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<PauseResult> RequestResumeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_currentState.IsPaused)
            {
                return new PauseResult
                {
                    Success = false,
                    Message = "Not currently paused",
                    NewState = _currentState with { }
                };
            }

            var oldState = _currentState with { };
            var now = DateTimeOffset.UtcNow;
            var pauseDuration = now - _currentState.PausedAt!.Value;
            
            _currentState = _currentState with
            {
                IsPaused = false,
                PausedAt = null,
                PauseReason = null,
                PausedBy = null,
                TotalPauseDuration = _currentState.TotalPauseDuration.Add(pauseDuration),
                CurrentPauseDuration = TimeSpan.Zero,
                AdminNotified = false,
                AdminNotificationId = null
            };

            StopPauseTimer();
            await PersistStateAsync(cancellationToken);

            // Log audit
            await _auditLogger.LogPauseEventAsync(
                _identityProvider.AgentId,
                _identityProvider.UserDisplayName,
                "RESUMED",
                _currentState.PauseReason ?? string.Empty,
                (int)pauseDuration.TotalSeconds,
                _currentState.AdminNotified,
                cancellationToken);

            PauseStateChanged?.Invoke(this, new PauseStateChangedEventArgs
            {
                OldState = oldState,
                NewState = _currentState with { },
                TriggeredBy = _identityProvider.UserSid,
                ChangedAt = now
            });

            _logger.LogInformation("Monitoring resumed after {Duration}", pauseDuration);

            return new PauseResult
            {
                Success = true,
                Message = "Monitoring resumed",
                NewState = _currentState with { }
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<PauseResult> ForceResumeAsync(string adminUserId, string reason, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_currentState.IsPaused)
            {
                return new PauseResult
                {
                    Success = false,
                    Message = "Not currently paused",
                    NewState = _currentState with { }
                };
            }

            var oldState = _currentState with { };
            var now = DateTimeOffset.UtcNow;
            var pauseDuration = now - _currentState.PausedAt!.Value;
            
            _currentState = _currentState with
            {
                IsPaused = false,
                PausedAt = null,
                PauseReason = $"Force resumed by admin: {reason}",
                PausedBy = adminUserId,
                TotalPauseDuration = _currentState.TotalPauseDuration.Add(pauseDuration),
                CurrentPauseDuration = TimeSpan.Zero,
                AdminNotified = true
            };

            StopPauseTimer();
            await PersistStateAsync(cancellationToken);

            // Log audit
            await _auditLogger.LogPauseEventAsync(
                _identityProvider.AgentId,
                _identityProvider.UserDisplayName,
                "FORCE_RESUMED",
                $"Admin {adminUserId}: {reason}",
                (int)pauseDuration.TotalSeconds,
                true,
                cancellationToken);

            // Also log as admin action
            await _auditLogger.LogAdminActionAsync(
                adminUserId,
                "FORCE_RESUME",
                "agent",
                _identityProvider.AgentId,
                $"Force resumed agent. Pause duration: {pauseDuration}",
                true,
                cancellationToken);

            PauseStateChanged?.Invoke(this, new PauseStateChangedEventArgs
            {
                OldState = oldState,
                NewState = _currentState with { },
                TriggeredBy = adminUserId,
                ChangedAt = now
            });

            return new PauseResult
            {
                Success = true,
                Message = "Agent force resumed by admin",
                NewState = _currentState with { }
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<PauseResult> SetMaxPauseAsync(TimeSpan maxPause, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var oldState = _currentState with { };
            _currentState = _currentState with
            {
                MaxPausePerDay = maxPause
            };
            
            await PersistStateAsync(cancellationToken);
            
            return new PauseResult
            {
                Success = true,
                Message = $"Max pause per day set to {maxPause.TotalMinutes} minutes",
                NewState = _currentState with { }
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public bool CanPause()
    {
        lock (_stateLock)
        {
            if (!_privacyConfig.CurrentValue.AllowUserPause) return false;
            if (_currentState.IsPaused) return false;
            
            // Check if daily limit would be exceeded
            var remaining = GetRemainingPauseTime();
            return remaining > TimeSpan.Zero;
        }
    }

    public TimeSpan GetRemainingPauseTime()
    {
        lock (_stateLock)
        {
            UpdateCurrentPauseDuration();
            var remaining = _currentState.MaxPausePerDay - _currentState.TotalPauseDuration - _currentState.CurrentPauseDuration;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public DateTimeOffset? GetPauseStartTime()
    {
        lock (_stateLock)
        {
            return _currentState.PausedAt;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadStateAsync(cancellationToken);
        StartDailyResetTimer();
    }

    private void UpdateCurrentPauseDuration()
    {
        if (_currentState.IsPaused && _currentState.PausedAt.HasValue)
        {
            _currentState = _currentState with
            {
                CurrentPauseDuration = DateTimeOffset.UtcNow - _currentState.PausedAt.Value
            };
        }
    }

    private async Task<bool> CheckDailyLimitAsync(CancellationToken cancellationToken)
    {
        UpdateCurrentPauseDuration();
        var totalUsed = _currentState.TotalPauseDuration + _currentState.CurrentPauseDuration;
        return totalUsed < _currentState.MaxPausePerDay;
    }

    private void StartPauseTimer()
    {
        _pauseTimer?.Dispose();
        _pauseTimer = new Timer(
            async _ => await CheckMaxPauseExceededAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    private void StopPauseTimer()
    {
        _pauseTimer?.Dispose();
        _pauseTimer = null;
    }

    private async Task CheckMaxPauseExceededAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!_currentState.IsPaused) return;
            
            UpdateCurrentPauseDuration();
            var totalUsed = _currentState.TotalPauseDuration + _currentState.CurrentPauseDuration;
            
            if (totalUsed >= _currentState.MaxPausePerDay)
            {
                _logger.LogWarning("Max pause time exceeded, forcing resume");
                
                var oldState = _currentState with { };
                var now = DateTimeOffset.UtcNow;
                var pauseDuration = now - _currentState.PausedAt!.Value;
                
                _currentState = _currentState with
                {
                    IsPaused = false,
                    PausedAt = null,
                    PauseReason = "Max daily pause time exceeded - auto resumed",
                    PausedBy = "system",
                    TotalPauseDuration = _currentState.TotalPauseDuration.Add(pauseDuration),
                    CurrentPauseDuration = TimeSpan.Zero
                };

                StopPauseTimer();
                await PersistStateAsync(CancellationToken.None);

                await _auditLogger.LogPauseEventAsync(
                    _identityProvider.AgentId,
                    _identityProvider.UserDisplayName,
                    "EXPIRED",
                    "Max daily pause time exceeded",
                    (int)pauseDuration.TotalSeconds,
                    true,
                    CancellationToken.None);

                PauseStateChanged?.Invoke(this, new PauseStateChangedEventArgs
                {
                    OldState = oldState,
                    NewState = _currentState with { },
                    TriggeredBy = "system",
                    ChangedAt = now
                });
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void StartDailyResetTimer()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMidnight = GetNextMidnight();
        var delay = nextMidnight - now;

        _dailyResetTimer?.Dispose();
        _dailyResetTimer = new Timer(
            async _ => await ResetDailyPauseAsync(),
            null,
            delay,
            TimeSpan.FromDays(1));
    }

    private async Task ResetDailyPauseAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _currentState = _currentState with
            {
                TotalPauseDuration = TimeSpan.Zero,
                MaxPauseResetTime = GetNextMidnight().AddDays(1)
            };
            
            await PersistStateAsync(CancellationToken.None);
            StartDailyResetTimer();
            
            _logger.LogInformation("Daily pause counters reset");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private DateTimeOffset GetNextMidnight()
    {
        var now = DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(_privacyConfig.CurrentValue.BlurWindowClasses.FirstOrDefault() ?? "UTC");
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var midnight = localNow.Date.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, tz);
    }

    private async Task NotifyAdminOfPauseAsync(string reason, CancellationToken cancellationToken)
    {
        try
        {
            var notificationId = Guid.NewGuid().ToString();
            _currentState = _currentState with
            {
                AdminNotified = true,
                AdminNotificationId = notificationId
            };

            // Send notification via message dispatcher
            await _messageDispatcher.SendAdminNotificationAsync(new
            {
                Type = "AGENT_PAUSED",
                AgentId = _identityProvider.AgentId,
                UserName = _identityProvider.UserDisplayName,
                Department = _identityProvider.Department,
                Reason = reason,
                Timestamp = DateTimeOffset.UtcNow,
                NotificationId = notificationId
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify admin of pause");
        }
    }

    private async Task LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_pauseStateFilePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(_pauseStateFilePath, cancellationToken);
            var state = JsonSerializer.Deserialize<PauseState>(json);
            
            if (state != null)
            {
                // Check if paused state is from today
                if (state.IsPaused && state.PausedAt.HasValue)
                {
                    var pauseAge = DateTimeOffset.UtcNow - state.PausedAt.Value;
                    if (pauseAge > TimeSpan.FromHours(24))
                    {
                        // Old pause, don't restore
                        state = state with { IsPaused = false, PausedAt = null, PauseReason = null };
                    }
                }

                _currentState = state with
                {
                    MaxPausePerDay = TimeSpan.FromMinutes(_privacyConfig.CurrentValue.MaxPauseMinutesPerDay),
                    MaxPauseResetTime = GetNextMidnight()
                };

                if (_currentState.IsPaused)
                {
                    StartPauseTimer();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pause state");
        }
    }

    private async Task PersistStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(_currentState, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_pauseStateFilePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist pause state");
        }
    }
}