# Deployment Guide

**Version:** 1.0  
**Platform:** Employee Monitoring Platform  
**Target:** Azure (Production), Docker Compose (Staging/Development)

---

## 1. Prerequisites

### 1.1 Required Tools
| Tool | Version | Purpose |
|------|---------|---------|
| Azure CLI | 2.50+ | Azure resource management |
| Terraform | 1.6+ | Infrastructure as Code |
| Helm | 3.12+ | Kubernetes package manager |
| kubectl | 1.28+ | Kubernetes CLI |
| Docker | 24+ | Container builds |
| .NET SDK | 8.0 | Build & publish |

### 1.2 Required Permissions
- **Azure:** Contributor + User Access Administrator on subscription
- **Azure AD:** Application Administrator (for App Registration)
- **GitHub:** Admin on repository (for Actions secrets)

---

## 2. Infrastructure Provisioning (Terraform)

### 2.1 Configure Variables

Create `infra/terraform/terraform.tfvars`:

```hcl
resource_group_name = "rg-employeemonitoring-prod"
location            = "East US 2"
dns_prefix          = "empmon-prod"
kubernetes_version  = "1.28.5"
ssh_public_key      = "ssh-rsa AAAAB3NzaC1yc2E..."
smtp_password       = "your-smtp-password"

tags = {
  Environment = "production"
  Project     = "employee-monitoring"
  ManagedBy   = "terraform"
  CostCenter  = "IT-SEC-001"
}
```

### 2.2 Deploy Infrastructure

```bash
cd infra/terraform

# Initialize
terraform init

# Plan (review changes)
terraform plan -out=tfplan

# Apply
terraform apply tfplan
```

**Expected outputs:**
- `kube_config` (sensitive)
- `postgres_main_fqdn`
- `postgres_audit_fqdn`
- `redis_hostname`
- `key_vault_uri`

### 2.3 Configure AKS Access

```bash
# Get credentials
az aks get-credentials \
  --resource-group $(terraform output -raw resource_group_name) \
  --name $(terraform output -raw aks_cluster_name) \
  --overwrite-existing

# Verify
kubectl get nodes
```

---

## 3. Secrets Management

### 3.1 Store Secrets in Key Vault

```bash
# Get Key Vault name
KV_URI=$(terraform output -raw key_vault_uri)

# Store secrets (run from secure environment)
az keyvault secret set --vault-name $KV_URI --name "db-password" --value "$(terraform output -raw db_password)"
az keyvault secret set --vault-name $KV_URI --name "audit-db-password" --value "$(terraform output -raw audit_db_password)"
az keyvault secret set --vault-name $KV_URI --name "redis-password" --value "$(terraform output -raw redis_password)"
az keyvault secret set --vault-name $KV_URI --name "jwt-signing-key" --value "$(terraform output -raw jwt_signing_key)"
az keyvault secret set --vault-name $KV_URI --name "oidc-client-secret" --value "<your-oidc-secret>"
az keyvault secret set --vault-name $KV_URI --name "smtp-password" --value "<your-smtp-password>"
```

### 3.2 Configure GitHub Actions Secrets

Go to GitHub Repository → Settings → Secrets and Variables → Actions → New repository secret:

| Secret Name | Value |
|-------------|-------|
| `AZURE_CREDENTIALS` | Service Principal JSON (from `az ad sp create-for-rbac`) |
| `STAGING_KUBECONFIG` | Base64 encoded kubeconfig for staging |
| `PRODUCTION_KUBECONFIG` | Base64 encoded kubeconfig for production |
| `STAGING_DB_PASSWORD` | From Key Vault |
| `PROD_DB_PASSWORD` | From Key Vault |
| `STAGING_AUDIT_DB_PASSWORD` | From Key Vault |
| `PROD_AUDIT_DB_PASSWORD` | From Key Vault |
| `STAGING_REDIS_PASSWORD` | From Key Vault |
| `PROD_REDIS_PASSWORD` | From Key Vault |
| `STAGING_JWT_KEY` | From Key Vault |
| `PROD_JWT_KEY` | From Key Vault |
| `STAGING_OIDC_SECRET` | From Key Vault |
| `PROD_OIDC_SECRET` | From Key Vault |
| `STAGING_SMTP_PASSWORD` | From Key Vault |
| `PROD_SMTP_PASSWORD` | From Key Vault |
| `SONAR_TOKEN` | SonarQube token |
| `SONAR_HOST_URL` | https://sonarcloud.io |

---

## 4. Application Deployment (Helm)

### 4.1 Create Values File

Create `infra/helm/employee-monitoring/values.prod.yaml`:

```yaml
replicaCount: 3

image:
  api:
    repository: ghcr.io/yourorg/employee-monitoring/api
    tag: "v1.0.0"
  dashboard:
    repository: ghcr.io/yourorg/employee-monitoring/dashboard
    tag: "v1.0.0"

ingress:
  enabled: true
  className: "nginx"
  annotations:
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
  hosts:
    - host: monitoring.yourcompany.com
      paths:
        - path: /
          pathType: Prefix
  tls:
    - secretName: employee-monitoring-tls
      hosts:
        - monitoring.yourcompany.com

config:
  database:
    host: "<postgres-main-fqdn>"
    port: 5432
    name: employeemonitoring
    username: "postgres"
    password: ""  # From Key Vault
    sslMode: "require"
  auditDatabase:
    host: "<postgres-audit-fqdn>"
    port: 5432
    name: employeemonitoring_audit
    username: "postgres"
    password: ""  # From Key Vault
    sslMode: "require"
  redis:
    host: "<redis-hostname>"
    port: 6379
    password: ""  # From Key Vault
    ssl: true
  jwt:
    issuer: "EmployeeMonitoring.Api"
    audience: "EmployeeMonitoring.Client"
    signingKey: ""  # From Key Vault
    expiryMinutes: 60
  oidc:
    authority: "https://login.microsoftonline.com/<tenant-id>/v2.0"
    clientId: "<your-client-id>"
    clientSecret: ""  # From Key Vault
    callbackPath: "/signin-oidc"
  notifications:
    email:
      smtpHost: "smtp.office365.com"
      smtpPort: 587
      username: "alerts@yourcompany.com"
      password: ""  # From Key Vault
      fromAddress: "alerts@yourcompany.com"
    teams:
      webhookUrl: "https://outlook.office.com/webhook/..."
    slack:
      webhookUrl: "https://hooks.slack.com/services/..."

autoscaling:
  enabled: true
  minReplicas: 3
  maxReplicas: 20

resources:
  api:
    limits:
      cpu: 1000m
      memory: 1Gi
    requests:
      cpu: 500m
      memory: 512Mi
```

### 4.2 Deploy

```bash
cd infra/helm

# Add Helm repo (if using external chart repo)
helm repo add employee-monitoring https://charts.yourcompany.com
helm repo update

# Deploy
helm upgrade --install employee-monitoring ./employee-monitoring \
  --namespace monitoring \
  --create-namespace \
  --values values.prod.yaml \
  --wait --timeout 10m

# Verify
kubectl get pods -n monitoring
kubectl get ingress -n monitoring
```

### 4.3 Verify Deployment

```bash
# Check pod status
kubectl get pods -n monitoring -l app.kubernetes.io/instance=employee-monitoring

# Check logs
kubectl logs -n monitoring -l app.kubernetes.io/component=api --tail=100

# Test health endpoints
curl -f https://monitoring.yourcompany.com/health
curl -f https://monitoring.yourcompany.com/health/ready

# Test gRPC
grpcurl -plaintext monitoring.yourcompany.com:443 list
```

---

## 5. Agent Deployment

### 5.1 Build Signed Agent

```bash
# On Windows build machine
cd src/Agent

# Build with signing (requires code signing certificate)
dotnet publish EmployeeMonitoring.Agent.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:SignAssembly=true \
  -p:AssemblyOriginatorKeyFile=../keys/strongname.snk \
  -o ../../artifacts/agent/v1.0.0

# Verify signature
signtool verify /pa ../../artifacts/agent/v1.0.0/EmployeeMonitoring.Agent.exe
```

### 5.2 Distribute Agent

**Options:**
1. **Intune/SCCM:** Deploy as Win32 app with detection rules
2. **Group Policy:** Deploy via startup script
3. **Manual:** Download from dashboard → Settings → Download Agent

**Installation Command (silent):**
```cmd
EmployeeMonitoring.Agent.exe --install --register --server https://monitoring.yourcompany.com
```

**Uninstall:**
```cmd
EmployeeMonitoring.Agent.exe --uninstall
```

### 5.3 Agent Configuration (via API)

Agents receive configuration automatically on connect. To update:

1. Go to Dashboard → Configuration
2. Modify global or per-group settings
3. Click "Deploy Configuration"
4. Agents receive update via SignalR/gRPC within 30 seconds

---

## 6. Post-Deployment Validation

### 6.1 Health Checks

```bash
# API Health
curl -f https://monitoring.yourcompany.com/health
curl -f https://monitoring.yourcompany.com/health/ready
curl -f https://monitoring.yourcompany.com/health/live

# Database
kubectl exec -n monitoring deploy/employee-monitoring-api -- \
  dotnet ef dbcontext info

# Redis
kubectl exec -n monitoring deploy/employee-monitoring-api -- \
  redis-cli -h $REDIS_HOST -a $REDIS_PASSWORD ping
```

### 6.2 Agent Registration Test

1. Install agent on test machine
2. Verify tray icon appears (green = running)
3. Check dashboard: Agent should appear "Online"
4. Test pause/resume from tray
5. Verify screenshot appears in dashboard within 10 minutes
6. Test DLP: Copy email to clipboard → verify alert

### 6.3 Dashboard Access Test

1. Navigate to `https://monitoring.yourcompany.com`
2. Authenticate with Azure AD
3. Verify role-based access:
   - Admin: Full access
   - Security: DLP, agents, alerts
   - Team Lead: Team aggregates only
   - Employee: Own data only

---

## 7. Monitoring & Alerting Setup

### 7.1 Prometheus/Grafana (Optional)

```bash
# Add Prometheus Operator
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm install prometheus prometheus-community/kube-prometheus-stack \
  -n monitoring \
  --set grafana.enabled=true \
  --set prometheus.prometheusSpec.serviceMonitorSelectorNilUsesHelmValues=false

# Import dashboards
# - Employee Monitoring Overview (JSON in docs/grafana/)
# - Agent Health
# - DLP Events
# - Productivity Trends
```

### 7.2 Alert Rules (PrometheusRule)

```yaml
apiVersion: monitoring.coreos.com/v1
kind: PrometheusRule
metadata:
  name: employee-monitoring-alerts
  namespace: monitoring
spec:
  groups:
    - name: employee-monitoring
      rules:
        - alert: AgentOffline
          expr: agent_online < 1
          for: 5m
          labels:
            severity: warning
          annotations:
            summary: "Agent {{ $labels.agent_id }} offline"
        - alert: DlpCriticalAlert
          expr: increase(dlp_events_total{severity="Critical"}[5m]) > 0
          labels:
            severity: critical
          annotations:
            summary: "Critical DLP event from {{ $labels.agent_id }}"
        - alert: PauseLimitExceeded
          expr: agent_pause_minutes_today / agent_max_pause_minutes > 0.9
          labels:
            severity: warning
          annotations:
            summary: "Agent {{ $labels.agent_id }} near pause limit"
```

---

## 8. Backup & Disaster Recovery

### 8.1 Automated Backups (Azure)

```bash
# PostgreSQL - already configured with geo-redundant backup (30 days PITR)
# Verify
az postgres flexible-server show --name <pg-name> --resource-group <rg> \
  --query "backup.retentionDays"

# Key Vault - soft delete + purge protection enabled by default
# Verify
az keyvault show --name <kv-name> --query "properties.enableSoftDelete"
```

### 8.2 Manual Backup (Before Major Changes)

```bash
# Database dump
pg_dump -h <pg-fqdn> -U postgres -d employeemonitoring \
  -Fc -f backup-$(date +%Y%m%d).dump

# Audit DB
pg_dump -h <pg-audit-fqdn> -U postgres -d employeemonitoring_audit \
  -Fc -f audit-backup-$(date +%Y%m%d).dump

# Store in secure blob storage
az storage blob upload --container backups --file backup-$(date +%Y%m%d).dump
```

### 8.3 Restore Procedure

```bash
# 1. Restore database
pg_restore -h <new-pg-fqdn> -U postgres -d employeemonitoring \
  -c -v backup-20240115.dump

# 2. Redeploy application (Helm will reconnect)
helm upgrade --install employee-monitoring ./employee-monitoring \
  -n monitoring \
  --set config.database.host=<new-pg-fqdn> \
  --wait

# 3. Verify agents reconnect (they will on next heartbeat)
```

---

## 9. Rollback Procedure

```bash
# 1. Rollback Helm release
helm rollback employee-monitoring <revision> -n monitoring

# 2. Or redeploy previous image tag
helm upgrade employee-monitoring ./employee-monitoring \
  -n monitoring \
  --set image.api.tag=v1.0.0 \
  --set image.dashboard.tag=v1.0.0 \
  --wait

# 3. Verify
kubectl rollout status deploy/employee-monitoring-api -n monitoring
```

---

## 10. Troubleshooting

### 10.1 Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Agent shows "Offline" | Network/firewall | Check port 443/8081 to API; verify mTLS cert |
| No screenshots | Smart blur error | Check agent logs: `%PROGRAMDATA%\EmployeeMonitoring\logs\` |
| DLP not triggering | Path not monitored | Verify `MonitoredPaths` in config; check FileSystemWatcher |
| Dashboard shows no data | SignalR disconnected | Check browser console; verify WebSocket upgrade |
| Agent won't install | Unsigned/cert issue | Verify code signing cert; check SmartScreen |

### 10.2 Log Locations

| Component | Location |
|-----------|----------|
| Agent (Windows) | `%PROGRAMDATA%\EmployeeMonitoring\logs\agent-<date>.log` |
| API (AKS) | `kubectl logs -n monitoring -l app.kubernetes.io/component=api` |
| Dashboard | Browser DevTools Console |
| Infrastructure | Azure Monitor / Log Analytics |

### 10.3 Debug Mode

```bash
# Agent
EmployeeMonitoring.Agent.exe --debug --console

# API
kubectl set env deploy/employee-monitoring-api ASPNETCORE_ENVIRONMENT=Development -n monitoring
```

---

## 11. Maintenance

### 11.1 Regular Tasks

| Task | Frequency | Command |
|------|-----------|---------|
| Certificate renewal | 90 days | `cert-manager` auto-renew |
| Database maintenance | Weekly | `az postgres flexible-server execute -n <name> -c "VACUUM ANALYZE"` |
| Log cleanup | Daily | Automatic (retention policies) |
| Agent version check | Weekly | Dashboard → Agents → check version column |

### 11.2 Updates

```bash
# 1. Build new images
git tag v1.0.1
git push origin v1.0.1

# 2. CI/CD builds and pushes images

# 3. Deploy
helm upgrade employee-monitoring ./employee-monitoring \
  -n monitoring \
  --set image.api.tag=v1.0.1 \
  --set image.dashboard.tag=v1.0.1 \
  --wait

# 4. Agents auto-update on next check-in (or force via dashboard)
```

---

## 12. Support Contacts

| Issue Type | Contact | SLA |
|------------|---------|-----|
| Production Down | oncall@yourcompany.com | 15 min |
| Security Incident | security@yourcompany.com | 15 min |
| Data/Privacy | dpo@yourcompany.com | 1 hour |
| General Support | support@yourcompany.com | 4 hours |

---

*This deployment guide should be reviewed and updated with each major release.*