#!/bin/sh
# Lists the MassTransit error queues of a ParcelFlow compose stack, or moves the messages of one of
# them back to its original queue once the cause of the failures is fixed. Run on the Docker host
# (VPS of the demo, or a local machine).
#
# Usage: replay-error-queue.sh <compose project name> list
#        replay-error-queue.sh <compose project name> replay <queue>
#   <queue> is the consumer queue, with or without its `_error` suffix
#   (e.g. notification-shipment-created). The project name is the Dokploy appName of the compose
#   application, or `parcelflow` locally (`docker compose ls`).
#
# The replay declares a dynamic shovel (rabbitmq_shovel plugin) that moves the messages present in
# `<queue>_error` to `<queue>`, then deletes itself. MassTransit keeps the MessageId (= EventId) and
# the Inbox of each service drops a message already consumed, so a replay never applies a fact twice.
# Messages that fail again go back to `<queue>_error`.
set -eu
# Git Bash on Windows would rewrite the `/` vhost argument into a Windows path.
export MSYS_NO_PATHCONV=1

timeout=${REPLAY_TIMEOUT:-120}

log() { printf '%s replay-error-queue: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
fail() {
  log "ERROR: $*" >&2
  exit 1
}
usage() {
  echo "Usage: $0 <compose project name> list" >&2
  echo "       $0 <compose project name> replay <queue>" >&2
  exit 2
}

project=${1:-}
action=${2:-}
[ -n "$project" ] && [ -n "$action" ] || usage
case "$project" in
  *[!a-z0-9_-]* | [!a-z0-9]*) fail "invalid compose project name '$project'." ;;
esac
command -v docker >/dev/null 2>&1 || fail "docker is not available."

rabbitmq=$(docker ps -q --filter "label=com.docker.compose.project=$project" --filter "label=com.docker.compose.service=rabbitmq")
[ -n "$rabbitmq" ] || fail "no running rabbitmq container in project '$project'."
[ "$(printf '%s\n' "$rabbitmq" | wc -l)" -eq 1 ] || fail "several rabbitmq containers in project '$project'."

ctl() { docker exec "$rabbitmq" rabbitmqctl -q "$@"; }
# Number of ready messages of a queue, empty when the queue does not exist.
depth() { ctl list_queues -p / --no-table-headers name messages | awk -v queue="$1" '$1 == queue { print $2 }'; }

case "$action" in
  list)
    [ $# -eq 2 ] || usage
    ctl list_queues -p / --no-table-headers name messages consumers |
      awk 'BEGIN { printf "%-60s %10s\n", "error queue", "messages" } $1 ~ /_error$/ { printf "%-60s %10s\n", $1, $2 }'
    ;;
  replay)
    [ $# -eq 3 ] || usage
    queue=${3%_error}
    case "$queue" in
      *[!a-z0-9._:-]* | '') fail "invalid queue name '$3'." ;;
    esac
    errors=$(depth "${queue}_error")
    [ -n "$errors" ] || fail "queue '${queue}_error' does not exist."
    [ -n "$(depth "$queue")" ] || fail "original queue '$queue' does not exist."
    [ "$errors" -gt 0 ] || { log "'${queue}_error' is empty: nothing to replay."; exit 0; }

    # The shovel connects with the broker's own credentials (direct connection, never exposed).
    user=$(docker exec "$rabbitmq" printenv RABBITMQ_DEFAULT_USER)
    password=$(docker exec "$rabbitmq" printenv RABBITMQ_DEFAULT_PASS)
    encoded=$(printf '%s' "$password" | od -An -tx1 | tr -d ' \n' | sed 's/../%&/g')
    shovel="replay-$queue"
    ctl list_parameters -p / --no-table-headers | awk '{ print $2 }' | grep -qx "$shovel" &&
      fail "a replay of '$queue' is already running (shovel '$shovel')."

    log "moving $errors message(s) from '${queue}_error' to '$queue'."
    ctl set_parameter -p / shovel "$shovel" "{\"src-protocol\": \"amqp091\", \"src-uri\": \"amqp://$user:$encoded@localhost\",
      \"src-queue\": \"${queue}_error\", \"src-delete-after\": \"queue-length\",
      \"dest-protocol\": \"amqp091\", \"dest-uri\": \"amqp://$user:$encoded@localhost\", \"dest-queue\": \"$queue\",
      \"ack-mode\": \"on-confirm\"}" >/dev/null

    # src-delete-after removes the shovel once the messages present at its start have been moved.
    waited=0
    while ctl list_parameters -p / --no-table-headers | awk '{ print $2 }' | grep -qx "$shovel"; do
      if [ "$waited" -ge "$timeout" ]; then
        ctl clear_parameter -p / shovel "$shovel" >/dev/null || true
        fail "the replay did not finish within ${timeout}s; shovel '$shovel' removed, check '${queue}_error'."
      fi
      sleep 2
      waited=$((waited + 2))
    done
    log "done: '${queue}_error' now holds $(depth "${queue}_error") message(s) (failed again, if any)."
    ;;
  *) usage ;;
esac
