output "environment" {
  description = "Everything you need to reach the environment, in one place."

  value = {
    application    = var.edge_url
    operator_ui    = "${var.edge_url}/app/"
    spa_client_id  = var.oidc_spa_client_id
    readiness      = "${var.edge_url}/health/ready"
    api_reference  = "${var.edge_url}/scalar/v1"
    public_website = "${var.edge_url}/site"
    identity       = "${var.keycloak_url}/realms/${var.realm_name}"
    identity_admin = "${var.keycloak_url}/admin"
    hmrc_stubs     = "http://localhost:${var.wiremock_port}/__admin/mappings"
    telemetry      = "http://localhost:${var.grafana_port}"
    inbox          = "http://localhost:${var.mailpit_port}"
    edge_dashboard = "http://localhost:${var.edge_dashboard_port}/dashboard/"
  }
}

output "test_users" {
  description = <<-EOT
    Seeded users and the roles each carries (via same-named Keycloak groups). All share
    the same password (test_user_password, "password" by default). Obtain a token with:

      curl -s -X POST '<identity>/protocol/openid-connect/token' \
        -d grant_type=password -d client_id=freedom-app \
        -d client_secret=<oidc_client_secret> \
        -d username=operator -d password=password
  EOT

  value = { for name, roles in local.seed_users : lower(name) => roles }
}

output "blob_containers" {
  description = "Blob containers created in the Azurite account."
  value       = sort(local.blob_containers)
}

output "queues" {
  description = "Queues created in the Azurite account."
  value       = sort(local.queues)
}
