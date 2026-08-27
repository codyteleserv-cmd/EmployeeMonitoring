namespace EmployeeMonitoring.Contracts;

// Shared non-protobuf helper types used across projects.
// Note: DlpEvent, ConsentRecord, AgentRegistration, and AuditLogEntry
// are defined in the protobuf contracts (agent.proto / api.proto) and
// must not be re-declared here to avoid type conflicts with generated code.

/// <summary>
/// Productivity data point for trend tracking (not part of the wire protocol).
/// </summary>
public class ProductivityDataPoint
{
    public DateTimeOffset Timestamp { get; set; }
    public double Productivity { get; set; }
}
