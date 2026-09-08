#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
    Empties every application table in the local Freedom database, leaving the schema intact.

.DESCRIPTION
    Runs sql/002-reset-data.sql inside the running SQL Server container as `sa`. Use it to get
    a local database back to a known-good empty state after test runs or manual poking, without
    `docker compose down -v` (which also drops the Keycloak realm and forces another tofu apply).

    Requires the stack to be up (docker compose up -d). Schemas, roles, logins and grants are
    untouched — only rows are removed.

.PARAMETER Container
    Name of the SQL Server container. Defaults to the compose service name `freedom-mssql`.
#>
[CmdletBinding()]
param(
    [string] $Container = 'freedom-mssql'
)

$ErrorActionPreference = 'Stop'

Write-Host "==> Resetting the Freedom database ($Container)" -ForegroundColor Cyan

# $MSSQL_SA_PASSWORD is resolved inside the container (it is in the mssql service environment),
# so it never touches this shell or the `docker exec` argv.
docker exec $Container /bin/sh -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -i /sql/002-reset-data.sql'

if ($LASTEXITCODE -ne 0) {
    throw "sqlcmd exited with code $LASTEXITCODE"
}

Write-Host 'Done.' -ForegroundColor Green
