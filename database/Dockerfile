# Database deployer — builds the Freedom dacpac and publishes it with SqlPackage.
#
# A one-shot container: it runs, brings the target database to the state the SQL project
# declares, optionally loads the local dev seed, and exits. The local stack runs it as the
# `db-deploy` compose service; nothing about it is local-only except where it points.
#
# Build context is the repository root:
#   docker build -f database/Dockerfile .

ARG SQLPACKAGE_VERSION=170.5.96
ARG GO_SQLCMD_VERSION=v1.10.0

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SQLPACKAGE_VERSION
ARG GO_SQLCMD_VERSION
ARG TARGETARCH
WORKDIR /source

RUN apt-get update \
    && apt-get install --no-install-recommends -y bzip2 \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet tool install microsoft.sqlpackage --version ${SQLPACKAGE_VERSION} --tool-path /tools

# go-sqlcmd runs the dev seed. It is a static binary, so it needs nothing from the runtime image.
RUN curl -fsSL "https://github.com/microsoft/go-sqlcmd/releases/download/${GO_SQLCMD_VERSION}/sqlcmd-linux-${TARGETARCH:-amd64}.tar.bz2" \
    | tar -xj -C /tools sqlcmd

COPY global.json ./
COPY database/UA.Action.Freedom.Database/ database/UA.Action.Freedom.Database/

RUN dotnet build database/UA.Action.Freedom.Database/UA.Action.Freedom.Database.sqlproj \
        --configuration Release \
        --output /dacpac

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /deploy

COPY --from=build /tools /tools
COPY --from=build /dacpac/UA.Action.Freedom.Database.dacpac ./Freedom.dacpac
COPY database/seed/ ./seed/
COPY --chmod=755 database/deploy.sh ./deploy.sh
COPY --chmod=755 database/seed.sh ./seed.sh

ENV PATH="/tools:${PATH}"

USER app

ENTRYPOINT ["/deploy/deploy.sh"]
