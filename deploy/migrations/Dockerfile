# syntax=docker/dockerfile:1
# ParcelFlow EF Core migration bundle, one image per service, run by the Kubernetes migration Jobs
# (the services never migrate at startup outside Development).
# Build from the repository root, SERVICE being the service folder name under src/Services
# (Customer, Shipment, Tracking, Warehouse, Delivery, Billing, Notification, Reporting):
#   docker build -f deploy/migrations/Dockerfile --build-arg SERVICE=Customer -t parcelflow/customer-migrations .
# Run (idempotent: only the pending migrations are applied):
#   docker run --rm --read-only --tmpfs /tmp parcelflow/customer-migrations --connection "Host=...;Database=...;Username=...;Password=..."

ARG VERSION=dev
ARG REVISION=unknown
ARG SERVICE
# Lower-case service name for the OCI title (Dockerfile ARGs have no case conversion): each
# service name has a single upper-case letter, its initial, replaced here.
ARG SERVICE_SLUG_1=${SERVICE/C/c}
ARG SERVICE_SLUG_2=${SERVICE_SLUG_1/S/s}
ARG SERVICE_SLUG_3=${SERVICE_SLUG_2/T/t}
ARG SERVICE_SLUG_4=${SERVICE_SLUG_3/W/w}
ARG SERVICE_SLUG_5=${SERVICE_SLUG_4/D/d}
ARG SERVICE_SLUG_6=${SERVICE_SLUG_5/B/b}
ARG SERVICE_SLUG_7=${SERVICE_SLUG_6/N/n}
ARG SERVICE_SLUG=${SERVICE_SLUG_7/R/r}
# Must match .config/dotnet-tools.json: the root .dockerignore excludes .config from the context.
ARG DOTNET_EF_VERSION=8.0.22

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG DOTNET_EF_VERSION

# Tool layer, before SERVICE is declared so that the eight service builds share it:
# dotnet-ef pinned to the EF Core runtime version of the services.
RUN dotnet tool install dotnet-ef --version "${DOTNET_EF_VERSION}" --tool-path /tools
ENV PATH="${PATH}:/tools" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

ARG SERVICE
RUN test -n "${SERVICE}" || { echo "Build argument SERVICE is required (e.g. --build-arg SERVICE=Customer)" >&2; exit 1; }

WORKDIR /src

# Restore layer: only the files that influence restore, so it stays cached
# until a project file or a shared build setting changes.
COPY global.json Directory.Build.props ./
# Shared building blocks are copied whole: every service depends on them and new ones need no Dockerfile change.
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Domain/ParcelFlow.${SERVICE}.Domain.csproj src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Domain/
COPY src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Application/ParcelFlow.${SERVICE}.Application.csproj src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Application/
COPY src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure/ParcelFlow.${SERVICE}.Infrastructure.csproj src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure/
RUN dotnet restore src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure/ParcelFlow.${SERVICE}.Infrastructure.csproj

# Sources of the project closure only. The Api host is not needed: each service has a single
# DbContext, created at design time by the IDesignTimeDbContextFactory of its Infrastructure
# project, which is also the only project referencing Microsoft.EntityFrameworkCore.Design.
COPY src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Domain/ src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Domain/
COPY src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Application/ src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Application/
COPY src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure/ src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure/

# Self-contained single-file bundle: the runtime image needs no .NET runtime nor any appsettings,
# the connection string is passed with --connection.
RUN dotnet ef migrations bundle \
        --project src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure \
        --startup-project src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure \
        --context ${SERVICE}DbContext \
        --configuration Release \
        --self-contained \
        --target-runtime linux-musl-x64 \
        --output /out/efbundle

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS runtime
ARG VERSION
ARG REVISION
ARG SERVICE
ARG SERVICE_SLUG

LABEL org.opencontainers.image.source="https://github.com/Yvahajatiana/ShippingTracer" \
      org.opencontainers.image.title="parcelflow-${SERVICE_SLUG}-migrations" \
      org.opencontainers.image.description="ParcelFlow ${SERVICE} service: EF Core migration bundle, applied by the migration Job" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"

# Invariant globalization: migrations need no culture data, so ICU is not installed.
# Single-file extraction (if any native library is bundled) goes to the /tmp emptyDir, the root
# filesystem being read-only in the Job.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp \
    TMPDIR=/tmp \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

WORKDIR /app
COPY --from=build --chmod=0755 /out/efbundle ./efbundle
USER $APP_UID

ENTRYPOINT ["/app/efbundle"]

