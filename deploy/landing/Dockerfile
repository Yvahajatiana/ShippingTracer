# syntax=docker/dockerfile:1.7

# Static landing page of the public demo (parcelflow.<DEMO_DOMAIN>). The page and its nginx template
# are baked into the image: a bind mount of the cloned repository broke on every Dokploy redeployment
# that kept the container, since Dokploy deletes and re-clones that directory.
# Build from this directory: docker build -t parcelflow/landing deploy/landing
FROM nginxinc/nginx-unprivileged:1.30-alpine

LABEL org.opencontainers.image.title="parcelflow-landing" \
      org.opencontainers.image.description="ParcelFlow public demo landing page served by nginx" \
      org.opencontainers.image.source="https://github.com/Yvahajatiana/ShippingTracer"

USER root

# Alpine security updates not yet picked up by the upstream image, then drop the stock site.
# The CI passes the build day: a new value invalidates this cached layer at least once a day,
# so fixes published after the last cache write are picked up.
ARG SECURITY_UPDATES_DATE=unset
RUN echo "Alpine security updates of ${SECURITY_UPDATES_DATE}" \
 && apk upgrade --no-cache \
 && rm -f /etc/nginx/conf.d/default.conf \
 && rm -rf /usr/share/nginx/html/*

# Root-owned and read-only; the stock entrypoint renders the template (envsubst of DEMO_DOMAIN).
COPY site/ /usr/share/nginx/html/
COPY nginx.conf.template /etc/nginx/templates/default.conf.template

# Only DEMO_DOMAIN is substituted: nginx variables such as $uri stay untouched.
ENV NGINX_ENVSUBST_FILTER="^DEMO_DOMAIN$"

USER 101
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
