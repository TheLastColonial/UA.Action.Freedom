#!/bin/sh
# Brings a database to the state the Freedom dacpac declares.
#
# SqlPackage diffs the dacpac against the target and generates the change itself: on an empty
# server that is a create, on an up-to-date one it is nothing. No script in here knows what
# the database looked like before.
#
# Environment:
#   FREEDOM_DB_SERVER      target server              (default: mssql)
#   FREEDOM_DB_NAME        target database            (default: Freedom)
#   FREEDOM_DB_USER        deploying login            (default: sa)
#   FREEDOM_DB_PASSWORD    its password               (required)
#
# Seed data loading is a separate step (database/seed.sh, db-seed compose service).
set -eu

server="${FREEDOM_DB_SERVER:-mssql}"
database="${FREEDOM_DB_NAME:-Freedom}"
user="${FREEDOM_DB_USER:-sa}"
: "${FREEDOM_DB_PASSWORD:?FREEDOM_DB_PASSWORD is required}"

xml_escape() {
    printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g'
}

# The connection string goes in a publish profile rather than on the command line, so the
# password never appears in the process table or in `docker inspect`.
profile="$(mktemp)"
trap 'rm -f "$profile"' EXIT
cat > "$profile" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <TargetConnectionString>$(xml_escape "Server=$server;Database=$database;User Id=$user;Password=$FREEDOM_DB_PASSWORD;TrustServerCertificate=True;Encrypt=False")</TargetConnectionString>
  </PropertyGroup>
</Project>
EOF

# AllowIncompatiblePlatform: the model is validated against Azure SQL, the production target;
#   the local SQL Server 2022 container runs the same engine.
# ScriptDatabaseOptions=false: database-level settings belong to whoever owns the server.
# ExcludeObjectTypes: users, logins and role membership are the environment's, not the
#   dacpac's (iac/local/sql/principals.sql) — a publish must never touch them.
# BlockOnPossibleDataLoss: a change that would drop a column with data in it fails the
#   deployment instead of quietly dropping it.
sqlpackage \
    -a:Publish \
    -sf:/deploy/Freedom.dacpac \
    -pr:"$profile" \
    -p:AllowIncompatiblePlatform=true \
    -p:ScriptDatabaseOptions=false \
    -p:BlockOnPossibleDataLoss=true \
    -p:ExcludeObjectTypes="Users;Logins;RoleMembership"
