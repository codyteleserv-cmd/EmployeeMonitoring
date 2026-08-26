using EmployeeMonitoring.Agent.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Manages user consent for monitoring activities.
/// </summary>
public class ConsentManager : IConsentManager
{
    private readonly IOptionsMonitor<ConsentConfiguration> _consentConfig;
    private readonly IOptionsMonitor<PrivacyConfiguration> _privacyConfig;
    private readonly IAgentIdentityProvider _identityProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<ConsentManager> _logger;
    private readonly string _consentFilePath;
    
    private ConsentStatus _currentStatus = new();
    private readonly SemaphoreSlim _statusLock = new(1, 1);

    public event EventHandler<ConsentChangedEventArgs>? ConsentChanged;

    public ConsentManager(
        IOptionsMonitor<ConsentConfiguration> consentConfig,
        IOptionsMonitor<PrivacyConfiguration> privacyConfig,
        IAgentIdentityProvider identityProvider,
        IAuditLogger auditLogger,
        ILogger<ConsentManager> logger)
    {
        _consentConfig = consentConfig;
        _privacyConfig = privacyConfig;
        _identityProvider = identityProvider;
        _auditLogger = auditLogger;
        _logger = logger;
        
        _consentFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EmployeeMonitoring",
            "consent.json");
        
        Directory.CreateDirectory(Path.GetDirectoryName(_consentFilePath)!);
    }

    public ConsentStatus GetConsentStatus()
    {
        return _currentStatus;
    }

    public async Task<bool> RequestConsentAsync(CancellationToken cancellationToken = default)
    {
        // In production, this would show a UI dialog
        // For now, we'll simulate consent being granted
        _logger.LogInformation("Consent requested for agent {AgentId}", _identityProvider.AgentId);
        
        var record = new ConsentRecord
        {
            AgentId = _identityProvider.AgentId,
            UserSid = _identityProvider.UserSid,
            ConsentGiven = true,
            ConsentVersion = _consentConfig.CurrentValue.ConsentVersion,
            GrantedModules = _consentConfig.CurrentValue.RequiredModules,
            IpAddress = GetLocalIpAddress(),
            UserAgent = "EmployeeMonitoring.Agent/1.0"
        };

        return await RecordConsentAsync(record, cancellationToken);
    }

    public async Task<bool> RecordConsentAsync(ConsentRecord record, CancellationToken cancellationToken = default)
    {
        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            var oldStatus = _currentStatus;
            
            _currentStatus = new ConsentStatus
            {
                ConsentGiven = record.ConsentGiven,
                ConsentTimestamp = DateTimeOffset.UtcNow,
                ConsentVersion = record.ConsentVersion,
                GrantedModules = record.GrantedModules,
                RequiresRenewal = false,
                RenewalDeadline = DateTimeOffset.UtcNow.AddDays(_consentConfig.CurrentValue.RenewalDays)
            };

            // Persist to file
            await PersistConsentAsync(record, cancellationToken);

            // Log audit
            await _auditLogger.LogAsync(new AuditLogEntry(
                LogId: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.UtcNow,
                ActorId: record.UserSid,
                ActorName: _identityProvider.UserDisplayName,
                ActorRole: "employee",
                Action: record.ConsentGiven ? "CONSENT_GRANTED" : "CONSENT_REVOKED",
                TargetType: "consent",
                TargetId: _identityProvider.AgentId,
                TargetName: _identityProvider.DeviceName,
                Details: $"Modules: {string.Join(", ", record.GrantedModules)}",
                IpAddress: record.IpAddress,
                UserAgent: record.UserAgent,
                Success: true,
                ErrorMessage: null
            ), cancellationToken);

            // Fire event
            ConsentChanged?.Invoke(this, new ConsentChangedEventArgs
            {
                OldStatus = oldStatus,
                NewStatus = _currentStatus,
                ChangedBy = record.UserSid,
                ChangedAt = DateTimeOffset.UtcNow
            });

            _logger.LogInformation("Consent recorded: {ConsentGiven} for {ModuleCount} modules", 
                record.ConsentGiven, record.GrantedModules.Count);

            return true;
        }
        finally
        {
            _statusLock.Release();
        }
    }

    public async Task<bool> RevokeConsentAsync(string reason, CancellationToken cancellationToken = default)
    {
        var record = new ConsentRecord
        {
            AgentId = _identityProvider.AgentId,
            UserSid = _identityProvider.UserSid,
            ConsentGiven = false,
            ConsentVersion = _consentConfig.CurrentValue.ConsentVersion,
            GrantedModules = new List<string>(),
            IpAddress = GetLocalIpAddress(),
            UserAgent = "EmployeeMonitoring.Agent/1.0"
        };

        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            var oldStatus = _currentStatus;
            
            _currentStatus = new ConsentStatus
            {
                ConsentGiven = false,
                ConsentTimestamp = DateTimeOffset.UtcNow,
                ConsentVersion = record.ConsentVersion,
                GrantedModules = new List<string>(),
                RequiresRenewal = true,
                RenewalDeadline = DateTimeOffset.UtcNow
            };

            // Clear persisted consent
            if (File.Exists(_consentFilePath))
            {
                File.Delete(_consentFilePath);
            }

            // Log audit
            await _auditLogger.LogAsync(new AuditLogEntry(
                LogId: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.UtcNow,
                ActorId: _identityProvider.UserSid,
                ActorName: _identityProvider.UserDisplayName,
                ActorRole: "employee",
                Action: "CONSENT_REVOKED",
                TargetType: "consent",
                TargetId: _identityProvider.AgentId,
                TargetName: _identityProvider.DeviceName,
                Details: $"Reason: {reason}",
                IpAddress: GetLocalIpAddress(),
                UserAgent: "EmployeeMonitoring.Agent/1.0",
                Success: true,
                ErrorMessage: null
            ), cancellationToken);

            ConsentChanged?.Invoke(this, new ConsentChangedEventArgs
            {
                OldStatus = oldStatus,
                NewStatus = _currentStatus,
                ChangedBy = _identityProvider.UserSid,
                ChangedAt = DateTimeOffset.UtcNow
            });

            return true;
        }
        finally
        {
            _statusLock.Release();
        }
    }

    public bool IsConsentValid()
    {
        if (!_currentStatus.ConsentGiven) return false;
        if (_currentStatus.RequiresRenewal && _currentStatus.RenewalDeadline <= DateTimeOffset.UtcNow) return false;
        return true;
    }

    public bool IsModuleConsented(string module)
    {
        return IsConsentValid() && _currentStatus.GrantedModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadConsentAsync(cancellationToken);
    }

    private async Task LoadConsentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_consentFilePath))
        {
            _logger.LogInformation("No existing consent file found");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_consentFilePath, cancellationToken);
            var record = JsonSerializer.Deserialize<ConsentRecord>(json);
            
            if (record != null && record.ConsentGiven)
            {
                await _statusLock.WaitAsync(cancellationToken);
                try
                {
                    _currentStatus = new ConsentStatus
                    {
                        ConsentGiven = record.ConsentGiven,
                        ConsentTimestamp = DateTimeOffset.UtcNow, // Would be stored in real implementation
                        ConsentVersion = record.ConsentVersion,
                        GrantedModules = record.GrantedModules,
                        RequiresRenewal = _currentStatus.RenewalDeadline <= DateTimeOffset.UtcNow,
                        RenewalDeadline = DateTimeOffset.UtcNow.AddDays(_consentConfig.CurrentValue.RenewalDays)
                    };
                }
                finally
                {
                    _statusLock.Release();
                }

                _logger.LogInformation("Loaded existing consent: {ModuleCount} modules", record.GrantedModules.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load consent file");
        }
    }

    private async Task PersistConsentAsync(ConsentRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_consentFilePath, json, cancellationToken);
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}