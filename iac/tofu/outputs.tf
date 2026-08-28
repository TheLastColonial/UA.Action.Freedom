output "environment" {
  description = "Everything you need to reach the environment, in one place."

  value = {
    application     = var.edge_url
    readiness       = "${var.edge_url}/health/ready"
    api_reference   = "${var.edge_url}/scalar/v1"
    public_website  = "${var.edge_url}/site"
    identity        = "${var.keycloak_url}/realms/${var.realm_name}"
    identity_admin  = "${var.keycloak_url}/admin"
    hmrc_stubs      = "http://localhost:8082/__admin/mappings"
    telemetry       = "http://localhost:3000"
    inbox           = "http://localhost:8025"
    edge_dashboard  = "http://localhost:8090/dashboard/"
  }
}

output "test_users" {
  description = <<-EOT
    Seeded users, one per app role. All share the same password (test_user_password,
    "password" by default). Obtain a token with:

      curl -s -X POST '<identity>/protocol/openid-connect/token' \
        -d grant_type=password -d client_id=freedom-app \
        -d client_secret=<oidc_client_secret> \
        -d username=dispatcher -d password=password
  EOT

  value = [for name in keys(local.app_roles) : lower(name)]
}

output "blob_containers" {
  description = "Blob containers created in the Azurite account."
  value       = sort(local.blob_containers)
}

output "queues" {
  description = "Queues created in the Azurite account."
  value       = sort(local.queues)
}
