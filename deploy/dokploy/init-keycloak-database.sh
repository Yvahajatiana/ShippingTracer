#!/bin/sh
# Creates the Keycloak database of the public demo, owned by the application role.
# Runs once, on the first start of an empty Postgres volume (docker-entrypoint-initdb.d),
# after deploy/postgres/init-databases.sh has created the eight service databases.
set -eu

echo "Creating database keycloak"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -c "CREATE DATABASE keycloak OWNER \"${POSTGRES_USER}\";"
