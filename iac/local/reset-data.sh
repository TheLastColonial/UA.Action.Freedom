#!/usr/bin/env bash
#
# Empties every application table in the local Freedom database, leaving the schema intact.
# Mirror of reset-data.ps1 for Linux/macOS.
#
# Runs sql/002-reset-data.sql inside the running SQL Server container as `sa`. Use it to get a
# local database back to a known-good empty state after test runs or manual poking, without
# `docker compose down -v` (which also drops the Keycloak realm and forces another tofu apply).
#
#   ./reset-data.sh [--container freedom-mssql]
#
set -euo pipefail

container="freedom-mssql"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --container) container="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

echo "==> Resetting the Freedom database (${container})"

# $MSSQL_SA_PASSWORD is resolved inside the container (it is in the mssql service environment),
# so it never touches this shell or the `docker exec` argv.
docker exec "${container}" /bin/sh -c \
    '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -i /sql/002-reset-data.sql'

echo "Done."
