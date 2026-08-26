terraform {
  required_version = ">= 1.5.0"
  
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 2.0"
    }
    helm = {
      source  = "hashicorp/helm"
      version = "~> 2.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "terraform-state-rg"
    storage_account_name = "tfstateemployeemonitoring"
    container_name       = "tfstate"
    key                  = "employee-monitoring.terraform.tfstate"
  }
}

provider "azurerm" {
  features {}
}

provider "kubernetes" {
  host                   = azurerm_kubernetes_cluster.main.kube_config.0.host
  client_certificate     = base64decode(azurerm_kubernetes_cluster.main.kube_config.0.client_certificate)
  client_key             = base64decode(azurerm_kubernetes_cluster.main.kube_config.0.client_key)
  cluster_ca_certificate = base64decode(azurerm_kubernetes_cluster.main.kube_config.0.cluster_ca_certificate)
}

provider "helm" {
  kubernetes {
    host                   = azurerm_kubernetes_cluster.main.kube_config.0.host
    client_certificate     = base64decode(azurerm_kubernetes_cluster.main.kube_config.0.client_certificate)
    client_key             = base64decode(azurerm_kubernetes_cluster.main.kube_config.0.client_key)
    cluster_ca_certificate = base64decode(azurerm_kubernetes_cluster.main.kube_config.0.cluster_ca_certificate)
  }
}

# Random passwords for secrets
resource "random_password" "db_password" {
  length  = 32
  special = false
}

resource "random_password" "audit_db_password" {
  length  = 32
  special = false
}

resource "random_password" "redis_password" {
  length  = 32
  special = false
}

resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

# Resource Group
resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

# Log Analytics Workspace
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${var.resource_group_name}-logs"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}

# AKS Cluster
resource "azurerm_kubernetes_cluster" "main" {
  name                = "${var.resource_group_name}-aks"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  dns_prefix          = var.dns_prefix
  kubernetes_version  = var.kubernetes_version

  default_node_pool {
    name       = "system"
    node_count = 2
    vm_size    = "Standard_D4s_v5"
    os_disk_size_gb = 128
    vnet_subnet_id = azurerm_subnet.aks.id
    tags = var.tags
  }

  linux_profile {
    admin_username = "azureuser"
    ssh_key {
      key_data = var.ssh_public_key
    }
  }

  network_profile {
    network_plugin     = "azure"
    network_policy     = "azure"
    service_cidr       = "10.0.0.0/16"
    dns_service_ip     = "10.0.0.10"
    docker_bridge_cidr = "172.17.0.1/16"
  }

  identity {
    type = "SystemAssigned"
  }

  azure_policy_enabled = true
  local_accounts_disabled = true

  oidc_issuer_enabled = true
  workload_identity_enabled = true

  tags = var.tags
}

# Additional node pool for workloads
resource "azurerm_kubernetes_cluster_node_pool" "workload" {
  name                  = "workload"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.main.id
  vm_size               = "Standard_D4s_v5"
  node_count            = 3
  os_disk_size_gb       = 128
  vnet_subnet_id        = azurerm_subnet.aks.id
  tags                  = var.tags
}

# Virtual Network
resource "azurerm_virtual_network" "main" {
  name                = "${var.resource_group_name}-vnet"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  address_space       = ["10.1.0.0/16"]
  tags                = var.tags
}

resource "azurerm_subnet" "aks" {
  name                 = "aks-subnet"
  resource_group_name  = azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = ["10.1.1.0/24"]
  service_endpoints    = ["Microsoft.Sql", "Microsoft.Storage", "Microsoft.KeyVault"]
}

# PostgreSQL Flexible Server (Main)
resource "azurerm_postgresql_flexible_server" "main" {
  name                = "${var.resource_group_name}-pg-main"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  administrator_login = "postgres"
  administrator_password = random_password.db_password.result
  version             = "16"
  storage_mb          = 32768
  sku_name            = "Standard_D4s_v5"
  zone                = "1"
  high_availability {
    mode = "ZoneRedundant"
    standby_availability_zone = "2"
  }
  backup {
    retention_days = 30
    geo_redundant_backup_enabled = true
  }
  network {
    delegated_subnet_id = azurerm_subnet.aks.id
  }
  tags = var.tags
}

resource "azurerm_postgresql_flexible_server_database" "main" {
  name      = "employeemonitoring"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# PostgreSQL Flexible Server (Audit)
resource "azurerm_postgresql_flexible_server" "audit" {
  name                = "${var.resource_group_name}-pg-audit"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  administrator_login = "postgres"
  administrator_password = random_password.audit_db_password.result
  version             = "16"
  storage_mb          = 32768
  sku_name            = "Standard_D2s_v5"
  zone                = "1"
  backup {
    retention_days = 90
    geo_redundant_backup_enabled = true
  }
  network {
    delegated_subnet_id = azurerm_subnet.aks.id
  }
  tags = var.tags
}

resource "azurerm_postgresql_flexible_server_database" "audit" {
  name      = "employeemonitoring_audit"
  server_id = azurerm_postgresql_flexible_server.audit.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# Redis Cache
resource "azurerm_redis_cache" "main" {
  name                = "${var.resource_group_name}-redis"
  location            = var.location
  resource_group_name = azurerm_resource_group.main.name
  capacity            = 2
  family              = "C"
  sku_name            = "Standard"
  minimum_tls_version = "1.2"
  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }
  tags = var.tags
}

# Key Vault for secrets
resource "azurerm_key_vault" "main" {
  name                        = "${var.resource_group_name}-kv"
  location                    = var.location
  resource_group_name         = azurerm_resource_group.main.name
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  sku_name                    = "standard"
  soft_delete_retention_days  = 90
  purge_protection_enabled    = true
  public_network_access_enabled = false
  tags                        = var.tags
}

# Key Vault secrets
resource "azurerm_key_vault_secret" "db_password" {
  name         = "db-password"
  value        = random_password.db_password.result
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "audit_db_password" {
  name         = "audit-db-password"
  value        = random_password.audit_db_password.result
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "redis_password" {
  name         = "redis-password"
  value        = random_password.redis_password.result
  key_vault_id = azurerm_key_vault.main.id
}

resource "azurerm_key_vault_secret" "jwt_signing_key" {
  name         = "jwt-signing-key"
  value        = random_password.jwt_signing_key.result
  key_vault_id = azurerm_key_vault.main.id
}

# Helm release for employee-monitoring
resource "helm_release" "employee_monitoring" {
  name       = "employee-monitoring"
  repository = "https://charts.employeemonitoring.com"
  chart      = "employee-monitoring"
  version    = "1.0.0"
  namespace  = "monitoring"
  create_namespace = true

  set {
    name  = "config.database.host"
    value = azurerm_postgresql_flexible_server.main.fqdn
  }
  set {
    name  = "config.database.name"
    value = "employeemonitoring"
  }
  set {
    name  = "config.database.username"
    value = "postgres"
  }
  set {
    name  = "config.database.password"
    value = random_password.db_password.result
  }
  set {
    name  = "config.auditDatabase.host"
    value = azurerm_postgresql_flexible_server.audit.fqdn
  }
  set {
    name  = "config.auditDatabase.name"
    value = "employeemonitoring_audit"
  }
  set {
    name  = "config.auditDatabase.username"
    value = "postgres"
  }
  set {
    name  = "config.auditDatabase.password"
    value = random_password.audit_db_password.result
  }
  set {
    name  = "config.redis.host"
    value = azurerm_redis_cache.main.hostname
  }
  set {
    name  = "config.redis.password"
    value = azurerm_redis_cache.main.primary_access_key
  }
  set {
    name  = "config.jwt.signingKey"
    value = random_password.jwt_signing_key.result
  }
  set {
    name  = "config.notifications.email.password"
    value = var.smtp_password
  }
}

# Outputs
output "kube_config" {
  value     = azurerm_kubernetes_cluster.main.kube_config_raw
  sensitive = true
}

output "postgres_main_fqdn" {
  value = azurerm_postgresql_flexible_server.main.fqdn
}

output "postgres_audit_fqdn" {
  value = azurerm_postgresql_flexible_server.audit.fqdn
}

output "redis_hostname" {
  value = azurerm_redis_cache.main.hostname
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}