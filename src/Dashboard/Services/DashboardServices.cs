using EmployeeMonitoring.Dashboard.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace EmployeeMonitoring.Dashboard.Services;

/// <summary>
/// Service for SignalR connection to admin hub.
/// </summary>
public class AdminHubService
{
    private readonly HubConnectionBuilder _builder;
    private readonly ILogger<AdminHubService> _logger;
    private HubConnection? _connection;

    public event Action<AgentViewModel>? AgentStatusUpdated;
    public event Action<ScreenshotViewModel>? ScreenshotReceived;
    public event Action<ActivityViewModel>? ActivityReceived;
    public event Action<DlpEventViewModel>? DlpEventReceived;
    public event Action<PauseEventViewModel>? PauseEventReceived;
    public event Action<string>? ConnectionStateChanged;

    public AdminHubService(HubConnectionBuilder builder, ILogger<AdminHubService> logger)
    {
        _builder = builder;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _connection = _builder.Build();

        _connection.On<AgentViewModel>("AgentStatusUpdate", agent =>
        {
            AgentStatusUpdated?.Invoke(agent);
        });

        _connection.On<ScreenshotViewModel>("ScreenshotReceived", screenshot =>
        {
            ScreenshotReceived?.Invoke(screenshot);
        });

        _connection.On<ActivityViewModel>("ActivityReceived", activity =>
        {
            ActivityReceived?.Invoke(activity);
        });

        _connection.On<DlpEventViewModel>("DlpEventReceived", dlp =>
        {
            DlpEventReceived?.Invoke(dlp);
        });

        _connection.On<PauseEventViewModel>("PauseEventReceived", pause =>
        {
            PauseEventReceived?.Invoke(pause);
        });

        _connection.Closed += async (error) =>
        {
            _logger.LogWarning(error, "Admin hub connection closed");
            ConnectionStateChanged?.Invoke("Disconnected");
            await Task.Delay(5000);
            await ConnectAsync();
        };

        _connection.Reconnecting += (error) =>
        {
            _logger.LogInformation("Admin hub reconnecting: {Error}", error?.Message);
            ConnectionStateChanged?.Invoke("Reconnecting");
            return Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) =>
        {
            _logger.LogInformation("Admin hub reconnected: {ConnectionId}", connectionId);
            ConnectionStateChanged?.Invoke("Connected");
            return Task.CompletedTask;
        };

        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            await _connection!.StartAsync();
            _logger.LogInformation("Admin hub connected");
            ConnectionStateChanged?.Invoke("Connected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to admin hub");
            ConnectionStateChanged?.Invoke("Failed");
        }
    }

    public async Task SendPauseCommandAsync(string agentId, string action, string reason)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.SendAsync("SendPauseCommand", agentId, action, reason);
        }
    }

    public async Task ForceResumeAsync(string agentId)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.SendAsync("ForceResume", agentId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }
}

/// <summary>
/// Service for managing agent state in the dashboard.
/// </summary>
public class AgentStateService
{
    private readonly Dictionary<string, AgentViewModel> _agents = new();
    private readonly ILogger<AgentStateService> _logger;

    public IReadOnlyDictionary<string, AgentViewModel> Agents => _agents;
    public int TotalAgents => _agents.Count;
    public int OnlineCount => _agents.Values.Count(a => a.Status == AgentState.Online);
    public int PausedCount => _agents.Values.Count(a => a.Status == AgentState.Paused);
    public int OfflineCount => _agents.Values.Count(a => a.Status == AgentState.Offline);
    public int DlpEvents24h => _agents.Values.Sum(a => a.DlpEventsToday);
    public int UnacknowledgedDlpCount => _agents.Values.Sum(a => a.DlpEventsToday); // Simplified
    public double AvgProductivity => _agents.Values.Any() ? _agents.Values.Average(a => a.ProductivityScore) : 0;
    public int TotalPauseMinutesToday => _agents.Values.Sum(a => a.PauseMinutesToday);
    public List<ProductivityDataPoint> ProductivityTrend { get; } = new();
    public Dictionary<string, int> DlpEventsByType { get; } = new();

    public AgentStateService(ILogger<AgentStateService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // In production, load initial state from API
        await Task.CompletedTask;
    }

    public void UpdateAgent(AgentViewModel agent)
    {
        _agents[agent.AgentId] = agent;
    }

    public void RemoveAgent(string agentId)
    {
        _agents.Remove(agentId);
    }

    public bool CanPause(string agentId)
    {
        if (!_agents.TryGetValue(agentId, out var agent)) return false;
        return agent.Status == AgentState.Online && !agent.IsPaused;
    }
}

/// <summary>
/// Authentication service.
/// </summary>
public class AuthService
{
    public UserInfo? User { get; private set; }
    public bool IsAuthenticated => User != null;
    public bool IsAdmin => User?.Roles.Contains("admin") == true;

    public event Action? AuthenticationStateChanged;

    public void Login(UserInfo user)
    {
        User = user;
        AuthenticationStateChanged?.Invoke();
    }

    public void Logout()
    {
        User = null;
        AuthenticationStateChanged?.Invoke();
    }

    public string GetToken() => User?.Token ?? string.Empty;
}

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Initials => string.Join("", Name.Split(' ').Select(n => n[0]).Take(2)).ToUpper();
    public string Token { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// API service for REST calls.
/// </summary>
public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private readonly ILogger<ApiService> _logger;

    public ApiService(HttpClient httpClient, AuthService authService, ILogger<ApiService> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    private void AddAuthHeader()
    {
        var token = _authService.GetToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        AddAuthHeader();
        return await _httpClient.GetFromJsonAsync<T>(url, cancellationToken);
    }

    public async Task<T?> PostAsync<T>(string url, object payload, CancellationToken cancellationToken = default)
    {
        AddAuthHeader();
        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public async Task<T?> PutAsync<T>(string url, object payload, CancellationToken cancellationToken = default)
    {
        AddAuthHeader();
        var response = await _httpClient.PutAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public async Task DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        AddAuthHeader();
        var response = await _httpClient.DeleteAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Notification service for toast messages.
/// </summary>
public class NotificationService
{
    private readonly ISnackbar _snackbar;

    public NotificationService(ISnackbar snackbar)
    {
        _snackbar = snackbar;
    }

    public void Success(string message) => _snackbar.Add(message, Severity.Success);
    public void Error(string message) => _snackbar.Add(message, Severity.Error);
    public void Warning(string message) => _snackbar.Add(message, Severity.Warning);
    public void Info(string message) => _snackbar.Add(message, Severity.Info);

    public void ShowPauseNotification(string userName, string reason)
    {
        _snackbar.Add($"⏸️ {userName} paused monitoring: {reason}", Severity.Warning, config =>
        {
            config.VisibleStateDuration = 10000;
            config.ShowCloseIcon = true;
        });
    }

    public void ShowDlpAlert(string userName, string type, Severity severity)
    {
        var sev = severity switch
        {
            Models.Severity.Critical => MudBlazor.Severity.Error,
            Models.Severity.High => MudBlazor.Severity.Error,
            Models.Severity.Medium => MudBlazor.Severity.Warning,
            _ => MudBlazor.Severity.Info
        };

        _snackbar.Add($"🚨 DLP: {type} for {userName}", sev, config =>
        {
            config.VisibleStateDuration = 15000;
            config.ShowCloseIcon = true;
            config.RequireInteraction = true;
        });
    }
}