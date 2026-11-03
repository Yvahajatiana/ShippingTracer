# syntax=docker/dockerfile:1
# ParcelFlow API gateway (YARP).
# Build from the repository root:
#   docker build -f src/Gateway/ParcelFlow.Gateway/Dockerfile -t parcelflow/gateway .

ARG VERSION=dev
ARG REVISION=unknown

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Restore layer: only the files that influence restore, so it stays cached
# until a project file or a shared build setting changes.
COPY global.json Directory.Build.props ./
# Shared building blocks are copied whole: every service depends on them and new ones need no Dockerfile change.
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Gateway/ParcelFlow.Gateway/ParcelFlow.Gateway.csproj src/Gateway/ParcelFlow.Gateway/
RUN dotnet restore src/Gateway/ParcelFlow.Gateway/ParcelFlow.Gateway.csproj -r linux-musl-x64

# Sources of the project closure only.
COPY src/Gateway/ParcelFlow.Gateway/ src/Gateway/ParcelFlow.Gateway/
RUN dotnet publish src/Gateway/ParcelFlow.Gateway/ParcelFlow.Gateway.csproj \
        -c Release \
        -r linux-musl-x64 \
        --no-restore \
        --self-contained false \
        -o /app \
        /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
ARG VERSION
ARG REVISION

LABEL org.opencontainers.image.source="https://github.com/Yvahajatiana/ShippingTracer" \
      org.opencontainers.image.title="parcelflow-gateway" \
      org.opencontainers.image.description="ParcelFlow API gateway: YARP reverse proxy, JWT validation and routing to the domain services" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"

# ICU for culture-aware formatting, tzdata for time zone conversions.
RUN apk add --no-cache icu-libs tzdata

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080

WORKDIR /app
COPY --from=build /app ./

EXPOSE 8080
USER $APP_UID

# busybox wget is available in the alpine base image.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "ParcelFlow.Gateway.dll"]
