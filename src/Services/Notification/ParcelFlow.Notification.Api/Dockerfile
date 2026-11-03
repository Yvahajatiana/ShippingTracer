# syntax=docker/dockerfile:1
# ParcelFlow Notification service.
# Build from the repository root:
#   docker build -f src/Services/Notification/ParcelFlow.Notification.Api/Dockerfile -t parcelflow/notification .

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
COPY src/Services/Notification/ParcelFlow.Notification.Domain/ParcelFlow.Notification.Domain.csproj src/Services/Notification/ParcelFlow.Notification.Domain/
COPY src/Services/Notification/ParcelFlow.Notification.Application/ParcelFlow.Notification.Application.csproj src/Services/Notification/ParcelFlow.Notification.Application/
COPY src/Services/Notification/ParcelFlow.Notification.Infrastructure/ParcelFlow.Notification.Infrastructure.csproj src/Services/Notification/ParcelFlow.Notification.Infrastructure/
COPY src/Services/Notification/ParcelFlow.Notification.Api/ParcelFlow.Notification.Api.csproj src/Services/Notification/ParcelFlow.Notification.Api/
RUN dotnet restore src/Services/Notification/ParcelFlow.Notification.Api/ParcelFlow.Notification.Api.csproj -r linux-musl-x64

# Sources of the project closure only.
COPY src/Services/Notification/ParcelFlow.Notification.Domain/ src/Services/Notification/ParcelFlow.Notification.Domain/
COPY src/Services/Notification/ParcelFlow.Notification.Application/ src/Services/Notification/ParcelFlow.Notification.Application/
COPY src/Services/Notification/ParcelFlow.Notification.Infrastructure/ src/Services/Notification/ParcelFlow.Notification.Infrastructure/
COPY src/Services/Notification/ParcelFlow.Notification.Api/ src/Services/Notification/ParcelFlow.Notification.Api/
RUN dotnet publish src/Services/Notification/ParcelFlow.Notification.Api/ParcelFlow.Notification.Api.csproj \
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
      org.opencontainers.image.title="parcelflow-notification" \
      org.opencontainers.image.description="ParcelFlow Notification service: simulated e-mail and SMS notifications driven by domain events" \
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

ENTRYPOINT ["dotnet", "ParcelFlow.Notification.Api.dll"]
