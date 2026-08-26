using EmployeeMonitoring.Contracts;

namespace EmployeeMonitoring.Agent.Services;

/// <summary>
/// gRPC client for agent-server communication.
/// </summary>
public interface IGrpcClient
{
    event EventHandler<GrpcConnectionEventArgs>? ConnectionStateChanged;
    event EventHandler<AgentMessage>? MessageReceived;
    
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<bool> SendAsync(AgentMessage message, CancellationToken cancellationToken = default);
    Task<AgentConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default);
    bool IsConnected { get; }
    string Endpoint { get; }
}

public class GrpcConnectionEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// SignalR client for real-time notifications.
/// </summary>
public interface ISignalRClient
{
    event EventHandler<SignalRConnectionEventArgs>? ConnectionStateChanged;
    event EventHandler<ConfigUpdate>? ConfigUpdateReceived;
    event EventHandler<PauseCommand>? PauseCommandReceived;
    event EventHandler<ConsentRequest>? ConsentRequestReceived;
    event EventHandler<DiagnosticCommand>? DiagnosticCommandReceived;
    
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    bool IsConnected { get; }
    string HubUrl { get; }
}

public class SignalRConnectionEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Message dispatcher for sending batched messages to server.
/// </summary>
public interface IMessageDispatcher
{
    Task<bool> SendScreenshotBatchAsync(List<Screenshot> screenshots, CancellationToken cancellationToken = default);
    Task<bool> SendActivityBatchAsync(List<ActivitySample> activities, CancellationToken cancellationToken = default);
    Task<bool> SendPauseEventAsync(PauseEvent pauseEvent, CancellationToken cancellationToken = default);
    Task<bool> SendDlpEventAsync(DlpEvent dlpEvent, CancellationToken cancellationToken = default);
    Task<bool> SendHeartbeatAsync(AgentHealth health, CancellationToken cancellationToken = default);
    Task<bool> SendDiagnosticInfoAsync(DiagnosticInfo info, CancellationToken cancellationToken = default);
    Task<bool> SendConsentAckAsync(ConsentAck ack, CancellationToken cancellationToken = default);
    Task<bool> SendAdminNotificationAsync(object notification, CancellationToken cancellationToken = default);
}