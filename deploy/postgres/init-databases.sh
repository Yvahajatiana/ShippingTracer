#!/bin/sh
# Creates one database per ParcelFlow service, owned by the application role.
# Runs once, on the first start of an empty Postgres volume (docker-entrypoint-initdb.d).
set -eu

for database in \
  parcelflow_customers \
  parcelflow_shipments \
  parcelflow_tracking \
  parcelflow_warehouse \
  parcelflow_delivery \
  parcelflow_billing \
  parcelflow_notification \
  parcelflow_reporting
do
  echo "Creating database ${database}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -c "CREATE DATABASE ${database} OWNER \"${POSTGRES_USER}\";"
done
