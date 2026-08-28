# Freedom Application — the image Azure Container Apps runs in the target design.
#
# Build context is the repository root, so the restore sees the whole solution graph:
#   docker build -f src/UA.Action.Freedom.Api/Dockerfile .
# In the local environment docker compose does this for you (iac/local/docker-compose.yml).

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source

# Project files first, so a code-only change reuses the restore layer. The Api project
# references Application and Data, which reference Domain, so the whole graph is copied
# before the restore.
COPY global.json ./
COPY src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj          src/UA.Action.Freedom.Api/
COPY src/UA.Action.Freedom.Application/UA.Action.Freedom.Application.csproj src/UA.Action.Freedom.Application/
COPY src/UA.Action.Freedom.Data/UA.Action.Freedom.Data.csproj        src/UA.Action.Freedom.Data/
COPY src/UA.Action.Freedom.Domain/UA.Action.Freedom.Domain.csproj    src/UA.Action.Freedom.Domain/
COPY src/HMRC.GVMS/HMRC.GVMS.csproj                                  src/HMRC.GVMS/
COPY src/HMRC.PushPullNotifications/HMRC.PushPullNotifications.csproj src/HMRC.PushPullNotifications/

RUN dotnet restore src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj

COPY src/ src/

RUN dotnet publish src/UA.Action.Freedom.Api/UA.Action.Freedom.Api.csproj \
        --configuration $BUILD_CONFIGURATION \
        --no-restore \
        --output /app

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is here for the container healthcheck, which probes /health/live over HTTP rather
# than merely checking the port is open. The distinction matters: a process that has
# bound the port but failed to configure itself is exactly the state a health probe
# exists to catch.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Plain HTTP only. TLS is terminated in front — by Cloudflare into Container Apps in the
# target design, by Traefik in the local environment — and the app is told not to redirect
# (Hosting__UseHttpsRedirection=false), because a redirect to HTTPS would arrive back here
# as HTTP and loop.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080

# The aspnet image ships a non-root `app` user (uid 1654). Container Apps does not require
# it, but running as root in a container that faces the internet is a choice worth not
# making.
USER app

ENTRYPOINT ["dotnet", "UA.Action.Freedom.Api.dll"]
