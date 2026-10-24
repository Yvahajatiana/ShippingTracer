# syntax=docker/dockerfile:1
# ParcelFlow Reporting service.
# Build from the repository root:
#   docker build -f src/Services/Reporting/ParcelFlow.Reporting.Api/Dockerfile -t parcelflow/reporting .

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
COPY src/Services/Reporting/ParcelFlow.Reporting.Domain/ParcelFlow.Reporting.Domain.csproj src/Services/Reporting/ParcelFlow.Reporting.Domain/
COPY src/Services/Reporting/ParcelFlow.Reporting.Application/ParcelFlow.Reporting.Application.csproj src/Services/Reporting/ParcelFlow.Reporting.Application/
COPY src/Services/Reporting/ParcelFlow.Reporting.Infrastructure/ParcelFlow.Reporting.Infrastructure.csproj src/Services/Reporting/ParcelFlow.Reporting.Infrastructure/
COPY src/Services/Reporting/ParcelFlow.Reporting.Api/ParcelFlow.Reporting.Api.csproj src/Services/Reporting/ParcelFlow.Reporting.Api/
RUN dotnet restore src/Services/Reporting/ParcelFlow.Reporting.Api/ParcelFlow.Reporting.Api.csproj -r linux-musl-x64

# Sources of the project closure only.
COPY src/Services/Reporting/ParcelFlow.Reporting.Domain/ src/Services/Reporting/ParcelFlow.Reporting.Domain/
COPY src/Services/Reporting/ParcelFlow.Reporting.Application/ src/Services/Reporting/ParcelFlow.Reporting.Application/
COPY src/Services/Reporting/ParcelFlow.Reporting.Infrastructure/ src/Services/Reporting/ParcelFlow.Reporting.Infrastructure/
COPY src/Services/Reporting/ParcelFlow.Reporting.Api/ src/Services/Reporting/ParcelFlow.Reporting.Api/
RUN dotnet publish src/Services/Reporting/ParcelFlow.Reporting.Api/ParcelFlow.Reporting.Api.csproj \
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
      org.opencontainers.image.title="parcelflow-reporting" \
      org.opencontainers.image.description="ParcelFlow Reporting service: shipment volumes, delivery rates and lead-time KPIs" \
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

ENTRYPOINT ["dotnet", "ParcelFlow.Reporting.Api.dll"]
