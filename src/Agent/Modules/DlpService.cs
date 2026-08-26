using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Agent.Modules;
using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Contracts;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EmployeeMonitoring.Agent.Modules;

/// <summary>
/// DLP monitoring service - file audit, clipboard PII detection, CRM export monitoring.
/// </summary>
public class DlpService : IDlpService, IDisposable
{
    private readonly IOptionsMonitor<DlpConfiguration> _config;
    private readonly IPauseManager _pauseManager;
    private readonly IConsentManager _consentManager;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<DlpService> _logger;
    
    private Timer? _scanTimer;
    private FileSystemWatcher? _fileWatcher;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _recentEvents = new();
    private bool _disposed;

    public event EventHandler<DlpEventDetectedEventArgs>? DlpEventDetected;
    public event EventHandler<DlpErrorEventArgs>? DlpError;
    public bool IsRunning { get; private set; }

    public DlpService(
        IOptionsMonitor<DlpConfiguration> config,
        IPauseManager pauseManager,
        IConsentManager consentManager,
        IAuditLogger auditLogger,
        ILogger<DlpService> logger)
    {
        _config = config;
        _pauseManager = pauseManager;
        _consentManager = consentManager;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        
        if (!_consentManager.IsModuleConsented("dlp"))
        {
            _logger.LogWarning("DLP monitoring consent not granted");
            return;
        }

        if (!_config.CurrentValue.Enabled)
        {
            _logger.LogInformation("DLP monitoring disabled in configuration");
            return;
        }

        // Start file system monitoring
        if (_config.CurrentValue.FileAuditEnabled)
        {
            StartFileWatcher();
        }

        // Start clipboard monitoring
        if (_config.CurrentValue.ClipboardPiiEnabled)
        {
            _scanTimer = new Timer(
                async _ => await ScanClipboardAsync(cancellationToken),
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30));
        }

        // Start periodic scan for CRM exports
        if (_config.CurrentValue.CrmExportMonitoring)
        {
            var crmTimer = new Timer(
                async _ => await ScanCrmExportsAsync(cancellationToken),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5));
        }

        IsRunning = true;
        _logger.LogInformation("DLP service started");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        _scanTimer?.Dispose();
        _scanTimer = null;
        IsRunning = false;
        _logger.LogInformation("DLP service stopped");
    }

    public async Task<List<DlpEvent>> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        var events = new List<DlpEvent>();
        
        if (_config.CurrentValue.ClipboardPiiEnabled)
        {
            events.AddRange(await ScanClipboardAsync(cancellationToken));
        }
        
        if (_config.CurrentValue.CrmExportMonitoring)
        {
            events.AddRange(await ScanCrmExportsAsync(cancellationToken));
        }

        return events;
    }

    private void StartFileWatcher()
    {
        var config = _config.CurrentValue;
        
        foreach (var path in config.MonitoredPaths)
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(path);
            if (!Directory.Exists(expandedPath)) continue;

            try
            {
                var watcher = new FileSystemWatcher(expandedPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileCreated;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                
                _logger.LogInformation("File watcher started for: {Path}", expandedPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file watcher for {Path}", expandedPath);
            }
        }
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        await HandleFileEventAsync(e.FullPath, DlpEventType.FileAccess, "File created");
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        await HandleFileEventAsync(e.FullPath, DlpEventType.FileAccess, "File modified");
    }

    private async void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        await HandleFileEventAsync(e.FullPath, DlpEventType.FileAccess, "File deleted");
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        await HandleFileEventAsync(e.FullPath, DlpEventType.FileAccess, $"File renamed from {e.OldFullPath}");
    }

    private async Task HandleFileEventAsync(string filePath, DlpEventType eventType, string action)
    {
        if (!ShouldMonitor()) return;

        try
        {
            // Check if file matches blocked extensions
            var config = _config.CurrentValue;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            var isBlockedExtension = config.BlockedExtensions.Any(ext => 
                extension.Equals(ext, StringComparison.OrdinalIgnoreCase));

            // Check for PII in file content (for text files)
            var piiFound = await ScanFileForPiiAsync(filePath);
            
            var severity = isBlockedExtension || piiFound ? Severity.High : Severity.Info;
            
            var dlpEvent = new DlpEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Type = eventType,
                Severity = severity,
                ProcessName = GetCallingProcessName(),
                FilePath = filePath,
                Details = $"{action}: {filePath}" + (piiFound ? " [PII detected]" : "") + (isBlockedExtension ? " [Blocked extension]" : ""),
                Metadata = new Dictionary<string, string>
                {
                    ["extension"] = extension,
                    ["pii_detected"] = piiFound.ToString(),
                    ["blocked_extension"] = isBlockedExtension.ToString()
                },
                Blocked = isBlockedExtension,
                UserSid = _config.CurrentValue.GetType().GetProperty("UserSid")?.GetValue(_config.CurrentValue) as string ?? string.Empty
            };

            await EmitDlpEventAsync(dlpEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle file event for {FilePath}", filePath);
        }
    }

    private async Task<List<DlpEvent>> ScanClipboardAsync(CancellationToken cancellationToken)
    {
        var events = new List<DlpEvent>();
        
        if (!ShouldMonitor()) return events;

        try
        {
            // Get clipboard content (requires STA thread)
            var clipboardText = await Task.Run(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        return Clipboard.GetText(TextDataFormat.UnicodeText);
                    }
                }
                catch { }
                return string.Empty;
            }, cancellationToken);

            if (string.IsNullOrEmpty(clipboardText)) return events;

            // Check for PII patterns
            var config = _config.CurrentValue;
            var piiMatches = new List<string>();

            foreach (var pattern in config.PiiPatterns)
            {
                try
                {
                    var regex = new Regex(pattern.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    var matches = regex.Matches(clipboardText);
                    if (matches.Count > 0)
                    {
                        piiMatches.Add($"{pattern.Name}: {matches.Count} matches");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "PII pattern {Name} failed", pattern.Name);
                }
            }

            if (piiMatches.Count > 0)
            {
                var dlpEvent = new DlpEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = DlpEventType.ClipboardPii,
                    Severity = Severity.High,
                    ProcessName = GetCallingProcessName(),
                    Details = $"Clipboard contains PII: {string.Join(", ", piiMatches)}",
                    Metadata = new Dictionary<string, string>
                    {
                        ["pii_types"] = string.Join(",", piiMatches),
                        ["clipboard_length"] = clipboardText.Length.ToString()
                    },
                    Blocked = false,
                    UserSid = string.Empty
                };

                events.Add(dlpEvent);
                await EmitDlpEventAsync(dlpEvent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Clipboard scan failed");
        }

        return events;
    }

    private async Task<List<DlpEvent>> ScanCrmExportsAsync(CancellationToken cancellationToken)
    {
        var events = new List<DlpEvent>();
        
        if (!ShouldMonitor()) return events;

        try
        {
            var config = _config.CurrentValue;
            
            // Check monitored paths for new export files
            foreach (var path in config.MonitoredPaths)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(path);
                if (!Directory.Exists(expandedPath)) continue;

                var exportFiles = Directory.GetFiles(expandedPath, "*.csv")
                    .Concat(Directory.GetFiles(expandedPath, "*.xlsx"))
                    .Concat(Directory.GetFiles(expandedPath, "*.json"))
                    .Where(f => IsRecentExport(f))
                    .ToList();

                foreach (var file in exportFiles)
                {
                    var fileHash = await ComputeFileHashAsync(file);
                    var eventKey = $"crm_export_{fileHash}";
                    
                    // Deduplicate
                    if (_recentEvents.ContainsKey(eventKey)) continue;
                    _recentEvents[eventKey] = DateTimeOffset.UtcNow;

                    // Clean old entries
                    var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
                    var keysToRemove = _recentEvents.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
                    foreach (var key in keysToRemove) _recentEvents.Remove(key);

                    var dlpEvent = new DlpEvent
                    {
                        EventId = Guid.NewGuid().ToString(),
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Type = DlpEventType.CrmBulkExport,
                        Severity = Severity.High,
                        ProcessName = GetCallingProcessName(),
                        FilePath = file,
                        Details = $"Potential CRM export detected: {Path.GetFileName(file)}",
                        Metadata = new Dictionary<string, string>
                        {
                            ["file_size"] = new FileInfo(file).Length.ToString(),
                            ["file_hash"] = fileHash,
                            ["directory"] = expandedPath
                        },
                        Blocked = false,
                        UserSid = string.Empty
                    };

                    events.Add(dlpEvent);
                    await EmitDlpEventAsync(dlpEvent);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRM export scan failed");
        }

        return events;
    }

    private bool IsRecentExport(string filePath)
    {
        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(filePath);
            return DateTimeOffset.UtcNow - lastWrite < TimeSpan.FromMinutes(10);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash)[..16];
    }

    private async Task<bool> ScanFileForPiiAsync(string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var textExtensions = new[] { ".txt", ".csv", ".json", ".xml", ".log", ".md" };
            
            if (!textExtensions.Contains(extension)) return false;

            var content = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrEmpty(content)) return false;

            var config = _config.CurrentValue;
            foreach (var pattern in config.PiiPatterns)
            {
                try
                {
                    var regex = new Regex(pattern.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    if (regex.IsMatch(content))
                    {
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    private bool ShouldMonitor()
    {
        return IsRunning && _consentManager.IsModuleConsented("dlp") && !_pauseManager.GetPauseState().IsPaused;
    }

    private string GetCallingProcessName()
    {
        try
        {
            // In a real implementation, this would use ETW or similar to get the actual process
            // For now, return current process
            return Process.GetCurrentProcess().ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task EmitDlpEventAsync(DlpEvent dlpEvent)
    {
        try
        {
            // Log audit
            await _auditLogger.LogDlpEventAsync(
                _config.CurrentValue.GetType().GetProperty("AgentId")?.GetValue(_config.CurrentValue) as string ?? string.Empty,
                _config.CurrentValue.GetType().GetProperty("UserDisplayName")?.GetValue(_config.CurrentValue) as string ?? string.Empty,
                dlpEvent.Type.ToString(),
                dlpEvent.Severity.ToString(),
                dlpEvent.Details,
                dlpEvent.Blocked,
                CancellationToken.None);

            // Emit event
            DlpEventDetected?.Invoke(this, new DlpEventDetectedEventArgs { Event = dlpEvent });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit DLP event");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _fileWatcher?.Dispose();
        _scanTimer?.Dispose();
        _scanLock.Dispose();
        _disposed = true;
    }
}