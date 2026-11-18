#!/bin/sh
# Restores the ParcelFlow public demo from a snapshot of the backup container, run on the VPS host.
#
# Usage: restore-demo.sh <compose project name> list
#        restore-demo.sh <compose project name> <snapshot | latest>
#   The project name is the Dokploy appName of the compose application (`docker compose ls`).
#   Containers are found by their compose labels, like reset-demo.sh.
#
# Every database comes back from the SAME snapshot: the services exchange facts through their
# outboxes and inboxes, so restoring a single database would leave the others ahead of it (see
# docs/demo.md, "Sauvegardes"). Steps: stop the portals, the gateway, the services and Keycloak;
# restore the nine databases; purge the RabbitMQ queues (messages newer than the snapshot) and flush
# Redis (cached reads); start Keycloak, re-run keycloak-demo-config, start the services, the gateway
# and the portals; wait until they are healthy and print the elapsed time (measured RTO).
set -eu

services="customer shipment tracking warehouse delivery billing notification reporting"
frontends="gateway customer-portal operations-portal admin-portal"
keycloak_timeout=${RESTORE_KEYCLOAK_TIMEOUT:-300}
services_timeout=${RESTORE_SERVICES_TIMEOUT:-300}
# Git Bash on Windows would rewrite the `/` vhost argument into a Windows path.
export MSYS_NO_PATHCONV=1

log() { printf '%s restore-demo: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
fail() {
  log "ERROR: $*" >&2
  exit 1
}

project=${1:-}
target=${2:-}
if [ -z "$project" ] || [ -z "$target" ]; then
  echo "Usage: $0 <compose project name> list | <snapshot | latest>" >&2
  exit 2
fi
case "$project" in
  *[!a-z0-9_-]* | [!a-z0-9]*) fail "invalid compose project name '$project'." ;;
esac
command -v docker >/dev/null 2>&1 || fail "docker is not available."

container() {
  ids=$(docker ps -aq --filter "label=com.docker.compose.project=$project" \
    --filter "label=com.docker.compose.service=$1")
  [ -n "$ids" ] || fail "no container for service '$1' in project '$project'."
  [ "$(printf '%s\n' "$ids" | wc -l)" -eq 1 ] || fail "several containers for service '$1' in project '$project'."
  printf '%s' "$ids"
}
health() { docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1"; }
wait_healthy() {
  waited=0
  while [ "$(health "$1")" != healthy ]; do
    [ "$waited" -lt "$2" ] || return 1
    sleep 5
    waited=$((waited + 5))
  done
}

backup=$(container backup)
if [ "$target" = list ]; then
  docker exec "$backup" parcelflow-backup list
  exit 0
fi

started=$(date -u +%s)
rabbitmq=$(container rabbitmq)
redis=$(container redis)
keycloak=$(container keycloak)
keycloak_config=$(container keycloak-demo-config)
app_containers=""
for service in $services $frontends; do
  app_containers="$app_containers $(container "$service")"
done

lock_dir="${TMPDIR:-/tmp}/parcelflow-reset-$project.lock"
mkdir "$lock_dir" 2>/dev/null || fail "a reset, a restore or a backup is running (remove $lock_dir if it is stale)."

stopped=false
on_exit() {
  status=$?
  if [ "$status" -ne 0 ] && [ "$stopped" = true ]; then
    log "restore failed: starting the stopped containers again (check the databases before reopening)"
    # shellcheck disable=SC2086 # word splitting of the id list is intended
    docker start "$keycloak" $app_containers >/dev/null 2>&1 || true
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

docker exec "$backup" parcelflow-restore "$target"

log "purging the RabbitMQ queues"
queues=$(docker exec "$rabbitmq" rabbitmqctl list_queues -p / -q --no-table-headers name)
for queue in $queues; do
  docker exec "$rabbitmq" rabbitmqctl purge_queue -p / -q "$queue" >/dev/null
done
log "flushing Redis"
docker exec "$redis" redis-cli FLUSHALL >/dev/null

log "starting Keycloak"
docker start "$keycloak" >/dev/null
wait_healthy "$keycloak" "$keycloak_timeout" || fail "Keycloak is not healthy after ${keycloak_timeout}s."
docker start -a "$keycloak_config"
[ "$(docker inspect -f '{{.State.ExitCode}}' "$keycloak_config")" = 0 ] || fail "keycloak-demo-config failed."

log "starting the services, the gateway and the portals"
# shellcheck disable=SC2086 # word splitting of the id list is intended
docker start $app_containers >/dev/null
stopped=false
unhealthy=""
for service in $services gateway; do
  wait_healthy "$(container "$service")" "$services_timeout" || unhealthy="$unhealthy $service"
done
[ -z "$unhealthy" ] || fail "not healthy after ${services_timeout}s:$unhealthy."
log "demo restored from $target in $(($(date -u +%s) - started))s (measured RTO)"
