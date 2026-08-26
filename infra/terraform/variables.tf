variable "resource_group_name" {
  description = "Name of the Azure resource group"
  type        = string
  default     = "rg-employeemonitoring-prod"
}

variable "location" {
  description = "Azure region for resources"
  type        = string
  default     = "East US 2"
}

variable "dns_prefix" {
  description = "DNS prefix for AKS cluster"
  type        = string
  default     = "employeemonitoring"
}

variable "kubernetes_version" {
  description = "Kubernetes version for AKS"
  type        = string
  default     = "1.28.5"
}

variable "ssh_public_key" {
  description = "SSH public key for AKS nodes"
  type        = string
}

variable "smtp_password" {
  description = "SMTP password for email notifications"
  type        = string
  sensitive   = true
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default = {
    Environment = "production"
    Project     = "employee-monitoring"
    ManagedBy   = "terraform"
  }
}