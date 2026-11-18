#!/bin/sh
# Daily backup of the ParcelFlow public demo, run on the VPS host (Dokploy "Server" schedule or cron,
# before the nightly reset).
#
# Usage: backup-demo.sh <compose project name>
#   The project name is the Dokploy appName of the compose application (`docker compose ls`).
#
# The nine databases are dumped one after the other, each in its own snapshot. To make them describe
# one state, the system is brought to rest first: the gateway is stopped (no client write), then the
# script waits until every RabbitMQ queue (error queues aside) and every service outbox is empty, so
# that no fact is still travelling between two databases. The dumps run with up to BACKUP_ATTEMPTS
# attempts BACKUP_RETRY_DELAY seconds apart, then the gateway starts again, whatever the outcome.
# Downtime: the quiescence and the dumps, usually a few seconds.
set -eu

services="customer shipment tracking warehouse delivery billing notification reporting"
databases="parcelflow_customers parcelflow_shipments parcelflow_tracking parcelflow_warehouse \
parcelflow_delivery parcelflow_billing parcelflow_notification parcelflow_reporting"
quiesce_timeout=${BACKUP_QUIESCE_TIMEOUT:-120}
attempts=${BACKUP_ATTEMPTS:-3}
retry_delay=${BACKUP_RETRY_DELAY:-300}
# Git Bash on Windows would rewrite the `/` vhost argument into a Windows path.
export MSYS_NO_PATHCONV=1

log() { printf '%s backup-demo: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
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

container() {
  ids=$(docker ps -aq --filter "label=com.docker.compose.project=$project" \
    --filter "label=com.docker.compose.service=$1")
  [ -n "$ids" ] || fail "no container for service '$1' in project '$project'."
  [ "$(printf '%s\n' "$ids" | wc -l)" -eq 1 ] || fail "several containers for service '$1' in project '$project'."
  printf '%s' "$ids"
}

backup=$(container backup)
gateway=$(container gateway)
postgres=$(container postgres)
rabbitmq=$(container rabbitmq)
for service in $services; do container "$service" >/dev/null; done

# Messages waiting or being processed in the queues, error queues excluded.
in_flight_messages() {
  docker exec "$rabbitmq" rabbitmqctl list_queues -p / -q --no-table-headers name messages |
    awk '$1 !~ /_error$/ { total += $2 } END { print total + 0 }'
}
# Messages still in the outboxes of the service databases.
pending_outbox() {
  total=0
  for database in $databases; do
    # shellcheck disable=SC2016 # expanded by the shell of the postgres container
    count=$(docker exec "$postgres" sh -c 'psql -Atq --username "$POSTGRES_USER" --dbname "$1" -c "SELECT count(*) FROM \"OutboxMessage\""' sh "$database")
    total=$((total + count))
  done
  echo "$total"
}

lock_dir="${TMPDIR:-/tmp}/parcelflow-reset-$project.lock"
mkdir "$lock_dir" 2>/dev/null || fail "a reset, a restore or a backup is running (remove $lock_dir if it is stale)."
stopped=false
on_exit() {
  status=$?
  if [ "$stopped" = true ]; then
    log "starting the gateway again"
    docker start "$gateway" >/dev/null 2>&1 || true
  fi
  rmdir "$lock_dir" 2>/dev/null || true
  exit "$status"
}
trap on_exit EXIT
trap 'exit 130' INT TERM

log "project $project: stopping the gateway"
stopped=true
docker stop "$gateway" >/dev/null

waited=0
while true; do
  messages=$(in_flight_messages)
  outbox=$(pending_outbox)
  [ "$messages" -eq 0 ] && [ "$outbox" -eq 0 ] && break
  if [ "$waited" -ge "$quiesce_timeout" ]; then
    log "WARNING: still $messages queued message(s) and $outbox outbox message(s) after ${quiesce_timeout}s: backing up anyway"
    break
  fi
  sleep 2
  waited=$((waited + 2))
done
[ "$waited" -ge "$quiesce_timeout" ] || log "at rest after ${waited}s: no queued message, empty outboxes"

attempt=1
until docker exec "$backup" parcelflow-backup now; do
  [ "$attempt" -lt "$attempts" ] || fail "backup failed after $attempts attempts."
  log "attempt $attempt failed, retrying in ${retry_delay}s"
  sleep "$retry_delay"
  attempt=$((attempt + 1))
done
log "backup completed"
