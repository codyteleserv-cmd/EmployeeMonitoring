# Employee Monitoring Platform

> **Transparent, Consensual, Auditable Employee Monitoring for Data Protection & Productivity Optimization**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Build](https://github.com/yourorg/employee-monitoring/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/yourorg/employee-monitoring/actions)
[![Security](https://img.shields.io/badge/Security-Audited-blue.svg)](docs/legal/DPIA.md)

---

## 🎯 Overview

The Employee Monitoring Platform is a **production-ready, enterprise-grade** solution for legitimate employee monitoring with focus on:

| Capability | Description |
|------------|-------------|
| **🛡️ Data Loss Prevention (DLP)** | File audit, clipboard PII detection, CRM export monitoring, blocked uploads |
| **📊 Productivity Analytics** | Activity tracking, categorization (productive/neutral/distracting), idle detection |
| **📸 Screenshot Capture** | Periodic capture with smart blur (passwords, PII, credit cards), multi-monitor support |
| **⏸️ Pause/Resume Control** | User-controlled pause with daily limits, admin notification, force-resume |
| **📋 Compliance & Audit** | Immutable audit logs, consent management, DPIA-ready, GDPR/CCPA compliant |
| **🔔 Real-time Alerts** | Teams/Slack/Email notifications for DLP, offline agents, pause limits |

---

## 🏗️ Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   AGENT     │────▶│    API      │◀───│  DASHBOARD  │
│  (Windows)  │     │  (.NET 8)   │     │  (Blazor)   │
└─────────────┘     └──────┬──────┘     └─────────────┘
                           │
                    ┌──────┴──────┐
                    │ INFRASTRUCTURE │
                    │ • AKS        │
                    │ • PostgreSQL │
                    │ • Redis      │
                    │ • Key Vault  │
                    └──────────────┘
```

**Key Technologies:**
- **Agent:** .NET 8, Windows Forms, gRPC, SignalR, System.Drawing
- **API:** ASP.NET Core 8, gRPC, SignalR, EF Core, MediatR
- **Dashboard:** Blazor WebAssembly, MudBlazor, Chart.js
- **Infrastructure:** AKS, Azure PostgreSQL, Redis, Key Vault, Helm

---

## ✨ Features

### 🛡️ Data Loss Prevention
- **File Audit:** Monitor defined business paths (CRM exports, lead databases)
- **Clipboard PII Detection:** Regex-based detection (email, phone, SSN, credit cards)
- **CRM Export Monitoring:** Detect bulk exports from Salesforce/HubSpot
- **Blocked Uploads:** Prevent uploads to personal cloud (Dropbox, Google Drive, etc.)

### 📊 Productivity Analytics
- **Foreground Window Tracking:** Process name, window title, domain
- **Smart Categorization:** Configurable productive/neutral/distracting rules
- **Idle Detection:** Input activity level (no keystroke logging!)
- **Team Aggregates:** Privacy-preserving team-level dashboards

### 📸 Screenshot Capture
- **Multi-Monitor Support:** Captures all screens
- **Smart Blur:** Automatic detection of passwords, credit cards, SSN, custom PII
- **Configurable Quality/Interval:** Balance storage vs detail
- **Thumbnail Preview:** Fast dashboard loading

### ⏸️ Pause/Resume Control
- **User-Initiated:** Transparent pause with reason (daily limit configurable)
- **Admin Notification:** Real-time Teams/Slack/Email alerts
- **Force Resume:** Admin override with audit trail
- **Auto-Resume:** After daily limit exceeded

### 📋 Compliance & Governance
- **Explicit Consent:** Per-module, versioned, renewable, withdrawable
- **Transparency Dashboard:** Users see exactly what's collected
- **Immutable Audit Logs:** Separate database, tamper-evident
- **Data Retention:** Automated purge (configurable per data type)
- **DPIA Included:** Complete Data Protection Impact Assessment

---

## 🚀 Quick Start

### Prerequisites
- .NET 8 SDK
- Docker & Docker Compose
- Azure CLI (for cloud deployment)
- GitHub account (for CI/CD)

### Local Development

```bash
# Clone repository
git clone https://github.com/yourorg/employee-monitoring.git
cd employee-monitoring

# Start infrastructure
docker-compose -f infra/docker/docker-compose.yml up -d

# Build solution
dotnet build EmployeeMonitoring.sln

# Run tests
dotnet test EmployeeMonitoring.sln

# Run API locally
cd src/Api
dotnet run

# Run Dashboard locally
cd src/Dashboard
dotnet run

# Build Agent (Windows only)
cd src/Agent
dotnet publish -c Release -r win-x64 --self-contained true
```

### Configuration

Key settings in `src/Agent/appsettings.json` and `src/Api/appsettings.json`:

```json
{
  "Agent": {
    "GrpcEndpoint": "https://localhost:5001",
    "HeartbeatIntervalSeconds": 30
  },
  "Screenshot": {
    "Enabled": true,
    "IntervalSeconds": 600,
    "SmartBlurEnabled": true
  },
  "Activity": {
    "Enabled": true,
    "SampleIntervalSeconds": 60
  },
  "Dlp": {
    "Enabled": true,
    "MonitoredPaths": ["C:\\CRM\\Exports"]
  },
  "Privacy": {
    "AllowUserPause": true,
    "MaxPauseMinutesPerDay": 60,
    "NotifyAdminOnPause": true
  }
}
```

---

## 📦 Deployment

### Azure (Production)

```bash
# 1. Provision infrastructure
cd infra/terraform
terraform init
terraform apply -var="ssh_public_key=..." -var="smtp_password=..."

# 2. Deploy application
cd infra/helm
helm upgrade --install employee-monitoring ./employee-monitoring \
  --namespace monitoring \
  --create-namespace \
  --set config.database.host=<pg-fqdn> \
  --set config.redis.host=<redis-fqdn> \
  --set config.jwt.signingKey=<key> \
  --wait --timeout 10m
```

### Docker Compose (Staging)

```bash
docker-compose -f infra/docker/docker-compose.yml -f infra/docker/docker-compose.staging.yml up -d
```

---

## 📚 Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture/Architecture.md) | System design, data flows, security |
| [API Reference](docs/api/API.md) | gRPC, REST, SignalR, protobuf schemas |
| [DPIA](docs/legal/DPIA.md) | Data Protection Impact Assessment |
| [Monitoring Policy](docs/legal/MonitoringPolicy.md) | Employee-facing policy document |
| [Deployment Guide](docs/deployment/DeploymentGuide.md) | Step-by-step deployment |

---

## 🔒 Security

| Control | Implementation |
|---------|----------------|
| **Encryption at Rest** | Azure TDE (AES-256) |
| **Encryption in Transit** | TLS 1.3 everywhere, mTLS for gRPC |
| **Authentication** | Azure AD / OIDC + JWT |
| **Authorization** | RBAC (Admin/Security/HR/TeamLead/Employee) |
| **Secrets** | Azure Key Vault (HSM-backed) |
| **Audit Logging** | Immutable, separate DB, tamper-evident |
| **Penetration Testing** | Annual + after major changes |

**Report vulnerabilities:** security@yourcompany.com

---

## 🧪 Testing

```bash
# Unit tests
dotnet test --filter "Category=Unit"

# Integration tests
dotnet test --filter "Category=Integration"

# Security scan
trivy fs .

# Dependency check
dotnet tool restore && dotnet dependency-check
```

---

## 📋 Compliance

| Standard | Status |
|----------|--------|
| GDPR (EU) | ✅ DPIA completed, consent, rights |
| CCPA/CPRA (CA) | ✅ Disclosure, opt-out, deletion |
| SOC 2 Type II | 🔄 Controls mapped |
| ISO 27001 | 🔄 Annex A mapped |
| HIPAA | ⚠️ Requires BAA + config |

---

## 🤝 Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open Pull Request

**Code Standards:**
- .NET 8, nullable enabled, implicit usings
- `dotnet format` before commit
- Tests required for new features
- Security review for data-handling changes

---

## 📄 License

MIT License - see [LICENSE](LICENSE) for details.

---

## ⚠️ Important Legal Notice

**This software is designed for LEGITIMATE, CONSENSUAL employee monitoring in compliance with applicable privacy laws.**

**DO NOT use this software for:**
- Covert surveillance
- Keystroke logging
- Webcam/microphone access
- Personal device monitoring
- Any purpose violating privacy laws

**Before deployment:**
1. Complete DPIA (included in `docs/legal/DPIA.md`)
2. Obtain works council/union approval (where required)
3. Deploy Monitoring Policy (`docs/legal/MonitoringPolicy.md`)
4. Obtain explicit employee consent
4. Configure privacy controls (blur, work hours, pause limits)

**The authors accept no liability for misuse of this software.**

---

## 🙏 Acknowledgments

- [MudBlazor](https://mudblazor.com/) - Beautiful Blazor components
- [gRPC](https://grpc.io/) - High-performance RPC
- [SignalR](https://dotnet.microsoft.com/apps/aspnet/signalr) - Real-time communication
- [Serilog](https://serilog.net/) - Structured logging

---

**Built with ❤️ for transparent, ethical workplace monitoring.**