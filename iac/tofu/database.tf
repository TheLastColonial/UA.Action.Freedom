# Freedom database bootstrap.
#
# In Azure this is a migration step in the deployment pipeline plus azurerm_mssql_database
# for the server itself. Here the server is a container and the schema is applied with
# sqlcmd, which is already inside that container — no host-side SQL tooling required.
#
# The script is ../local/sql/001-schemas.sql, mounted at /sql. It is idempotent, so a
# re-apply is harmless, and it is the file to read to understand how receiver detail is
# segregated (recommendations 4.4).

resource "terraform_data" "database_schema" {
  # Last in the chain, for the same reason as the storage resources: everything here talks
  # to a container through Docker Desktop's loopback proxy, and running the graph in
  # parallel makes that proxy drop connections.
  depends_on = [terraform_data.queues]

  # Re-runs whenever the script changes, so editing the schema and re-applying is the
  # normal workflow rather than something needing a taint.
  triggers_replace = {
    script = filesha256("${path.module}/../local/sql/001-schemas.sql")
  }

  provisioner "local-exec" {
    command = join(" ", [
      # `docker exec -e NAME` with no value forwards the variable from this process's
      # environment. The password therefore never appears on a command line — not here,
      # not in `docker inspect`, not in the process table — and, just as usefully, never
      # has to survive `cmd /C` quoting, which silently mangles a quoted -P argument into
      # a login failure.
      "docker exec -e SQLCMDPASSWORD -e FREEDOM_APP_PASSWORD -e FREEDOM_SENSITIVE_PASSWORD ${var.mssql_container}",
      "/opt/mssql-tools18/bin/sqlcmd",
      "-S localhost -U sa",
      # -C trusts the container's self-signed certificate; -b makes sqlcmd exit non-zero on
      # a T-SQL error, without which a failed bootstrap would report success.
      "-C -b",
      "-i /sql/001-schemas.sql",
    ])

    # sqlcmd resolves $(NAME) in the script from the environment as well as from -v, so the
    # login passwords reach CREATE LOGIN the same way the sa password reaches sqlcmd itself.
    environment = {
      SQLCMDPASSWORD             = var.mssql_sa_password
      FREEDOM_APP_PASSWORD       = var.mssql_app_password
      FREEDOM_SENSITIVE_PASSWORD = var.mssql_sensitive_password
    }
  }
}
