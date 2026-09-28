# Customs Worker — the Azure Function of the target design, packaged as a plain container.
#
# Build context is the repository root:
#   docker build -f src/UA.Action.Freedom.CustomsWorker/Dockerfile .

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source

COPY global.json Directory.Packages.props ./
COPY src/UA.Action.Freedom.CustomsWorker/UA.Action.Freedom.CustomsWorker.csproj src/UA.Action.Freedom.CustomsWorker/
COPY src/EDI.ELO/EDI.ELO.csproj                                                src/EDI.ELO/
COPY src/HMRC.GVMS/HMRC.GVMS.csproj                                            src/HMRC.GVMS/
COPY src/HMRC.PushPullNotifications/HMRC.PushPullNotifications.csproj          src/HMRC.PushPullNotifications/
COPY src/UA.Action.Freedom.Telemetry/UA.Action.Freedom.Telemetry.csproj              src/UA.Action.Freedom.Telemetry/

RUN dotnet restore src/UA.Action.Freedom.CustomsWorker/UA.Action.Freedom.CustomsWorker.csproj

COPY src/ src/

RUN dotnet publish src/UA.Action.Freedom.CustomsWorker/UA.Action.Freedom.CustomsWorker.csproj \
        --configuration $BUILD_CONFIGURATION \
        --no-restore \
        --output /app

# ---- runtime --------------------------------------------------------------
# The runtime image, not aspnet: the worker binds no port. Per recommendations 4.1 it
# reaches out to HMRC and to French customs and never accepts an inbound connection, so there
# is nothing to listen on and nothing to attack. That is also why ELO passage notifications
# are not consumed — they would require exactly such an endpoint.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app .

USER app

ENTRYPOINT ["dotnet", "UA.Action.Freedom.CustomsWorker.dll"]
