using EmployeeMonitoring.Agent.Configuration;
using EmployeeMonitoring.Agent.Services;
using EmployeeMonitoring.Contracts;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// gRPC client implementation with automatic reconnection and message queuing.
/// </summary>
public class GrpcClient : IGrpcClient, IDisposable, IAsyncDisposable
{
    private readonly IOptionsMonitor<AgentConfiguration> _config;
    private readonly IAgentIdentityProvider _identityProvider;
    private readonly ILogger<GrpcClient> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    
    private GrpcChannel? _channel;
    private AgentService.AgentServiceClient? _client;
    private AsyncDuplexStreamingCall<AgentMessage, ServerMessage>? _streamingCall;
    private readonly Channel<AgentMessage> _outboundQueue = Channel.CreateUnbounded<AgentMessage>();
    private CancellationTokenSource? _cts;
    private Task? _sendTask;
    private Task? _receiveTask;
    private bool _disposed;

    public event EventHandler<GrpcConnectionEventArgs>? ConnectionStateChanged;
    public event EventHandler<AgentMessage>? MessageReceived;
    
    public bool IsConnected => _streamingCall != null && !_disposed;
    public string Endpoint => _config.CurrentValue.GrpcEndpoint;

    public GrpcClient(
        IOptionsMonitor<AgentConfiguration> config,
        IAgentIdentityProvider identityProvider,
        ILogger<GrpcClient> logger)
    {
        _config = config;
        _identityProvider = identityProvider;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return true;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            var endpoint = _config.CurrentValue.GrpcEndpoint;
            _logger.LogInformation("Connecting to gRPC endpoint: {Endpoint}", endpoint);

            _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.SecureSsl, // Use TLS
                MaxReceiveMessageSize = 10 * 1024 * 1024, // 10MB
                MaxSendMessageSize = 10 * 1024 * 1024,
                HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                    EnableMultipleHttp2Connections = true
                }
            });

            _client = new AgentService.AgentServiceClient(_channel);

            // Start bidirectional streaming
            _streamingCall = _client.Connect();

            // Start send/receive loops
            _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            // Send registration
            var registration = new AgentMessage
            {
                AgentId = _identityProvider.AgentId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Registration = new AgentRegistration
                {
                    DeviceId = _identityProvider.DeviceId,
                    DeviceName = _identityProvider.DeviceName,
                    OsVersion = Environment.OSVersion.VersionString,
                    AgentVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    UserSid = _identityProvider.UserSid,
                    UserDisplayName = _identityProvider.UserDisplayName,
                    Department = _identityProvider.Department,
                    Tags = { _identityProvider.Tags },
                    Capabilities = new Capabilities
                    {
                        ScreenshotCapture = true,
                        ActivityTracking = true,
                        DlpMonitoring = true,
                        ClipboardMonitoring = true,
                        NetworkMonitoring = false,
                        MaxScreenshotIntervalSeconds = 3600,
                        MaxActivityIntervalSeconds = 3600
                    }
                }
            };

            await _outboundQueue.Writer.WriteAsync(registration, _cts.Token);

            _logger.LogInformation("gRPC connection established");
            ConnectionStateChanged?.Invoke(this, new GrpcConnectionEventArgs { IsConnected = true });
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to gRPC endpoint");
            ConnectionStateChanged?.Invoke(this, new GrpcConnectionEventArgs { IsConnected = false, Error = ex.Message });
            await CleanupAsync();
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Disconnecting gRPC client");
            _cts?.Cancel();
            
            try
            {
                if (_streamingCall != null)
                {
                    await _streamingCall.RequestStream.CompleteAsync();
                    await _streamingCall.ResponseStream.Completion;
                }
            }
            catch { }

            await CleanupAsync();
            ConnectionStateChanged?.Invoke(this, new GrpcConnectionEventArgs { IsConnected = false });
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<bool> SendAsync(AgentMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send message: not connected");
            return false;
        }

        message.AgentId = _identityProvider.AgentId;
        message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            await _outboundQueue.Writer.WriteAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue message");
            return false;
        }
    }

    public async Task<AgentConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null) throw new InvalidOperationException("Not connected");

        try
        {
            var request = new ConfigRequest { AgentId = _identityProvider.AgentId };
            var response = await _client.GetConfigurationAsync(request, cancellationToken: cancellationToken);
            
            // Convert protobuf to configuration object
            // This would be implemented based on your configuration mapping
            return new AgentConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration");
            throw;
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outboundQueue.Reader.ReadAllAsync(cancellationToken))
            {
                if (_streamingCall == null) break;
                
                try
                {
                    await _streamingCall.RequestStream.WriteAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send message");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send loop error");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_streamingCall == null) return;

            await foreach (var message in _streamingCall.ResponseStream.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Handle server messages
                    await HandleServerMessageAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to handle server message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receive loop error");
        }
        finally
        {
            if (!_disposed)
            {
                _logger.LogWarning("gRPC receive loop ended, connection lost");
                ConnectionStateChanged?.Invoke(this, new GrpcConnectionEventArgs { IsConnected = false, Error = "Connection lost" });
            }
        }
    }

    private async Task HandleServerMessageAsync(ServerMessage message, CancellationToken cancellationToken)
    {
        // Forward to message received event
        var agentMessage = new AgentMessage(); // Would need to map from ServerMessage
        MessageReceived?.Invoke(this, agentMessage);
    }

    private async Task CleanupAsync()
    {
        _sendTask = null;
        _receiveTask = null;
        _streamingCall = null;
        _client = null;
        
        if (_channel != null)
        {
            await _channel.ShutdownAsync();
            _channel = null;
        }
        
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _ = DisconnectAsync(CancellationToken.None);
        _outboundQueue.Writer.Complete();
        _connectionLock.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync();
        _outboundQueue.Writer.Complete();
        _connectionLock.Dispose();
        _disposed = true;
    }
}