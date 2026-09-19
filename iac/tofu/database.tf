# Freedom database principals.
#
# The schema is not applied here. It is the dacpac built from database/UA.Action.Freedom.Database,
# published by the `db-deploy` compose service before this runs — the database's shape ships on
# its own pipeline, separately from code and from environment wiring. What remains for the control
# plane is the environment's half: the logins and users that connect locally, and the roles they
# join. In Azure that is managed identities added by the deployment pipeline; the roles and grants
# they rely on come from the same dacpac.
#
# The script is ../local/sql/principals.sql, mounted at /sql and run with the sqlcmd already
# inside the container — no host-side SQL tooling required.

resource "terraform_data" "database_principals" {
  # Last in the chain, for the same reason as the storage resources: everything here talks
  # to a container through Docker Desktop's loopback proxy, and running the graph in
  # parallel makes that proxy drop connections.
  depends_on = [terraform_data.queues]

  # Re-runs whenever the script changes, so editing it and re-applying is the normal workflow
  # rather than something needing a taint.
  triggers_replace = {
    script = filesha256("${path.module}/../local/sql/principals.sql")
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
      # a T-SQL error, without which a failed step would report success.
      "-C -b",
      "-i /sql/principals.sql",
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
