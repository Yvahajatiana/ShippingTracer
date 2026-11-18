# syntax=docker/dockerfile:1.7

# Logical backups of the ParcelFlow public demo: pg_dump of the eight service databases and of
# Keycloak (realm, users, clients), on a schedule, to a volume of the host. The scripts are baked
# into the image: Dokploy re-clones the repository on every deployment (see deploy/landing).
# Build from this directory: docker build -t parcelflow/backup deploy/pg-backup
# Same major version as the demo PostgreSQL, so pg_dump and pg_restore match the server.
FROM postgres:16-alpine

LABEL org.opencontainers.image.title="parcelflow-backup" \
      org.opencontainers.image.description="Scheduled pg_dump backups and restore of the ParcelFlow demo databases" \
      org.opencontainers.image.source="https://github.com/Yvahajatiana/ShippingTracer"

# Alpine security updates not yet picked up by the upstream image.
# The CI passes the build day: a new value invalidates this cached layer at least once a day.
ARG SECURITY_UPDATES_DATE=unset
RUN echo "Alpine security updates of ${SECURITY_UPDATES_DATE}" \
 && apk upgrade --no-cache \
 && install -d -o postgres -g postgres -m 0750 /backups

COPY --chmod=0755 parcelflow-backup parcelflow-restore /usr/local/bin/

USER postgres
VOLUME ["/backups"]
ENTRYPOINT ["/usr/local/bin/parcelflow-backup"]
CMD ["schedule"]
