# syntax=docker/dockerfile:1
# ParcelFlow Shipment service.
# Build from the repository root:
#   docker build -f src/Services/Shipment/ParcelFlow.Shipment.Api/Dockerfile -t parcelflow/shipment .

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
COPY src/BuildingBlocks/ParcelFlow.BuildingBlocks.Contracts/ParcelFlow.BuildingBlocks.Contracts.csproj src/BuildingBlocks/ParcelFlow.BuildingBlocks.Contracts/
COPY src/BuildingBlocks/ParcelFlow.BuildingBlocks.Application/ParcelFlow.BuildingBlocks.Application.csproj src/BuildingBlocks/ParcelFlow.BuildingBlocks.Application/
COPY src/BuildingBlocks/ParcelFlow.BuildingBlocks.Api/ParcelFlow.BuildingBlocks.Api.csproj src/BuildingBlocks/ParcelFlow.BuildingBlocks.Api/
COPY src/Services/Shipment/ParcelFlow.Shipment.Domain/ParcelFlow.Shipment.Domain.csproj src/Services/Shipment/ParcelFlow.Shipment.Domain/
COPY src/Services/Shipment/ParcelFlow.Shipment.Application/ParcelFlow.Shipment.Application.csproj src/Services/Shipment/ParcelFlow.Shipment.Application/
COPY src/Services/Shipment/ParcelFlow.Shipment.Infrastructure/ParcelFlow.Shipment.Infrastructure.csproj src/Services/Shipment/ParcelFlow.Shipment.Infrastructure/
COPY src/Services/Shipment/ParcelFlow.Shipment.Api/ParcelFlow.Shipment.Api.csproj src/Services/Shipment/ParcelFlow.Shipment.Api/
RUN dotnet restore src/Services/Shipment/ParcelFlow.Shipment.Api/ParcelFlow.Shipment.Api.csproj -r linux-musl-x64

# Sources of the project closure only.
COPY src/BuildingBlocks/ParcelFlow.BuildingBlocks.Contracts/ src/BuildingBlocks/ParcelFlow.BuildingBlocks.Contracts/
COPY src/BuildingBlocks/ParcelFlow.BuildingBlocks.Application/ src/BuildingBlocks/ParcelFlow.BuildingBlocks.Application/
COPY src/BuildingBlocks/ParcelFlow.BuildingBlocks.Api/ src/BuildingBlocks/ParcelFlow.BuildingBlocks.Api/
COPY src/Services/Shipment/ParcelFlow.Shipment.Domain/ src/Services/Shipment/ParcelFlow.Shipment.Domain/
COPY src/Services/Shipment/ParcelFlow.Shipment.Application/ src/Services/Shipment/ParcelFlow.Shipment.Application/
COPY src/Services/Shipment/ParcelFlow.Shipment.Infrastructure/ src/Services/Shipment/ParcelFlow.Shipment.Infrastructure/
COPY src/Services/Shipment/ParcelFlow.Shipment.Api/ src/Services/Shipment/ParcelFlow.Shipment.Api/
RUN dotnet publish src/Services/Shipment/ParcelFlow.Shipment.Api/ParcelFlow.Shipment.Api.csproj \
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
      org.opencontainers.image.title="parcelflow-shipment" \
      org.opencontainers.image.description="ParcelFlow Shipment service: shipments, server-side quotes, tracking numbers and PDF labels" \
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

ENTRYPOINT ["dotnet", "ParcelFlow.Shipment.Api.dll"]
