# Employee Monitoring Platform - API Reference

**Version:** 1.0  
**Base URL:** `https://api.monitoring.company.com`  
**gRPC Endpoint:** `grpc.monitoring.company.com:443`  
**SignalR Hubs:** `/hubs/agent`, `/hubs/admin`

---

## 1. Authentication

All API endpoints require authentication via **JWT Bearer token** obtained from Azure AD / OIDC.

```http
Authorization: Bearer <jwt_token>
```

### Token Claims
| Claim | Description |
|-------|-------------|
| `sub` | User unique identifier |
| `role` | Role: `admin`, `security`, `hr`, `team_lead`, `employee` |
| `department` | User's department |
| `team` | User's team |
| `agent_ids` | Array of agent IDs user can access (for team leads) |

### Authorization Policies
| Policy | Required Role(s) |
|--------|------------------|
| `AdminOnly` | `admin` |
| `SecurityOnly` | `security`, `admin` |
| `TeamLeadOrAbove` | `team_lead`, `security`, `admin` |
| `HROrAbove` | `hr`, `security`, `admin` |

---

## 2. gRPC Services

### 2.1 Agent Service (`AgentService`)

**Endpoint:** `AgentService`  
**Transport:** Bidirectional streaming gRPC over TLS 1.3 + mTLS

#### Connect (Streaming)
```protobuf
rpc Connect(stream AgentMessage) returns (stream ServerMessage);
```
Establishes persistent connection for agent communication.

**AgentMessage Types:**
| Message | Direction | Description |
|---------|-----------|-------------|
| `AgentRegistration` | Agent → API | Initial registration with capabilities |
| `ScreenshotBatch` | Agent → API | Batched screenshots (every 30s) |
| `ActivityBatch` | Agent → API | Batched activity samples (every 10s) |
| `PauseEvent` | Agent → API | Pause/resume events (immediate) |
| `DlpEvent` | Agent → API | DLP events (immediate) |
| `Heartbeat` | Agent → API | Health check (every 30s) |
| `DiagnosticInfo` | Agent → API | Diagnostic metrics |

**ServerMessage Types:**
| Message | Direction | Description |
|---------|-----------|-------------|
| `ConfigUpdate` | API → Agent | Configuration changes |
| `PauseCommand` | API → Agent | Pause/resume/force commands |
| `ConsentRequest` | API → Agent | Request user consent |
| `DiagnosticCommand` | API → Agent | Diagnostics (logs, self-test) |
| `Acknowledgement` | API → Agent | Message acknowledgment |

#### GetConfiguration
```protobuf
rpc GetConfiguration(ConfigRequest) returns (AgentConfiguration);
```
Returns agent-specific configuration (merged global + department + agent).

#### Heartbeat
```protobuf
rpc Heartbeat(HeartbeatRequest) returns (HeartbeatResponse);
```
Agent health check; returns server time and config updates.

### 2.2 Admin Service (`AdminService`)

**Endpoint:** `AdminService`  
**Transport:** gRPC (unary + server streaming)

#### Real-time Monitoring
```protobuf
rpc WatchAgents(WatchAgentsRequest) returns (stream AgentStatusUpdate);
rpc WatchAgentDetails(WatchAgentDetailsRequest) returns (stream AgentDetailUpdate);
```

#### Screenshots
```protobuf
rpc GetScreenshots(GetScreenshotsRequest) returns (stream ScreenshotData);
rpc GetScreenshot(GetScreenshotRequest) returns (ScreenshotData);
```

#### Activity & Productivity
```protobuf
rpc GetActivitySummary(GetActivitySummaryRequest) returns (ActivitySummary);
rpc GetProductivityReport(GetProductivityReportRequest) returns (ProductivityReport);
rpc GetTeamProductivity(GetTeamProductivityRequest) returns (TeamProductivityReport);
```

#### Pause Management
```protobuf
rpc GetPauseEvents(GetPauseEventsRequest) returns (stream PauseEventRecord);
rpc GetPauseStatistics(GetPauseStatisticsRequest) returns (PauseStatistics);
rpc ForceResumeAgent(ForceResumeRequest) returns (ForceResumeResponse);
rpc SendPauseCommand(SendPauseCommandRequest) returns (SendPauseCommandResponse);
```

#### DLP Management
```protobuf
rpc GetDlpEvents(GetDlpEventsRequest) returns (stream DlpEventRecord);
rpc GetDlpStatistics(GetDlpStatisticsRequest) returns (DlpStatistics);
rpc AcknowledgeDlpEvent(AcknowledgeDlpEventRequest) returns (AcknowledgeDlpEventResponse);
```

#### Configuration
```protobuf
rpc GetAgentConfiguration(GetAgentConfigRequest) returns (AgentConfiguration);
rpc UpdateAgentConfiguration(UpdateAgentConfigRequest) returns (UpdateAgentConfigResponse);
rpc GetGlobalConfiguration(GlobalConfigRequest) returns (GlobalConfiguration);
rpc UpdateGlobalConfiguration(UpdateGlobalConfigRequest) returns (UpdateGlobalConfigResponse);
rpc DeployConfiguration(DeployConfigRequest) returns (DeployConfigResponse);
```

#### User & Device Management
```protobuf
rpc ListUsers(ListUsersRequest) returns (UserList);
rpc GetUser(GetUserRequest) returns (UserProfile);
rpc UpdateUser(UpdateUserRequest) returns (UpdateUserResponse);
rpc ListDevices(ListDevicesRequest) returns (DeviceList);
rpc GetDevice(GetDeviceRequest) returns (DeviceDetail);
rpc DecommissionDevice(DecommissionDeviceRequest) returns (DecommissionResponse);
```

#### Consent
```protobuf
rpc GetConsentStatuses(GetConsentStatusesRequest) returns (stream ConsentStatusRecord);
rpc RequestConsent(RequestConsentRequest) returns (RequestConsentResponse);
```

#### Audit & Compliance
```protobuf
rpc GetAuditLog(GetAuditLogRequest) returns (stream AuditLogEntry);
rpc ExportAuditLog(ExportAuditLogRequest) returns (ExportAuditLogResponse);
rpc GetComplianceReport(GetComplianceReportRequest) returns (ComplianceReport);
```

#### Alerts
```protobuf
rpc GetAlerts(GetAlertsRequest) returns (stream AlertRecord);
rpc AcknowledgeAlert(AcknowledgeAlertRequest) returns (AcknowledgeAlertResponse);
rpc CreateAlertRule(CreateAlertRuleRequest) returns (AlertRule);
rpc UpdateAlertRule(UpdateAlertRuleRequest) returns (AlertRule);
rpc DeleteAlertRule(DeleteAlertRuleRequest) returns (DeleteAlertRuleResponse);
rpc ListAlertRules(ListAlertRulesRequest) returns (AlertRuleList);
```

#### Reports
```protobuf
rpc GenerateReport(GenerateReportRequest) returns (ReportJob);
rpc GetReportStatus(GetReportStatusRequest) returns (ReportJob);
rpc DownloadReport(DownloadReportRequest) returns (stream ReportChunk);
```

---

## 3. REST API (Supplementary)

### 3.1 Authentication
```http
POST /api/auth/token
Content-Type: application/json

{
  "grant_type": "client_credentials",
  "client_id": "...",
  "client_secret": "...",
  "scope": "api://monitoring/.default"
}
```

### 3.2 Agent Management
```http
GET    /api/v1/agents                    # List agents (with filters)
GET    /api/v1/agents/{agentId}          # Get agent details
PATCH  /api/v1/agents/{agentId}          # Update agent (tags, department)
POST   /api/v1/agents/{agentId}/pause    # Pause agent (admin)
POST   /api/v1/agents/{agentId}/resume   # Resume agent (admin)
DELETE /api/v1/agents/{agentId}          # Decommission agent
```

### 3.3 Screenshots
```http
GET    /api/v1/agents/{agentId}/screenshots          # List with filters
GET    /api/v1/screenshots/{screenshotId}            # Get screenshot (with image)
GET    /api/v1/screenshots/{screenshotId}/thumbnail  # Get thumbnail only
```

### 3.4 Activity
```http
GET    /api/v1/agents/{agentId}/activity             # Activity samples
GET    /api/v1/agents/{agentId}/activity/summary     # Aggregated summary
GET    /api/v1/reports/productivity                  # Team/org productivity
```

### 3.5 DLP Events
```http
GET    /api/v1/dlpevents              # List with filters
GET    /api/v1/dlpevents/{eventId}    # Get event details
PATCH  /api/v1/dlpevents/{eventId}/acknowledge        # Acknowledge
GET    /api/v1/reports/dlpsummary     # DLP statistics
```

### 3.6 Pause Management
```http
GET    /api/v1/pauseevents             # List with filters
GET    /api/v1/pausestatistics         # Statistics
POST   /api/v1/agents/{agentId}/force-resume            # Admin force resume
```

### 3.7 Reports
```http
POST   /api/v1/reports/generate        # Generate report
GET    /api/v1/reports/{jobId}         # Report status
GET    /api/v1/reports/{jobId}/download               # Download file
```

### 3.8 Configuration
```http
GET    /api/v1/config/global           # Global config
PATCH  /api/v1/config/global           # Update global
GET    /api/v1/config/agents/{agentId} # Agent config
PATCH  /api/v1/config/agents/{agentId} # Update agent config
POST   /api/v1/config/deploy           # Deploy to agents
```

### 3.9 Users
```http
GET    /api/v1/users                   # List users
GET    /api/v1/users/{userId}          # User profile
PATCH  /api/v1/users/{userId}          # Update user
```

### 3.10 Audit & Export
```http
GET    /api/v1/auditlog                # Audit log (with filters)
POST   /api/v1/auditlog/export         # Export audit log
GET    /api/v1/compliance/report       # Compliance report
```

---

## 4. SignalR Hubs

### 4.1 Agent Hub (`/hubs/agent`)
**Client → Server:** (Agent only)
- `Register(agentId)` - Register connection
- `Heartbeat()` - Keep alive

**Server → Client:** (Agent only)
- `ConfigUpdate(update)` - Configuration change
- `PauseCommand(command)` - Pause/resume command
- `ConsentRequest(request)` - Request consent
- `DiagnosticCommand(command)` - Diagnostic request

### 4.2 Admin Hub (`/hubs/admin`)
**Client → Server:** (Admin dashboard)
- `SubscribeAgents(request)` - Subscribe to agent updates
- `SubscribeAgentDetails(agentId)` - Subscribe to agent detail stream
- `UnsubscribeAgentDetails(agentId)` - Unsubscribe

**Server → Client:** (Admin dashboard)
- `AgentStatusUpdate(agent)` - Real-time agent status
- `ScreenshotReceived(screenshot)` - New screenshot
- `ActivityReceived(activity)` - New activity sample
- `DlpEventReceived(event)` - New DLP event
- `PauseEventReceived(event)` - Pause/resume event
- `AlertReceived(alert)` - New alert

---

## 5. Data Models

### 5.1 Agent
```json
{
  "agentId": "string",
  "deviceName": "string",
  "userName": "string",
  "department": "string",
  "team": "string",
  "status": "Online|Paused|Offline|Error",
  "lastHeartbeat": "2024-01-15T10:30:00Z",
  "isPaused": false,
  "health": "Healthy|Degraded|Unhealthy",
  "tags": {}
}
```

### 5.2 Screenshot
```json
{
  "id": "string",
  "agentId": "string",
  "capturedAt": "2024-01-15T10:30:00Z",
  "monitorIndex": 0,
  "width": 1920,
  "height": 1080,
  "format": "jpeg",
  "blurred": true,
  "blurRegions": [
    {"x": 100, "y": 200, "width": 300, "height": 50, "reason": "PasswordField"}
  ],
  "activeWindowTitle": "CRM - Leads",
  "activeProcessName": "chrome",
  "productivity": "Productive"
}
```

### 5.3 Activity Sample
```json
{
  "id": "string",
  "agentId": "string",
  "timestamp": "2024-01-15T10:30:00Z",
  "durationSeconds": 60,
  "processName": "chrome",
  "windowTitle": "CRM - Leads - Google Chrome",
  "domain": "crm.company.com",
  "productivity": "Productive",
  "isIdle": false,
  "idleSeconds": 0,
  "inputLevel": "High"
}
```

### 5.4 DLP Event
```json
{
  "id": "string",
  "agentId": "string",
  "timestamp": "2024-01-15T10:30:00Z",
  "type": "ClipboardPii",
  "severity": "High",
  "processName": "chrome",
  "filePath": "",
  "details": "Clipboard contains PII: Email: 3 matches",
  "metadata": {"pii_types": "Email: 3 matches", "clipboard_length": "150"},
  "blocked": false,
  "acknowledged": false
}
```

### 5.5 Pause Event
```json
{
  "id": "string",
  "agentId": "string",
  "timestamp": "2024-01-15T10:30:00Z",
  "action": "Paused",
  "reason": "Lunch break",
  "pauseDurationSeconds": 0,
  "adminNotified": true,
  "adminNotificationId": "notif-123"
}
```

### 5.6 Productivity Report
```json
{
  "startTime": "2024-01-15T00:00:00Z",
  "endTime": "2024-01-15T23:59:59Z",
  "totalAgents": 50,
  "agents": [
    {
      "agentId": "agent-001",
      "userName": "John Doe",
      "department": "Sales",
      "productivityScore": 78.5,
      "productiveSeconds": 21600,
      "distractingSeconds": 3600,
      "pauseSeconds": 1800,
      "topApps": {"chrome": 18000, "outlook": 7200},
      "topCategories": {"productive": 21600, "neutral": 7200, "distracting": 3600}
    }
  ],
  "team": {
    "avgProductivityScore": 72.3,
    "totalProductiveSeconds": 1080000,
    "totalDistractingSeconds": 180000,
    "agentsOnline": 48,
    "agentsPaused": 2,
    "productivityByDepartment": {"Sales": 78.5, "Engineering": 82.1}
  }
}
```

---

## 6. Error Handling

### 6.1 gRPC Error Codes
| Code | HTTP Equivalent | Description |
|------|-----------------|-------------|
| `OK` | 200 | Success |
| `INVALID_ARGUMENT` | 400 | Invalid request parameters |
| `UNAUTHENTICATED` | 401 | Missing/invalid token |
| `PERMISSION_DENIED` | 403 | Insufficient permissions |
| `NOT_FOUND` | 404 | Resource not found |
| `ALREADY_EXISTS` | 409 | Resource conflict |
| `INTERNAL` | 500 | Server error |
| `UNAVAILABLE` | 503 | Service unavailable |

### 6.2 REST Error Response
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid request parameters",
    "details": [
      {"field": "agentId", "message": "Agent ID is required"}
    ],
    "correlationId": "abc-123"
  }
}
```

---

## 7. Rate Limiting

| Endpoint | Limit | Window |
|----------|-------|--------|
| gRPC Streams | 100 concurrent | Per agent |
| REST API | 1000 req/min | Per user |
| Report Generation | 5 concurrent | Per org |
| Export | 10/day | Per user |

---

## 8. Versioning

| Method | Strategy |
|--------|----------|
| gRPC | Package version in proto (`v1`, `v2`) |
| REST | URL versioning (`/api/v1/`, `/api/v2/`) |
| SignalR | Hub protocol version in connection handshake |
| Protobuf | Field numbers never reused, only added |

---

## 9. SDKs & Samples

| Language | Package | Repository |
|----------|---------|------------|
| C# | `EmployeeMonitoring.Client` | NuGet |
| TypeScript | `@employeemonitoring/client` | npm |
| Python | `employeemonitoring-client` | PyPI |

**Sample: C# Agent Registration**
```csharp
var channel = GrpcChannel.ForAddress("https://grpc.monitoring.company.com", 
    new GrpcChannelOptions { Credentials = ChannelCredentials.SecureSsl });

var client = new AgentService.AgentServiceClient(channel);
var call = client.Connect();

await call.RequestStream.WriteAsync(new AgentMessage
{
    AgentId = agentId,
    Registration = new AgentRegistration { ... }
});
```

---

*For the complete protobuf definitions, see `src/Contracts/Protos/agent.proto` and `api.proto`.*