#!/bin/sh
# Nightly reset of the ParcelFlow public demo, run on the VPS host (cron or Dokploy "Server" schedule).
#
# Usage: reset-demo.sh <compose project name>
#   The project name is the Dokploy appName of the compose application (`docker compose ls`).
#   Containers are found by their compose labels: no compose file or environment is needed.
#
# Steps: stop the portals, the gateway, the eight services and Keycloak; drop and recreate the
# eight service databases and the Keycloak database; purge the RabbitMQ queues and flush Redis;
# start Keycloak (realm re-imported from the file), wait until it is healthy, re-run
# keycloak-demo-config; start the services (migrations and demo seeds at startup), the gateway
# and the portals; wait until the .NET containers are healthy.
# On failure the stopped containers are started again so that the demo does not stay down.
set -eu

services="customer shipment tracking warehouse delivery billing notification reporting"
frontends="gateway customer-portal operations-portal admin-portal"
databases="parcelflow_customers parcelflow_shipments parcelflow_tracking parcelflow_warehouse \
parcelflow_delivery parcelflow_billing parcelflow_notification parcelflow_reporting keycloak"
keycloak_timeout=${RESET_KEYCLOAK_TIMEOUT:-300}
services_timeout=${RESET_SERVICES_TIMEOUT:-300}

log() { printf '%s reset-demo: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
fail() {
  log "ERROR: $*" >&2
  exit 1
}

project=${1:-}
if [ -z "$project" ]; then
  echo "Usage: $0 <compose project name>" >&2
  exit 2
fi
case "$project" in
  *[!a-z0-9_-]* | [!a-z0-9]*) fail "invalid compose project name '$project'." ;;
esac
command -v docker >/dev/null 2>&1 || fail "docker is not available."

# Container id of a compose service of the project; exactly one container is expected.
container() {
  ids=$(docker ps -aq --filter "label=com.docker.compose.project=$project" \
    --filter "label=com.docker.compose.service=$1")
  [ -n "$ids" ] || fail "no container for service '$1' in project '$project'."
  [ "$(printf '%s\n' "$ids" | wc -l)" -eq 1 ] || fail "several containers for service '$1' in project '$project'."
  printf '%s' "$ids"
}

health() { docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1"; }

# Waits until the container reports healthy, at most $2 seconds.
wait_healthy() {
  waited=0
  while [ "$(health "$1")" != healthy ]; do
    [ "$waited" -lt "$2" ] || return 1
    sleep 5
    waited=$((waited + 5))
  done
}

# Resolve every container before stopping anything.
postgres=$(container postgres)
rabbitmq=$(container rabbitmq)
redis=$(container redis)
keycloak=$(container keycloak)
keycloak_config=$(container keycloak-demo-config)
app_containers=""
for service in $services $frontends; do
  app_containers="$app_containers $(container "$service")"
done
for infrastructure in "$postgres" "$rabbitmq" "$redis"; do
  [ "$(docker inspect -f '{{.State.Running}}' "$infrastructure")" = true ] \
    || fail "infrastructure container $infrastructure is not running."
done

lock_dir="${TMPDIR:-/tmp}/parcelflow-reset-$project.lock"
mkdir "$lock_dir" 2>/dev/null || fail "another reset is running (remove $lock_dir if it is stale)."

stopped=false
on_exit() {
  status=$?
  if [ "$status" -ne 0 ] && [ "$stopped" = true ]; then
    log "reset failed: starting the stopped containers again"
    # shellcheck disable=SC2086 # word splitting of the id list is intended
    docker start "$keycloak" $app_containers >/dev/null 2>&1 || true
    # Detached: the one-shot retries its sign-in until Keycloak is ready.
    docker start "$keycloak_config" >/dev/null 2>&1 || true
  fi
  rmdir "$lock_dir" 2>/dev/null || true
  exit "$status"
}
trap on_exit EXIT
trap 'exit 130' INT TERM

log "project $project: stopping the portals, the gateway, the services and Keycloak"
stopped=true
# shellcheck disable=SC2086 # word splitting of the id list is intended
docker stop $app_containers "$keycloak" >/dev/null

for database in $databases; do
  log "recreating database $database"
  # shellcheck disable=SC2016 # expanded by the shell of the postgres container
  docker exec "$postgres" sh -c 'psql -q -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -c "DROP DATABASE IF EXISTS $1 WITH (FORCE);" -c "CREATE DATABASE $1 OWNER \"$POSTGRES_USER\";"' sh "$database"
done

log "purging the RabbitMQ queues"
queues=$(docker exec "$rabbitmq" rabbitmqctl list_queues -p / -q --no-table-headers name)
for queue in $queues; do
  docker exec "$rabbitmq" rabbitmqctl purge_queue -p / -q "$queue" >/dev/null
done

log "flushing Redis"
docker exec "$redis" redis-cli FLUSHALL >/dev/null

log "starting Keycloak (realm import into the empty database)"
docker start "$keycloak" >/dev/null
wait_healthy "$keycloak" "$keycloak_timeout" || fail "Keycloak is not healthy after ${keycloak_timeout}s."

log "running keycloak-demo-config"
docker start -a "$keycloak_config"
[ "$(docker inspect -f '{{.State.ExitCode}}' "$keycloak_config")" = 0 ] || fail "keycloak-demo-config failed."

log "starting the services, the gateway and the portals"
# shellcheck disable=SC2086 # word splitting of the id list is intended
docker start $app_containers >/dev/null
stopped=false

unhealthy=""
for service in $services gateway; do
  id=$(container "$service")
  wait_healthy "$id" "$services_timeout" || unhealthy="$unhealthy $service"
done
[ -z "$unhealthy" ] || fail "not healthy after ${services_timeout}s:$unhealthy."
log "demo reset completed"
