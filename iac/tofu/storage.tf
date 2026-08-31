# Blob containers and queues inside the Azurite account.
#
# In Azure these are azurerm_storage_container and azurerm_storage_queue resources. Azurite
# speaks the same REST API but no provider targets it, so the Azure CLI does the work.
#
# Two things about the shape of this file are deliberate rather than lazy.
#
# The connection string is handed over in the environment, not on the command line. It is
# semicolon-separated, and OpenTofu runs local-exec through `cmd /C` on Windows, which
# mangles the quoting and passes `az` a truncated value. Separate --blob-endpoint /
# --queue-endpoint flags are not an alternative: `az storage queue` derives the account
# name from the endpoint hostname, which works for `account.queue.core.windows.net` and
# not for Azurite's path-style `127.0.0.1:10001/devstoreaccount1`.
#
# Each kind is one resource creating every name in sequence, rather than a for_each
# creating one resource per name. for_each would read better in the plan, but OpenTofu runs
# it in parallel and Azurite resets connections when six Azure CLI processes arrive at
# once — an intermittent failure in the one command a newcomer runs first is worse than a
# slightly coarser resource graph.
#
# There are no destroy-time provisioners. Azurite keeps everything in one docker volume, so
# `docker compose down -v` is the teardown; deleting containers one at a time from state
# would fail whenever that volume had already gone, which is most of the time.

locals {
  # One account with prefixes, not an account per document type (recommendations 1):
  #   manifests/       manifest documents that travel with a vehicle
  #   gmr/             Goods Movement Reference documents, written by the Customs Worker
  #   elo/             Export Load Objects
  #   dataprotection/  the ASP.NET Core key ring (3.2) — not a document, but it lives
  #                    outside the container filesystem for the same reason
  blob_containers = ["manifests", "gmr", "elo", "dataprotection"]

  # The durable hand-offs to the workers, each with somewhere for messages that will never
  # succeed to go and be noticed:
  #   customs-work        GMR submissions for the Customs Worker
  #   manifest-documents  approved manifests for the Manifest Worker to render
  queues = [
    "customs-work", "customs-work-poison",
    "manifest-documents", "manifest-documents-poison",
  ]

  azurite_connection_string = join(";", [
    "DefaultEndpointsProtocol=http",
    "AccountName=${var.azurite_account_name}",
    "AccountKey=${var.azurite_account_key}",
    "BlobEndpoint=${var.azurite_blob_endpoint}",
    "QueueEndpoint=${var.azurite_queue_endpoint}",
  ])

  azurite_environment = { AZURE_STORAGE_CONNECTION_STRING = local.azurite_connection_string }
}

resource "terraform_data" "blob_containers" {
  # Runs after Keycloak rather than alongside it. On Windows every one of these endpoints
  # is reached through Docker Desktop's loopback proxy, which resets connections when the
  # whole graph hits it at once — and an intermittent failure in the first command a
  # newcomer runs is worse than an apply that takes a few seconds longer.
  depends_on = [keycloak_user_groups.seed]

  # Re-runs when the set of containers changes, or when the account is replaced underneath
  # — `docker compose down -v` wipes the Azurite volume, and state must not go on claiming
  # these exist.
  triggers_replace = {
    account    = var.azurite_blob_endpoint
    containers = join(",", local.blob_containers)
  }

  provisioner "local-exec" {
    command = join(" && ", [
      for name in local.blob_containers :
      "az storage container create --name ${name} --output none"
    ])
    environment = local.azurite_environment
  }
}

resource "terraform_data" "queues" {
  depends_on = [terraform_data.blob_containers]

  triggers_replace = {
    account = var.azurite_queue_endpoint
    queues  = join(",", local.queues)
  }

  provisioner "local-exec" {
    command = join(" && ", [
      for name in local.queues :
      "az storage queue create --name ${name} --output none"
    ])
    environment = local.azurite_environment
  }
}
