# syntax=docker/dockerfile:1
# ParcelFlow Delivery service.
# Build from the repository root:
#   docker build -f src/Services/Delivery/ParcelFlow.Delivery.Api/Dockerfile -t parcelflow/delivery .

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
COPY src/Services/Delivery/ParcelFlow.Delivery.Domain/ParcelFlow.Delivery.Domain.csproj src/Services/Delivery/ParcelFlow.Delivery.Domain/
COPY src/Services/Delivery/ParcelFlow.Delivery.Application/ParcelFlow.Delivery.Application.csproj src/Services/Delivery/ParcelFlow.Delivery.Application/
COPY src/Services/Delivery/ParcelFlow.Delivery.Infrastructure/ParcelFlow.Delivery.Infrastructure.csproj src/Services/Delivery/ParcelFlow.Delivery.Infrastructure/
COPY src/Services/Delivery/ParcelFlow.Delivery.Api/ParcelFlow.Delivery.Api.csproj src/Services/Delivery/ParcelFlow.Delivery.Api/
RUN dotnet restore src/Services/Delivery/ParcelFlow.Delivery.Api/ParcelFlow.Delivery.Api.csproj -r linux-musl-x64

# Sources of the project closure only.
COPY src/Services/Delivery/ParcelFlow.Delivery.Domain/ src/Services/Delivery/ParcelFlow.Delivery.Domain/
COPY src/Services/Delivery/ParcelFlow.Delivery.Application/ src/Services/Delivery/ParcelFlow.Delivery.Application/
COPY src/Services/Delivery/ParcelFlow.Delivery.Infrastructure/ src/Services/Delivery/ParcelFlow.Delivery.Infrastructure/
COPY src/Services/Delivery/ParcelFlow.Delivery.Api/ src/Services/Delivery/ParcelFlow.Delivery.Api/
RUN dotnet publish src/Services/Delivery/ParcelFlow.Delivery.Api/ParcelFlow.Delivery.Api.csproj \
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
      org.opencontainers.image.title="parcelflow-delivery" \
      org.opencontainers.image.description="ParcelFlow Delivery service: delivery rounds, courier assignment and proofs of delivery" \
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

ENTRYPOINT ["dotnet", "ParcelFlow.Delivery.Api.dll"]
