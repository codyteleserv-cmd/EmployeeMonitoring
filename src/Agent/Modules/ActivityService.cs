using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Agent.Modules;
using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Contracts;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EmployeeMonitoring.Agent.Modules;

/// <summary>
/// Activity tracking service - tracks foreground window, idle time, and input activity.
/// NO KEYSTROKE LOGGING - only activity levels.
/// </summary>
[SupportedOSPlatform("windows")]
public class ActivityService : IActivityService, IDisposable
{
    private readonly IOptionsMonitor<ActivityConfiguration> _config;
    private readonly IPauseManager _pauseManager;
    private readonly IConsentManager _consentManager;
    private readonly ILogger<ActivityService> _logger;
    
    private Timer? _sampleTimer;
    private readonly SemaphoreSlim _sampleLock = new(1, 1);
    private DateTimeOffset _lastInputTime = DateTimeOffset.UtcNow;
    private IntPtr _lastForegroundWindow = IntPtr.Zero;
    private string _lastProcessName = string.Empty;
    private bool _disposed;

    public event EventHandler<ActivitySampledEventArgs>? ActivitySampled;
    public event EventHandler<ActivityErrorEventArgs>? ActivityError;
    public bool IsRunning { get; private set; }

    public ActivityService(
        IOptionsMonitor<ActivityConfiguration> config,
        IPauseManager pauseManager,
        IConsentManager consentManager,
        ILogger<ActivityService> logger)
    {
        _config = config;
        _pauseManager = pauseManager;
        _consentManager = consentManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        
        if (!_consentManager.IsModuleConsented("activity"))
        {
            _logger.LogWarning("Activity tracking consent not granted");
            return;
        }

        if (!_config.CurrentValue.Enabled)
        {
            _logger.LogInformation("Activity tracking disabled in configuration");
            return;
        }

        _sampleTimer = new Timer(
            async _ => await SampleAndEmitAsync(cancellationToken),
            null,
            TimeSpan.FromSeconds(_config.CurrentValue.SampleIntervalSeconds),
            TimeSpan.FromSeconds(_config.CurrentValue.SampleIntervalSeconds));

        IsRunning = true;
        _logger.LogInformation("Activity service started (interval: {Interval}s)", _config.CurrentValue.SampleIntervalSeconds);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        
        _sampleTimer?.Dispose();
        _sampleTimer = null;
        IsRunning = false;
        _logger.LogInformation("Activity service stopped");
    }

    public async Task<ActivitySample> SampleNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_consentManager.IsModuleConsented("activity"))
        {
            throw new InvalidOperationException("Consent not granted for activity tracking");
        }

        return await SampleActivityAsync(cancellationToken);
    }

    private async Task SampleAndEmitAsync(CancellationToken cancellationToken)
    {
        if (!ShouldSample()) return;

        try
        {
            var sample = await SampleActivityAsync(cancellationToken);
            
            ActivitySampled?.Invoke(this, new ActivitySampledEventArgs { Sample = sample });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sample activity");
            ActivityError?.Invoke(this, new ActivityErrorEventArgs
            {
                Error = ex.Message,
                Exception = ex
            });
        }
    }

    private bool ShouldSample()
    {
        if (!IsRunning) return false;
        if (!_consentManager.IsModuleConsented("activity")) return false;
        if (_pauseManager.GetPauseState().IsPaused) return false;
        return true;
    }

    private async Task<ActivitySample> SampleActivityAsync(CancellationToken cancellationToken)
    {
        await _sampleLock.WaitAsync(cancellationToken);
        try
        {
            var config = _config.CurrentValue;
            var now = DateTimeOffset.UtcNow;

            // Get foreground window
            var (processName, windowTitle, windowClass, domain) = GetForegroundWindowInfo();
            
            // Calculate idle time
            var idleSeconds = GetIdleTimeSeconds();
            var isIdle = idleSeconds > 30; // Consider idle after 30 seconds
            
            // Calculate input activity level (no keystroke logging!)
            var inputLevel = CalculateInputActivityLevel(idleSeconds);

            // Determine productivity level
            var productivity = DetermineProductivity(processName, windowTitle, domain);

            var sample = new ActivitySample
            {
                Timestamp = now.ToUnixTimeMilliseconds(),
                DurationSeconds = config.SampleIntervalSeconds,
                ProcessName = processName,
                WindowTitle = windowTitle,
                WindowClass = windowClass,
                Domain = domain,
                Productivity = productivity,
                IsIdle = isIdle,
                IdleSeconds = (int)idleSeconds,
                ActiveSeconds = isIdle ? 0 : config.SampleIntervalSeconds,
                InputLevel = inputLevel
            };

            // Update tracking state
            _lastForegroundWindow = GetForegroundWindow();
            _lastProcessName = processName;

            return sample;
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    private (string processName, string windowTitle, string windowClass, string domain) GetForegroundWindowInfo()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return (string.Empty, string.Empty, string.Empty, string.Empty);

            var windowTitle = GetWindowText(hWnd);
            var windowClass = GetWindowClassName(hWnd);
            var processId = GetWindowProcessId(hWnd);
            
            var processName = string.Empty;
            var domain = string.Empty;

            if (processId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    processName = process.ProcessName;
                    
                    // Try to extract domain from browser window title
                    domain = ExtractDomainFromTitle(windowTitle, processName);
                }
                catch { }
            }

            return (processName, windowTitle, windowClass, domain);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private string ExtractDomainFromTitle(string windowTitle, string processName)
    {
        // Common browser process names
        var browsers = new[] { "chrome", "firefox", "msedge", "iexplore", "opera", "brave" };
        if (!browsers.Contains(processName.ToLowerInvariant())) return string.Empty;

        // Try to extract domain from title
        // Format: "Page Title - Domain.com - Browser"
        var parts = windowTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts.Reverse())
        {
            if (Uri.CheckHostName(part) != UriHostNameType.Unknown)
            {
                return part;
            }
            
            // Check if it looks like a domain
            if (part.Contains('.') && !part.Contains(' ') && part.Length > 3)
            {
                return part;
            }
        }

        return string.Empty;
    }

    private long GetIdleTimeSeconds()
    {
        var lastInput = GetLastInputInfo();
        return (long)(DateTimeOffset.UtcNow - lastInput).TotalSeconds;
    }

    private InputActivityLevel CalculateInputActivityLevel(long idleSeconds)
    {
        if (idleSeconds <= 10) return InputActivityLevel.High;
        if (idleSeconds <= 60) return InputActivityLevel.Moderate;
        if (idleSeconds <= 300) return InputActivityLevel.Low;
        return InputActivityLevel.None;
    }

    private ProductivityLevel DetermineProductivity(string processName, string windowTitle, string domain)
    {
        var config = _config.CurrentValue;
        
        foreach (var category in config.Categories.OrderByDescending(c => c.Weight))
        {
            // Check process name
            if (category.ProcessNames.Any(p => 
                processName.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return ParseProductivityLevel(category.Name);
            }

            // Check window title patterns
            if (category.WindowTitlePatterns.Any(pattern => 
                MatchesPattern(windowTitle, pattern)))
            {
                return ParseProductivityLevel(category.Name);
            }

            // Check domain patterns
            if (!string.IsNullOrEmpty(domain) && 
                category.DomainPatterns.Any(pattern => 
                    MatchesPattern(domain, pattern)))
            {
                return ParseProductivityLevel(category.Name);
            }
        }

        return ProductivityLevel.Unknown;
    }

    private ProductivityLevel ParseProductivityLevel(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "productive" => ProductivityLevel.Productive,
            "neutral" => ProductivityLevel.Neutral,
            "distracting" => ProductivityLevel.Distracting,
            _ => ProductivityLevel.Unknown
        };
    }

    private bool MatchesPattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern)) return false;
        
        // Simple wildcard matching
        if (pattern.Contains('*'))
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
        }
        
        return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    // P/Invoke for Windows API
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private DateTimeOffset GetLastInputInfo()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);
        GetLastInputInfo(ref info);
        
        var tickCount = Environment.TickCount64;
        var lastInputTick = (long)info.dwTime;
        var idleMilliseconds = tickCount - lastInputTick;
        
        return DateTimeOffset.UtcNow.AddMilliseconds(-idleMilliseconds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    private string GetWindowText(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private int GetWindowProcessId(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint processId);
        return (int)processId;
    }

    private string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _sampleTimer?.Dispose();
        _sampleLock.Dispose();
        _disposed = true;
    }
}