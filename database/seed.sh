#!/bin/sh
# Loads the dev seed data into the database.
#
# This is a separate step from schema deployment, run only when explicitly invoked
# via the db-seed compose service. Safe to run multiple times — the seed script guards
# against re-seeding an already-populated database (see database/seed/dev-seed.sql).
#
# Environment:
#   FREEDOM_DB_SERVER      target server              (default: mssql)
#   FREEDOM_DB_NAME        target database            (default: Freedom)
#   FREEDOM_DB_USER        connecting login           (default: sa)
#   FREEDOM_DB_PASSWORD    its password               (required)
set -eu

server="${FREEDOM_DB_SERVER:-mssql}"
database="${FREEDOM_DB_NAME:-Freedom}"
user="${FREEDOM_DB_USER:-sa}"
: "${FREEDOM_DB_PASSWORD:?FREEDOM_DB_PASSWORD is required}"

echo "Loading dev seed into $database on $server."
SQLCMDPASSWORD="$FREEDOM_DB_PASSWORD" sqlcmd \
    -S "$server" -U "$user" -d "$database" -C -b -I \
    -i /deploy/seed/dev-seed.sql
