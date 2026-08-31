# Manifest Worker — the document-generating Azure Function of the target design, packaged as a
# plain container.
#
# Build context is the repository root:
#   docker build -f src/UA.Action.Freedom.ManifestWorker/Dockerfile .

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source

COPY global.json ./
COPY src/UA.Action.Freedom.ManifestWorker/UA.Action.Freedom.ManifestWorker.csproj src/UA.Action.Freedom.ManifestWorker/

RUN dotnet restore src/UA.Action.Freedom.ManifestWorker/UA.Action.Freedom.ManifestWorker.csproj

COPY src/ src/

RUN dotnet publish src/UA.Action.Freedom.ManifestWorker/UA.Action.Freedom.ManifestWorker.csproj \
        --configuration $BUILD_CONFIGURATION \
        --no-restore \
        --output /app

# ---- runtime --------------------------------------------------------------
# The runtime image, not aspnet: the worker binds no port. It reads a queue and writes to blob
# storage, never accepting an inbound connection, so there is nothing to listen on and nothing
# to attack.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app .

USER app

ENTRYPOINT ["dotnet", "UA.Action.Freedom.ManifestWorker.dll"]
