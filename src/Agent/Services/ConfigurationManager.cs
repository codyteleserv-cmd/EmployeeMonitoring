using EmployeeMonitoring.Agent.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Manages agent configuration with hot-reload and validation.
/// </summary>
public class ConfigurationManager : IConfigurationManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConfigurationManager> _logger;
    private readonly Dictionary<Type, object> _configurationCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    public ConfigurationManager(
        IServiceProvider serviceProvider,
        ILogger<ConfigurationManager> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public T GetConfiguration<T>() where T : class, new()
    {
        return GetConfigurationAsync<T>().GetAwaiter().GetResult();
    }

    public async Task<T> GetConfigurationAsync<T>(CancellationToken cancellationToken = default) where T : class, new()
    {
        var type = typeof(T);
        
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_configurationCache.TryGetValue(type, out var cached))
            {
                return (T)cached;
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        // Load from options monitor
        var optionsMonitor = _serviceProvider.GetService(typeof(IOptionsMonitor<>).MakeGenericType(type));
        if (optionsMonitor == null)
        {
            _logger.LogWarning("No IOptionsMonitor registered for {Type}", type.Name);
            return new T();
        }

        var currentValueProperty = optionsMonitor.GetType().GetProperty("CurrentValue");
        var value = currentValueProperty?.GetValue(optionsMonitor) as T ?? new T();

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            _configurationCache[type] = value;
        }
        finally
        {
            _cacheLock.Release();
        }

        return value;
    }

    public async Task UpdateConfigurationAsync<T>(T configuration, string updatedBy, CancellationToken cancellationToken = default) where T : class
    {
        var type = typeof(T);
        object? oldConfig = null;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_configurationCache.TryGetValue(type, out var cached))
            {
                oldConfig = DeepClone(cached);
            }
            _configurationCache[type] = configuration;
        }
        finally
        {
            _cacheLock.Release();
        }

        // Validate
        var isValid = await ValidateConfigurationAsync(configuration, cancellationToken);
        if (!isValid)
        {
            // Rollback
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                if (oldConfig != null)
                {
                    _configurationCache[type] = oldConfig;
                }
            }
            finally
            {
                _cacheLock.Release();
            }
            throw new InvalidOperationException($"Configuration validation failed for {type.Name}");
        }

        // In production, this would persist to disk and notify server
        _logger.LogInformation("Configuration updated: {Type} by {UpdatedBy}", type.Name, updatedBy);

        // Fire event
        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs
        {
            ConfigurationType = type.Name,
            OldConfiguration = oldConfig,
            NewConfiguration = configuration,
            ChangedBy = updatedBy,
            ChangedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task<bool> ValidateConfigurationAsync<T>(T configuration, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var type = typeof(T);
            
            // Type-specific validation
            return type.Name switch
            {
                nameof(ScreenshotConfiguration) => ValidateScreenshotConfig(configuration as ScreenshotConfiguration),
                nameof(ActivityConfiguration) => ValidateActivityConfig(configuration as ActivityConfiguration),
                nameof(DlpConfiguration) => ValidateDlpConfig(configuration as DlpConfiguration),
                nameof(PrivacyConfiguration) => ValidatePrivacyConfig(configuration as PrivacyConfiguration),
                nameof(WorkScheduleConfiguration) => ValidateWorkScheduleConfig(configuration as WorkScheduleConfiguration),
                _ => true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration validation failed for {Type}", typeof(T).Name);
            return false;
        }
    }

    private bool ValidateScreenshotConfig(ScreenshotConfiguration? config)
    {
        if (config == null) return false;
        return config.IntervalSeconds >= 30 && // Minimum 30 seconds
               config.Quality is >= 10 and <= 100 &&
               config.MaxWidth > 0 && config.MaxHeight > 0 &&
               config.MaxBatchSize > 0;
    }

    private bool ValidateActivityConfig(ActivityConfiguration? config)
    {
        if (config == null) return false;
        return config.SampleIntervalSeconds >= 10 && // Minimum 10 seconds
               config.Categories.All(c => !string.IsNullOrEmpty(c.Name) && c.Weight > 0);
    }

    private bool ValidateDlpConfig(DlpConfiguration? config)
    {
        if (config == null) return false;
        return config.PiiPatterns.All(p => !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.Regex));
    }

    private bool ValidatePrivacyConfig(PrivacyConfiguration? config)
    {
        if (config == null) return false;
        return config.DataRetentionDays >= 1 &&
               config.MaxPauseMinutesPerDay >= 0;
    }

    private bool ValidateWorkScheduleConfig(WorkScheduleConfiguration? config)
    {
        if (config == null) return false;
        
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(config.Timezone);
        }
        catch
        {
            return false;
        }

        return config.Days.Count == 7 &&
               config.Days.All(d => d.DayOfWeek is >= 0 and <= 6 &&
                   TimeSpan.TryParse(d.StartTime, out _) &&
                   TimeSpan.TryParse(d.EndTime, out _));
    }

    private static object? DeepClone(object obj)
    {
        if (obj == null) return null;
        var json = JsonSerializer.Serialize(obj, obj.GetType());
        return JsonSerializer.Deserialize(json, obj.GetType());
    }
}