terraform {
  required_version = ">= 1.9.0"

  required_providers {
    keycloak = {
      source  = "keycloak/keycloak"
      version = "~> 5.0"
    }
  }
}

# Points at the Keycloak container started by ../local/docker-compose.yml.
#
# In Azure the equivalent resources are Entra External ID app registrations, app roles and
# user flows, provisioned with the azuread provider. The shape of the configuration is the
# same — a client, a set of roles, an assignment per user — which is the point of using
# Keycloak here rather than hand-waving identity away.
provider "keycloak" {
  client_id = "admin-cli"
  username  = var.keycloak_admin_username
  password  = var.keycloak_admin_password
  url       = var.keycloak_url

  # start-dev serves plain HTTP. Nothing here leaves the machine.
  tls_insecure_skip_verify = true
  initial_login            = false
}
