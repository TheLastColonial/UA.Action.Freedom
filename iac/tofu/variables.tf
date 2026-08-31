# Defaults match iac/local/.env.example, so `tofu apply` needs no arguments on a stock
# environment. Override in terraform.tfvars (gitignored) if you changed the ports or
# passwords there.

variable "keycloak_url" {
  description = "Base URL of the Keycloak container, as reached from the host."
  type        = string
  default     = "http://localhost:8081"
}

variable "keycloak_admin_username" {
  description = "Keycloak bootstrap admin username (KEYCLOAK_ADMIN in .env)."
  type        = string
  default     = "admin"
}

variable "keycloak_admin_password" {
  description = "Keycloak bootstrap admin password (KEYCLOAK_ADMIN_PASSWORD in .env)."
  type        = string
  default     = "admin"
  sensitive   = true
}

variable "realm_name" {
  description = "Realm standing in for the Entra External ID tenant."
  type        = string
  default     = "freedom"
}

variable "oidc_client_id" {
  description = "Client id the Freedom Application authenticates with."
  type        = string
  default     = "freedom-app"
}

variable "oidc_client_secret" {
  description = "Client secret for the Freedom Application (OIDC_CLIENT_SECRET in .env)."
  type        = string
  default     = "local-freedom-client-secret"
  sensitive   = true
}

variable "edge_url" {
  description = "Public URL of the environment, used for OIDC redirect and post-logout URIs."
  type        = string
  default     = "http://localhost:8080"
}

variable "oidc_spa_client_id" {
  description = "Client id the browser SPA (operator UI) authenticates with — public, PKCE."
  type        = string
  default     = "freedom-spa"
}

variable "vite_dev_url" {
  description = "Origin of the Vite dev server, added to the SPA client's redirect URIs and web origins for local development."
  type        = string
  default     = "http://localhost:5173"
}

variable "test_user_password" {
  description = "Password shared by every seeded test user."
  type        = string
  default     = "password"
  sensitive   = true
}

variable "azurite_account_name" {
  description = "Azurite's well-known development account name."
  type        = string
  default     = "devstoreaccount1"
}

variable "azurite_account_key" {
  description = <<-EOT
    Azurite's well-known development account key. Published by Microsoft and identical on
    every Azurite installation — it is safe only because nothing in this environment is
    reachable from outside the machine. Azure uses managed identity with shared-key
    authorisation disabled entirely (docs/recommendations.md 4.2).
  EOT
  type        = string
  default     = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="
}

variable "azurite_blob_endpoint" {
  description = "Azurite blob endpoint, as reached from the host."
  type        = string
  default     = "http://127.0.0.1:10000/devstoreaccount1"
}

variable "azurite_queue_endpoint" {
  description = "Azurite queue endpoint, as reached from the host."
  type        = string
  default     = "http://127.0.0.1:10001/devstoreaccount1"
}

variable "mssql_container" {
  description = "Name of the SQL Server container to run the schema bootstrap in."
  type        = string
  default     = "freedom-mssql"
}

variable "mssql_sa_password" {
  description = "SQL Server sa password (MSSQL_SA_PASSWORD in .env)."
  type        = string
  default     = "Local_Freedom_Dev_1"
  sensitive   = true
}

# The two ordinary logins the bootstrap creates. In Azure both are managed identities with
# no password at all (recommendations 4.2); these exist only because a local SQL Server has
# nothing else to authenticate with. They must match what docker-compose hands the app.
variable "mssql_app_password" {
  description = "Password for the freedom_app login (FREEDOM_APP_DB_PASSWORD in .env)."
  type        = string
  default     = "Local_Freedom_App_1"
  sensitive   = true
}

variable "mssql_sensitive_password" {
  description = "Password for the freedom_sensitive login (FREEDOM_SENSITIVE_DB_PASSWORD in .env)."
  type        = string
  default     = "Local_Freedom_Sensitive_1"
  sensitive   = true
}
