# Toolchain for working on one agent's worktree: .NET 10 SDK (features add Node, pwsh, docker CLI).
# The *stack* (SQL, Keycloak, ...) is not started from in here — bind-mount paths in docker-compose.yml
# are host paths. Start it on the host with scripts/agent/agent.ps1; this container reaches it through
# host.docker.internal using .agent/env.container.sh.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install --yes --no-install-recommends git curl jq netcat-openbsd ca-certificates \
    && rm -rf /var/lib/apt/lists/*
