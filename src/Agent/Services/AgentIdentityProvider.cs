using EmployeeMonitoring.Agent.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Principal;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// Provides agent identity information from system and configuration.
/// </summary>
public class AgentIdentityProvider : IAgentIdentityProvider
{
    private readonly IOptionsMonitor<AgentConfiguration> _config;
    private readonly ILogger<AgentIdentityProvider> _logger;
    private string _agentId = string.Empty;
    private string _deviceId = string.Empty;

    public AgentIdentityProvider(
        IOptionsMonitor<AgentConfiguration> config,
        ILogger<AgentIdentityProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string AgentId => _agentId;
    public string DeviceId => _deviceId;
    public string DeviceName => Environment.MachineName;
    public string UserSid => _config.CurrentValue.UserSid;
    public string UserDisplayName => GetUserDisplayName();
    public string Department => _config.CurrentValue.Department;
    public Dictionary<string, string> Tags => _config.CurrentValue.Tags;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _deviceId = await GetOrCreateDeviceIdAsync(cancellationToken);
        _agentId = $"{DeviceName}-{_deviceId[..8]}";
        
        // Get user SID if not configured
        if (string.IsNullOrEmpty(UserSid))
        {
            var sid = GetCurrentUserSid();
            if (!string.IsNullOrEmpty(sid))
            {
                _logger.LogInformation("Detected user SID: {Sid}", sid);
            }
        }

        _logger.LogInformation("Agent initialized: {AgentId} (Device: {DeviceId})", AgentId, DeviceId);
    }

    private string GetUserDisplayName()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            return identity?.Name ?? Environment.UserName;
        }
        catch
        {
            return Environment.UserName;
        }
    }

    private string GetCurrentUserSid()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            return identity?.User?.Value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken)
    {
        var config = _config.CurrentValue;
        
        if (!string.IsNullOrEmpty(config.DeviceId))
        {
            return config.DeviceId;
        }

        // Generate stable device ID from hardware
        var deviceId = await GenerateStableDeviceIdAsync(cancellationToken);
        
        // Note: In production, this would be persisted via configuration manager
        return deviceId;
    }

    private async Task<string> GenerateStableDeviceIdAsync(CancellationToken cancellationToken)
    {
        // Use machine GUID from registry as stable identifier
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var machineGuid = key?.GetValue("MachineGuid") as string;
            
            if (!string.IsNullOrEmpty(machineGuid))
            {
                return machineGuid.ToUpperInvariant();
            }
        }
        catch
        {
            // Fall through to fallback
        }

        // Fallback: hash of machine name + OS version
        var fallback = $"{Environment.MachineName}-{Environment.OSVersion.Version}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fallback));
        return Convert.ToHexString(hash)[..32];
    }
}