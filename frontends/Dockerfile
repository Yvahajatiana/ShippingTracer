# syntax=docker/dockerfile:1

# One image per portal, built from the shared Angular workspace:
#   docker build -f frontends/Dockerfile --build-arg PORTAL=admin-portal -t parcelflow/admin-portal frontends

# ---------------------------------------------------------------------------
# Stage 1: build the Angular portal
# ---------------------------------------------------------------------------
FROM node:22.23-alpine AS build

ARG PORTAL
ENV CI=true \
    NG_CLI_ANALYTICS=false \
    NPM_CONFIG_UPDATE_NOTIFIER=false

WORKDIR /workspace

# Restore dependencies first so this layer is reused while only sources change.
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci --no-audit --no-fund

# Only the files the Angular build needs, so nginx/entrypoint edits do not invalidate this stage.
# .postcssrc.json enables the Tailwind PostCSS plugin; without it the bundle ships no utilities.
COPY angular.json tsconfig.json components.json .postcssrc.json ./
COPY projects ./projects
COPY docker/build/csp-script-hashes.mjs /tools/csp-script-hashes.mjs

RUN case "$PORTAL" in \
      customer-portal|operations-portal|admin-portal) ;; \
      *) echo "PORTAL build arg must be customer-portal, operations-portal or admin-portal (got '$PORTAL')" >&2; exit 1 ;; \
    esac \
 && npx ng build "$PORTAL" \
 && test -f "dist/$PORTAL/browser/index.html" \
 && node /tools/csp-script-hashes.mjs "dist/$PORTAL/browser/index.html" /tools/csp-script-hashes.txt

# ---------------------------------------------------------------------------
# Stage 2: serve the static bundle with an unprivileged nginx
# ---------------------------------------------------------------------------
FROM nginxinc/nginx-unprivileged:1.30-alpine AS runtime

ARG PORTAL

LABEL org.opencontainers.image.title="parcelflow-${PORTAL}" \
      org.opencontainers.image.description="ParcelFlow ${PORTAL} served by nginx" \
      org.opencontainers.image.source="https://github.com/Yvahajatiana/ShippingTracer"

USER root

# Drop the stock site and welcome page; the portal ships its own configuration.
RUN rm -f /etc/nginx/conf.d/default.conf \
 && rm -rf /usr/share/nginx/html/*

# Static files stay root-owned and read-only; only config.json is writable by nginx (uid 101)
# so the entrypoint script can render it from the environment at container start.
COPY --from=build /workspace/dist/${PORTAL}/browser/ /usr/share/nginx/html/
RUN chown 101:0 /usr/share/nginx/html/config.json \
 && chmod 0664 /usr/share/nginx/html/config.json

# nginx templates, rendered by the stock 20-envsubst-on-templates.sh at container start.
# The CSP script hashes of this very build are baked into the security headers template.
COPY docker/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY docker/nginx/security-headers.conf.template /etc/nginx/templates/parcelflow/security-headers.conf.template
COPY --from=build /tools/csp-script-hashes.txt /tmp/csp-script-hashes.txt
RUN hashes="$(cat /tmp/csp-script-hashes.txt)" \
 && sed -i "s|__CSP_SCRIPT_HASHES__|${hashes}|" /etc/nginx/templates/parcelflow/security-headers.conf.template \
 && sed -i "s|script-src 'self' ;|script-src 'self';|" /etc/nginx/templates/parcelflow/security-headers.conf.template \
 && ! grep -q __CSP_SCRIPT_HASHES__ /etc/nginx/templates/parcelflow/security-headers.conf.template \
 && rm /tmp/csp-script-hashes.txt

# Validates the environment and writes config.json (sourced before the envsubst step).
COPY --chmod=0755 docker/entrypoint/16-parcelflow-config.envsh /docker-entrypoint.d/16-parcelflow-config.envsh

# Runtime configuration (KEYCLOAK_URL is required and has no default).
# KEYCLOAK_CLIENT_ID defaults to the portal client, e.g. admin-portal -> parcelflow-admin.
ENV GATEWAY_URL=http://gateway:8080 \
    KEYCLOAK_REALM=parcelflow \
    KEYCLOAK_CLIENT_ID=parcelflow-${PORTAL%-portal} \
    API_BASE_URL="" \
    NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1 \
    NGINX_ENVSUBST_FILTER="^(GATEWAY_URL|KEYCLOAK_URL|NGINX_LOCAL_RESOLVERS)$"

USER 101

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/healthz > /dev/null || exit 1
