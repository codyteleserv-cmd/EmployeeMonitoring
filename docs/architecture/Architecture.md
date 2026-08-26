# Employee Monitoring Platform - Architecture Documentation

**Version:** 1.0  
**Last Updated:** 2024-01-15

---

## 1. System Overview

The Employee Monitoring Platform is a **cloud-native, microservices-based** system designed for transparent, consensual, and auditable employee monitoring. It consists of three primary components:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        EMPLOYEE MONITORING PLATFORM                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                   │
│  │   AGENT      │    │   API        │    │  DASHBOARD   │                   │
│  │  (Windows)   │◄──▶│  (.NET 8)    │◄──▶│  (Blazor)    │                   │
│  │              │    │              │    │              │                   │
│  │ • Screenshots│    │ • gRPC       │    │ • Real-time  │                   │
│  │ • Activity   │    │ • SignalR    │    │ • Charts     │                   │
│  │ • DLP        │    │ • REST       │    │ • Tables     │                   │
│  │ • Pause/Resume│   │ • AuthZ      │    │ • Admin UI   │                   │
│  │ • Consent UI │    │ • Audit      │    │ • Export     │                   │
│  └──────────────┘    └──────┬───────┘    └──────────────┘                   │
│                             │                                                │
│                    ┌────────┴────────┐                                       │
│                    │  INFRASTRUCTURE  │                                       │
│                    │                 │                                       │
│                    │ • PostgreSQL    │                                       │
│                    │ • Redis         │                                       │
│                    │ • Key Vault     │                                       │
│                    │ • AKS           │                                       │
│                    └─────────────────┘                                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Component Details

### 2.1 Agent (Windows Service + Tray UI)

**Technology:** .NET 8, Windows Forms, gRPC, SignalR  
**Deployment:** Signed single-file executable, Windows Service + Interactive Tray UI

**Modules:**
| Module | Technology | Key Features |
|--------|------------|--------------|
| **Screenshot Service** | System.Drawing, Windows API | Multi-monitor, smart blur (password/PII), JPEG compression, batching |
| **Activity Service** | Windows API (GetForegroundWindow, GetLastInputInfo) | Foreground window, idle detection, input level (no keystrokes), productivity categorization |
| **DLP Service** | FileSystemWatcher, Clipboard API, Regex | File audit, clipboard PII, CRM export detection, path monitoring |
| **Pause Manager** | In-memory + persisted JSON | User pause/resume, daily limits, admin notification, force resume |
| **Consent Manager** | JSON persistence + UI | Per-module consent, versioning, renewal, withdrawal |
| **Communication** | gRPC (bidirectional streaming), SignalR | mTLS, auto-reconnect, message batching, offline queue |

**Security:**
- Signed executable (Authenticode)
- mTLS to API (certificate pinning)
- No admin rights required (runs as user)
- Auto-update via signed packages
- Transparent tray icon (always visible)

### 2.2 API (ASP.NET Core 8)

**Technology:** ASP.NET Core 8, gRPC, SignalR, Entity Framework Core, MediatR

**Services:**
| Service | Responsibility |
|---------|----------------|
| **Agent gRPC Service** | Bidirectional streaming for agent communication |
| **Admin gRPC Service** | Admin dashboard operations (streaming + unary) |
| **Agent Hub (SignalR)** | Real-time agent → admin updates |
| **Admin Hub (SignalR)** | Real-time admin dashboard updates |
| **Agent Repository** | Agent CRUD, status, heartbeats |
| **Screenshot Repository** | Screenshot storage, retrieval, thumbnail generation |
| **Activity Repository** | Activity samples, aggregation, productivity summaries |
| **DLP Repository** | DLP events, statistics, acknowledgment |
| **Pause Repository** | Pause events, statistics, daily limits |
| **Audit Service** | Immutable audit logging (separate DB) |
| **Notification Service** | Email, Teams, Slack, Webhook alerts |
| **Report Service** | Scheduled report generation (PDF/CSV/Excel) |

**Authentication & Authorization:**
- JWT Bearer tokens (Azure AD / OIDC)
- Role-based access control (RBAC)
- Policies: AdminOnly, SecurityOnly, TeamLeadOrAbove, HROrAbove

**Data Protection:**
- TLS 1.3 everywhere
- mTLS for gRPC
- Encryption at rest (Azure TDE)
- Separate audit database (immutable logs)

### 2.3 Dashboard (Blazor WebAssembly)

**Technology:** Blazor WASM, MudBlazor, Chart.js, SignalR Client

**Pages:**
| Page | Features |
|------|----------|
| **Dashboard** | Real-time stats, agent grid, productivity/DLP charts |
| **Agents** | Filterable grid, agent detail, pause/resume actions |
| **Screenshots** | Filterable gallery, thumbnail preview, blur region details |
| **Activity** | Time-series charts, productivity breakdown, export |
| **DLP Events** | Filterable grid, severity, acknowledgment workflow |
| **Pauses** | Timeline, statistics, reason breakdown, admin actions |
| **Audit Log** | Filterable, exportable, compliance reporting |
| **Users/Devices** | Management, roles, consent status |
| **Configuration** | Global + per-agent/department/team config deployment |
| **Alerts** | Real-time, acknowledgment, rule management |
| **Reports** | Scheduled, ad-hoc, multiple formats |

**Real-time:** SignalR for live agent status, screenshots, DLP alerts, pause events

### 2.4 Infrastructure

| Component | Technology | Configuration |
|-----------|------------|---------------|
| **Container Orchestration** | Azure Kubernetes Service (AKS) | System + Workload node pools |
| **Primary Database** | Azure PostgreSQL Flexible Server | Zone-redundant HA, geo-backup |
| **Audit Database** | Azure PostgreSQL Flexible Server | Separate server, 90-day retention |
| **Cache/Backplane** | Azure Redis Cache | Standard tier, TLS, maxmemory-lru |
| **Secrets** | Azure Key Vault | Soft delete, purge protection |
| **Monitoring** | Azure Monitor, Log Analytics | Prometheus/Grafana integration |
| **CI/CD** | GitHub Actions | Build, test, security scan, deploy |
| **DNS/SSL** | Azure Front Door + App Gateway | WAF, custom domains |

---

## 3. Data Flow

### 3.1 Agent → API (Continuous)

```
Agent                          API
  │                              │
  ├─ gRPC Connect ─────────────▶│
  │  (AgentRegistration)        │
  │◀─ ConfigUpdate ────────────┤
  │                              │
  ├─ ScreenshotBatch ─────────▶│
  │  (batched, every 30s)       │
  │                              │
  ├─ ActivityBatch ───────────▶│
  │  (batched, every 10s)       │
  │                              │
  ├─ PauseEvent ──────────────▶│
  │  (immediate)                │
  │                              │
  ├─ DlpEvent ────────────────▶│
  │  (immediate)                │
  │                              │
  ├─ Heartbeat ───────────────▶│
  │  (every 30s)                │
  │                              │
  └─ SignalR Connect ────────▶│
     (Admin Hub)                │
```

### 3.2 API Processing Pipeline

```
┌─────────────────────────────────────────────────────────────┐
│                    MESSAGE PROCESSING                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  gRPC Stream ──▶ MessageQueue ──▶ Background Workers       │
│                      │                                        │
│                      ├─▶ ScreenshotProcessor ──▶ DB + Blob  │
│                      ├─▶ ActivityProcessor  ──▶ DB + Agg    │
│                      ├─▶ DlpProcessor       ──▶ DB + Alert  │
│                      ├─▶ PauseProcessor     ──▶ DB + Notify │
│                      └─▶ HeartbeatProcessor ──▶ Status      │
│                                                              │
│  SignalR Hub ◀── Real-time Updates (Admin Dashboard)       │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### 3.3 Admin Dashboard Data Flow

```
Admin Dashboard                    API
    │                                │
    ├─ gRPC WatchAgents ──────────▶│ (streaming)
    │◀─ AgentStatusUpdate ────────┤
    │                                │
    ├─ gRPC GetScreenshots ───────▶│ (streaming)
    │◀─ ScreenshotData ────────────┤
    │                                │
    ├─ SignalR Connect ───────────▶│
    │◀─ Real-time Updates ────────┤
    │                                │
    └─ REST Export/Reports ───────▶│
```

---

## 4. Security Architecture

### 4.1 Network Security
```
Internet ──▶ Azure Front Door (WAF) ──▶ App Gateway ──▶ AKS Private Cluster
                                              │
                                              ├─▶ API (Internal Load Balancer)
                                              ├─▶ Dashboard (Public Load Balancer)
                                              └─▶ Private Endpoints (DB, Redis, KV)
```

### 4.2 Data Encryption
| State | Method |
|-------|--------|
| **At Rest (DB)** | Azure Transparent Data Encryption (AES-256) |
| **At Rest (Blobs)** | Azure Storage Service Encryption (AES-256) |
| **In Transit (gRPC)** | TLS 1.3 + mTLS (certificate pinning) |
| **In Transit (SignalR)** | TLS 1.3 (WSS) |
| **In Transit (Dashboard)** | TLS 1.3 (HTTPS) |
| **Secrets** | Azure Key Vault (HSM-backed) |

### 4.3 Identity & Access
```
Azure AD (OIDC) ──▶ JWT Token ──▶ API (JWT Validation)
                      │
                      ├─▶ Roles: admin, security, hr, team_lead, employee
                      ├─▶ Policies: AdminOnly, SecurityOnly, TeamLeadOrAbove, HROrAbove
                      └─▶ Claims: sub, role, department, team, agent_ids
```

---

## 5. Deployment Architecture

### 5.1 Environments
| Environment | Purpose | Infrastructure |
|-------------|---------|----------------|
| **Development** | Developer testing | Local Docker Compose / AKS Dev |
| **Staging** | Integration testing, UAT | AKS Staging (subset of prod) |
| **Production** | Live | AKS Prod (multi-zone, HA) |

### 5.2 Deployment Pipeline
```
Git Push ──▶ GitHub Actions
    │
    ├─▶ Build & Test (Linux + Windows)
    ├─▶ Security Scan (Trivy, Dependency Check)
    ├─▶ Code Quality (SonarQube)
    ├─▶ Docker Build & Push (GHCR)
    ├─▶ Deploy Staging (develop branch)
    ├─▶ Smoke Tests
    ├─▶ Deploy Production (tags v*)
    └─▶ Release Notes + Agent Artifact
```

### 5.3 Helm Deployment
```yaml
# Key Helm values for production
replicaCount: 3
autoscaling:
  enabled: true
  minReplicas: 3
  maxReplicas: 20
resources:
  api:
    limits: {cpu: 1000m, memory: 1Gi}
    requests: {cpu: 500m, memory: 512Mi}
```

---

## 6. Observability

### 6.1 Metrics (Prometheus)
| Metric | Type | Description |
|--------|------|-------------|
| `agent_connected_total` | Counter | Total agent connections |
| `agent_online` | Gauge | Currently online agents |
| `screenshots_captured_total` | Counter | Screenshots processed |
| `activity_samples_total` | Counter | Activity samples processed |
| `dlp_events_total` | Counter | DLP events by type/severity |
| `pause_events_total` | Counter | Pause/resume events |
| `api_request_duration_seconds` | Histogram | API latency |
| `grpc_stream_active` | Gauge | Active gRPC streams |

### 6.2 Logging (Structured JSON)
```json
{
  "timestamp": "2024-01-15T10:30:00.123Z",
  "level": "Information",
  "correlationId": "abc-123",
  "message": "Screenshot batch processed",
  "agentId": "agent-001",
  "batchSize": 5,
  "processingTimeMs": 45
}
```

### 6.3 Alerting Rules
| Alert | Condition | Severity |
|-------|-----------|----------|
| AgentOffline | No heartbeat > 5 min | Warning |
| DlpCritical | Severity=Critical | Critical |
| PauseLimitExceeded | Daily pause > 90% | Warning |
| ApiHighLatency | p99 > 2s | Warning |
| DiskSpaceLow | < 10% free | Critical |

---

## 7. Disaster Recovery

| Component | RPO | RTO | Strategy |
|-----------|-----|-----|----------|
| **Primary DB** | 5 min | 30 min | Geo-redundant HA + PITR |
| **Audit DB** | 1 hour | 1 hour | Geo-redundant + long-term retention |
| **Redis** | N/A | 15 min | Rebuild from primary DB |
| **AKS** | N/A | 30 min | Multi-zone, GitOps rebuild |
| **Agent Config** | N/A | Immediate | Pushed from API on connect |

---

## 8. Capacity Planning

| Component | Baseline | Peak | Scaling Trigger |
|-----------|----------|------|-----------------|
| **API Pods** | 3 | 20 | CPU > 70%, Memory > 80% |
| **Dashboard Pods** | 2 | 10 | CPU > 70% |
| **PostgreSQL** | 4 vCore | 16 vCore | CPU > 80%, Connections > 80% |
| **Redis** | 2.5 GB | 10 GB | Memory > 80% |
| **Agent Connections** | 1,000 | 10,000 | Horizontal pod scaling |

---

## 9. Compliance Mapping

| Requirement | Implementation |
|-------------|----------------|
| **GDPR Art. 5** (Principles) | Data minimization, purpose limitation, storage limitation |
| **GDPR Art. 6** (Lawfulness) | Documented legal bases per module |
| **GDPR Art. 7** (Consent) | Granular, informed, withdrawable |
| **GDPR Art. 12-14** (Transparency) | Dashboard, policy, DPIA |
| **GDPR Art. 15-22** (Rights) | Dashboard access, export, deletion |
| **GDPR Art. 25** (By Design) | Privacy by default, smart blur, minimization |
| **GDPR Art. 32** (Security) | Encryption, mTLS, RBAC, audit |
| **GDPR Art. 33-34** (Breach) | 72-hour notification process |
| **GDPR Art. 35** (DPIA) | Completed and reviewed |
| **SOC 2 Type II** | Controls mapped, auditable |
| **ISO 27001** | Annex A controls implemented |

---

## 10. Future Extensibility

| Planned Feature | Architecture Impact |
|-----------------|---------------------|
| **Mac/Linux Agent** | .NET 8 cross-platform, platform-specific modules |
| **Mobile Dashboard** | Blazor Hybrid / MAUI |
| **ML-based Anomaly Detection** | New gRPC service, async processing |
| **SIEM Integration** | Webhook/Event Hub sink for audit/DLP |
| **Custom Plugin Framework** | Agent module SDK, sandboxed execution |
| **Zero Trust Network Access** | Replace VPN, agent-based ZTNA |

---

*This architecture document is maintained as part of the platform's living documentation.*