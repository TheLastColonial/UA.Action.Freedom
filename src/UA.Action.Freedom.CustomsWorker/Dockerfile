# Customs Worker — the Azure Function of the target design, packaged as a plain container.
#
# Build context is the repository root:
#   docker build -f src/UA.Action.Freedom.CustomsWorker/Dockerfile .

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source

COPY global.json ./
COPY src/UA.Action.Freedom.CustomsWorker/UA.Action.Freedom.CustomsWorker.csproj src/UA.Action.Freedom.CustomsWorker/
COPY src/HMRC.GVMS/HMRC.GVMS.csproj                                            src/HMRC.GVMS/
COPY src/HMRC.PushPullNotifications/HMRC.PushPullNotifications.csproj          src/HMRC.PushPullNotifications/

RUN dotnet restore src/UA.Action.Freedom.CustomsWorker/UA.Action.Freedom.CustomsWorker.csproj

COPY src/ src/

RUN dotnet publish src/UA.Action.Freedom.CustomsWorker/UA.Action.Freedom.CustomsWorker.csproj \
        --configuration $BUILD_CONFIGURATION \
        --no-restore \
        --output /app

# ---- runtime --------------------------------------------------------------
# The runtime image, not aspnet: the worker binds no port. Per recommendations 4.1 it
# reaches out to HMRC and never accepts an inbound connection, so there is nothing to
# listen on and nothing to attack.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app .

USER app

ENTRYPOINT ["dotnet", "UA.Action.Freedom.CustomsWorker.dll"]
