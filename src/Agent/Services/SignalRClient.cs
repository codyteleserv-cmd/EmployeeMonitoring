using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// SignalR client for real-time server-to-agent communication.
/// </summary>
public class SignalRClient : ISignalRClient, IAsyncDisposable
{
    private readonly IOptionsMonitor<AgentConfiguration> _config;
    private readonly ILogger<SignalRClient> _logger;
    private HubConnection? _connection;
    private bool _disposed;

    public event EventHandler<SignalRConnectionEventArgs>? ConnectionStateChanged;
    public event EventHandler<ConfigUpdate>? ConfigUpdateReceived;
    public event EventHandler<PauseCommand>? PauseCommandReceived;
    public event EventHandler<ConsentRequest>? ConsentRequestReceived;
    public event EventHandler<DiagnosticCommand>? DiagnosticCommandReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
    public string HubUrl => _config.CurrentValue.SignalRHubUrl;

    public SignalRClient(
        IOptionsMonitor<AgentConfiguration> config,
        ILogger<SignalRClient> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;

        try
        {
            _logger.LogInformation("Connecting to SignalR hub: {HubUrl}", _config.CurrentValue.SignalRHubUrl);

            _connection = new HubConnectionBuilder()
                .WithUrl(_config.CurrentValue.SignalRHubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(null); // Would use auth token
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                                         Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents |
                                         Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                })
                .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1) })
                .ConfigureLogging(logging => logging.AddConsole())
                .Build();

            // Register handlers
            _connection.On<ConfigUpdate>("ConfigUpdate", async update =>
            {
                _logger.LogInformation("Received config update v{Version}", update.ConfigVersion);
                ConfigUpdateReceived?.Invoke(this, update);
            });

            _connection.On<PauseCommand>("PauseCommand", async command =>
            {
                _logger.LogInformation("Received pause command: {Type}", command.Type);
                PauseCommandReceived?.Invoke(this, command);
            });

            _connection.On<ConsentRequest>("ConsentRequest", async request =>
            {
                _logger.LogInformation("Received consent request");
                ConsentRequestReceived?.Invoke(this, request);
            });

            _connection.On<DiagnosticCommand>("DiagnosticCommand", async command =>
            {
                _logger.LogInformation("Received diagnostic command: {Type}", command.Type);
                DiagnosticCommandReceived?.Invoke(this, command);
            });

            _connection.Closed += async (error) =>
            {
                _logger.LogWarning(error, "SignalR connection closed");
                ConnectionStateChanged?.Invoke(this, new SignalRConnectionEventArgs { IsConnected = false, Error = error?.Message });
            };

            _connection.Reconnecting += async (error) =>
            {
                _logger.LogInformation("SignalR reconnecting: {Error}", error?.Message);
            };

            _connection.Reconnected += async (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
                ConnectionStateChanged?.Invoke(this, new SignalRConnectionEventArgs { IsConnected = true });
            };

            await _connection.StartAsync(cancellationToken);
            
            _logger.LogInformation("SignalR connection established");
            ConnectionStateChanged?.Invoke(this, new SignalRConnectionEventArgs { IsConnected = true });
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub");
            ConnectionStateChanged?.Invoke(this, new SignalRConnectionEventArgs { IsConnected = false, Error = ex.Message });
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
            try
            {
                await _connection.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping SignalR connection");
            }
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync();
        _disposed = true;
    }
}