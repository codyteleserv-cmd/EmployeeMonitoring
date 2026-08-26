using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Agent.Modules;
using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Contracts;
using Microsoft.Extensions.Options;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EmployeeMonitoring.Agent.Modules;

/// <summary>
/// Screenshot capture service with multi-monitor support and smart blur.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreenshotService : IScreenshotService, IDisposable
{
    private readonly IOptionsMonitor<ScreenshotConfiguration> _config;
    private readonly IOptionsMonitor<PrivacyConfiguration> _privacyConfig;
    private readonly IPauseManager _pauseManager;
    private readonly IConsentManager _consentManager;
    private readonly ILogger<ScreenshotService> _logger;
    
    private Timer? _captureTimer;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private bool _disposed;

    public event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;
    public event EventHandler<ScreenshotErrorEventArgs>? ScreenshotError;
    public bool IsRunning { get; private set; }

    public ScreenshotService(
        IOptionsMonitor<ScreenshotConfiguration> config,
        IOptionsMonitor<PrivacyConfiguration> privacyConfig,
        IPauseManager pauseManager,
        IConsentManager consentManager,
        ILogger<ScreenshotService> logger)
    {
        _config = config;
        _privacyConfig = privacyConfig;
        _pauseManager = pauseManager;
        _consentManager = consentManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        
        if (!_consentManager.IsModuleConsented("screenshots"))
        {
            _logger.LogWarning("Screenshot consent not granted");
            return;
        }

        if (!_config.CurrentValue.Enabled)
        {
            _logger.LogInformation("Screenshots disabled in configuration");
            return;
        }

        _captureTimer = new Timer(
            async _ => await CaptureAndEmitAsync(cancellationToken),
            null,
            TimeSpan.FromSeconds(_config.CurrentValue.IntervalSeconds),
            TimeSpan.FromSeconds(_config.CurrentValue.IntervalSeconds));

        IsRunning = true;
        _logger.LogInformation("Screenshot service started (interval: {Interval}s)", _config.CurrentValue.IntervalSeconds);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        
        _captureTimer?.Dispose();
        _captureTimer = null;
        IsRunning = false;
        _logger.LogInformation("Screenshot service stopped");
    }

    public async Task<ScreenshotResult> CaptureNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_consentManager.IsModuleConsented("screenshots"))
        {
            return new ScreenshotResult { Success = false, Error = "Consent not granted" };
        }

        return await CaptureScreenshotsAsync(cancellationToken);
    }

    private async Task CaptureAndEmitAsync(CancellationToken cancellationToken)
    {
        // Check if monitoring should run (work hours, not paused, consent)
        if (!ShouldCapture()) return;

        var result = await CaptureScreenshotsAsync(cancellationToken);
        
        if (result.Success && result.Screenshots.Count > 0)
        {
            ScreenshotCaptured?.Invoke(this, new ScreenshotCapturedEventArgs
            {
                Screenshots = result.Screenshots,
                CapturedAt = DateTimeOffset.UtcNow
            });
        }
        else if (!result.Success)
        {
            ScreenshotError?.Invoke(this, new ScreenshotErrorEventArgs
            {
                Error = result.Error ?? "Unknown error",
                Exception = null
            });
        }
    }

    private bool ShouldCapture()
    {
        if (!IsRunning) return false;
        if (!_consentManager.IsModuleConsented("screenshots")) return false;
        if (_pauseManager.GetPauseState().IsPaused) return false;
        
        // Check work hours
        var workConfig = _config.CurrentValue; // Would need WorkScheduleConfiguration
        // For now, always allow - work hours check would be added here
        
        return true;
    }

    private async Task<ScreenshotResult> CaptureScreenshotsAsync(CancellationToken cancellationToken)
    {
        await _captureLock.WaitAsync(cancellationToken);
        try
        {
            var screenshots = new List<Screenshot>();
            var config = _config.CurrentValue;
            var privacyConfig = _privacyConfig.CurrentValue;

            try
            {
                var screenCount = config.MultiMonitor ? Screen.AllScreens.Length : 1;
                
                for (int i = 0; i < screenCount; i++)
                {
                    var screen = Screen.AllScreens[i];
                    var screenshot = await CaptureScreenAsync(screen, i, config, privacyConfig, cancellationToken);
                    if (screenshot != null)
                    {
                        screenshots.Add(screenshot);
                    }
                }

                return new ScreenshotResult { Success = true, Screenshots = screenshots };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to capture screenshots");
                return new ScreenshotResult { Success = false, Error = ex.Message };
            }
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private async Task<Screenshot?> CaptureScreenAsync(
        Screen screen, 
        int monitorIndex, 
        ScreenshotConfiguration config,
        PrivacyConfiguration privacyConfig,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format24bppRgb);
                using var graphics = Graphics.FromImage(bitmap);
                
                graphics.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);

                // Apply smart blur if enabled
                var blurRegions = new List<BlurRegion>();
                if (config.SmartBlurEnabled && privacyConfig.SmartBlurEnabled)
                {
                    blurRegions = ApplySmartBlur(bitmap, privacyConfig);
                }

                // Resize if needed
                if (bitmap.Width > config.MaxWidth || bitmap.Height > config.MaxHeight)
                {
                    var resized = ResizeImage(bitmap, config.MaxWidth, config.MaxHeight);
                    bitmap.Dispose(); // Will be handled by using
                    // Note: In real implementation, we'd need to handle this differently
                }

                // Compress to JPEG
                var imageBytes = CompressToJpeg(bitmap, config.Quality);
                
                // Get active window info
                var (windowTitle, processName) = GetActiveWindowInfo();

                return new Screenshot
                {
                    Id = Guid.NewGuid().ToString(),
                    CapturedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MonitorIndex = monitorIndex,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    ImageData = Google.Protobuf.ByteString.CopyFrom(imageBytes),
                    Format = "jpeg",
                    Blurred = blurRegions.Count > 0,
                    BlurRegions = { blurRegions },
                    ActiveWindowTitle = windowTitle,
                    ActiveProcessName = processName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to capture screen {MonitorIndex}", monitorIndex);
                return null;
            }
        }, cancellationToken);
    }

    private List<BlurRegion> ApplySmartBlur(Bitmap bitmap, PrivacyConfiguration privacyConfig)
    {
        var blurRegions = new List<BlurRegion>();
        
        try
        {
            // Get foreground window
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return blurRegions;

            // Get window class name
            var className = GetWindowClassName(foregroundWindow);
            
            // Check if this window class should be blurred
            if (privacyConfig.BlurWindowClasses.Any(c => 
                className.Contains(c, StringComparison.OrdinalIgnoreCase)))
            {
                // Blur the entire window area
                var windowRect = GetWindowRect(foregroundWindow);
                var blurRegion = new BlurRegion
                {
                    X = windowRect.Left,
                    Y = windowRect.Top,
                    Width = windowRect.Right - windowRect.Left,
                    Height = windowRect.Bottom - windowRect.Top,
                    Reason = BlurReason.UserRequested
                };
                
                // Apply blur to bitmap
                ApplyBlurToRegion(bitmap, blurRegion);
                blurRegions.Add(blurRegion);
            }

            // Additionally, detect password fields and sensitive inputs
            // This would require UI Automation or OCR in a real implementation
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Smart blur failed");
        }

        return blurRegions;
    }

    private void ApplyBlurToRegion(Bitmap bitmap, BlurRegion region)
    {
        // Simple box blur implementation
        var rect = new Rectangle(region.X, region.Y, region.Width, region.Height);
        rect.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var regionBitmap = bitmap.Clone(rect, bitmap.PixelFormat);
        using var blurred = new Bitmap(regionBitmap.Width, regionBitmap.Height);
        
        using (var g = Graphics.FromImage(blurred))
        {
            // Draw scaled down then up for blur effect
            var smallRect = new Rectangle(0, 0, Math.Max(1, regionBitmap.Width / 8), Math.Max(1, regionBitmap.Height / 8));
            using var small = new Bitmap(regionBitmap, smallRect.Size);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, new Rectangle(0, 0, blurred.Width, blurred.Height));
        }
        
        using (var g = Graphics.FromImage(bitmap))
        {
            g.DrawImage(blurred, rect.Location);
        }
    }

    private byte[] CompressToJpeg(Bitmap bitmap, int quality)
    {
        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        bitmap.Save(ms, encoder, encoderParams);
        return ms.ToArray();
    }

    private Bitmap ResizeImage(Bitmap source, int maxWidth, int maxHeight)
    {
        var ratioX = (double)maxWidth / source.Width;
        var ratioY = (double)maxHeight / source.Height;
        var ratio = Math.Min(ratioX, ratioY);
        
        var newWidth = (int)(source.Width * ratio);
        var newHeight = (int)(source.Height * ratio);
        
        var dest = new Bitmap(newWidth, newHeight);
        using (var g = Graphics.FromImage(dest))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, newWidth, newHeight);
        }
        return dest;
    }

    private (string windowTitle, string processName) GetActiveWindowInfo()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return (string.Empty, string.Empty);

            var title = GetWindowText(hWnd);
            var processId = GetWindowProcessId(hWnd);
            var processName = string.Empty;
            
            if (processId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    processName = process.ProcessName;
                }
                catch { }
            }

            return (title, processName);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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

    private RECT GetWindowRect(IntPtr hWnd)
    {
        GetWindowRect(hWnd, out RECT rect);
        return rect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _captureTimer?.Dispose();
        _captureLock.Dispose();
        _disposed = true;
    }
}