#!/usr/bin/env bash
# ParcelFlow on a local minikube cluster: install, port-forward, uninstall (bash twin of local.ps1).
#
# Usage: deploy/kubernetes/local.sh <up|forward|down> [options]
#   up       [--start-cluster] [--skip-image-load] [--timeout 15m]
#            Loads the locally built images (docker compose build) into minikube and installs the
#            chart with values-local.yaml, the secrets of the repository .env and the realm file.
#   forward  [--address 127.0.0.1] [--detach] [--stop]
#            Port-forwards the portals (4201-4203), Keycloak (8180), the gateway (5100) and the
#            RabbitMQ console (15672). Runs until Ctrl+C, or in the background with --detach.
#   down     [--delete-namespace] [--stop-cluster]
#            Stops the port-forwards and uninstalls the release.
# Common options: --namespace parcelflow, --release parcelflow.
#
# Safety: every helm / kubectl call targets the context named by PARCELFLOW_KUBE_CONTEXT (default
# "parcelflow") explicitly, and the script refuses to run unless that context is a minikube cluster.
#
# Environment: PARCELFLOW_KUBE_CONTEXT, PARCELFLOW_MINIKUBE_CPUS (4), PARCELFLOW_MINIKUBE_MEMORY (8g),
# PARCELFLOW_MINIKUBE_CNI (empty, "calico" enforces the network policies), HELM, KUBECTL, MINIKUBE.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
chart_path="$repo_root/deploy/helm/parcelflow"
values_file="$script_dir/values-local.yaml"
realm_file="$repo_root/deploy/keycloak/parcelflow-realm.json"
env_file="$repo_root/.env"

kube_context="${PARCELFLOW_KUBE_CONTEXT:-parcelflow}"
helm="${HELM:-helm}"
kubectl="${KUBECTL:-kubectl}"
minikube="${MINIKUBE:-minikube}"

namespace=parcelflow
release=parcelflow
start_cluster=false
skip_image_load=false
timeout=15m
address=127.0.0.1
detach=false
stop=false
delete_namespace=false
stop_cluster=false

# Chart secret key -> docker compose .env variable.
secret_keys=(postgres-password rabbitmq-password keycloak-admin-password notification-client-secret demo-user-password)
secret_vars=(POSTGRES_PASSWORD RABBITMQ_PASSWORD KEYCLOAK_ADMIN_PASSWORD NOTIFICATION_CLIENT_SECRET DEMO_USER_PASSWORD)

# The 13 images built by docker compose (parcelflow/<name>:<tag>).
images=(customer shipment tracking warehouse delivery billing notification reporting
  gateway customer-portal operations-portal admin-portal rabbitmq)

usage() {
  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

step() { printf '\033[36m==> %s\033[0m\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }
require_tool() { command -v "$1" > /dev/null 2>&1 || die "'$1' not found. $2"; }

[[ $# -ge 1 ]] || usage 1
command="$1"
shift
while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace) namespace="$2"; shift 2 ;;
    --release) release="$2"; shift 2 ;;
    --start-cluster) start_cluster=true; shift ;;
    --skip-image-load) skip_image_load=true; shift ;;
    --timeout) timeout="$2"; shift 2 ;;
    --address) address="$2"; shift 2 ;;
    --detach) detach=true; shift ;;
    --stop) stop=true; shift ;;
    --delete-namespace) delete_namespace=true; shift ;;
    --stop-cluster) stop_cluster=true; shift ;;
    -h|--help) usage 0 ;;
    *) die "unknown option $1" ;;
  esac
done

# Resource prefix, as computed by the parcelflow.fullname helper.
if [[ "$release" == *parcelflow* ]]; then prefix="$release"; else prefix="$release-parcelflow"; fi

# port|service|target port|name|url
forwards=(
  "4201|$prefix-customer-portal|8080|customer portal|http://localhost:4201"
  "4202|$prefix-operations-portal|8080|operations portal|http://localhost:4202"
  "4203|$prefix-admin-portal|8080|admin portal|http://localhost:4203"
  "8180|$prefix-keycloak|8080|Keycloak|http://localhost:8180/admin"
  "5100|$prefix-gateway|8080|gateway|http://localhost:5100"
  "15672|$prefix-rabbitmq|15672|RabbitMQ console|http://localhost:15672"
)

state_dir="${TMPDIR:-/tmp}/parcelflow-k8s-$kube_context-$namespace"
state_file="$state_dir/port-forward.pids"

cluster_running() {
  local state
  state="$("$minikube" status -p "$kube_context" --format '{{.Host}}/{{.APIServer}}' 2> /dev/null || true)"
  [[ "$(echo "$state" | tr -d '[:space:]')" == "Running/Running" ]]
}

# Refuses any context that is not the minikube cluster of the profile. Reads the kubeconfig only.
assert_local_context() {
  local cluster provider
  cluster="$("$kubectl" config view -o "jsonpath={.contexts[?(@.name==\"$kube_context\")].context.cluster}" 2> /dev/null || true)"
  [[ -n "$cluster" ]] || die "kube context '$kube_context' not found. Create it with: minikube start -p $kube_context --driver=docker (or 'up --start-cluster')."
  provider="$("$kubectl" config view -o "jsonpath={.clusters[?(@.name==\"$cluster\")].cluster.extensions[*].extension.provider}" 2> /dev/null || true)"
  [[ " $provider " == *" minikube.sigs.k8s.io "* ]] ||
    die "kube context '$kube_context' is not a minikube cluster: refusing to touch it. Set PARCELFLOW_KUBE_CONTEXT to a minikube profile."
}

# Value of a variable in .env (last occurrence), optional surrounding quotes removed.
dotenv_value() {
  local line key value result=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    line="${line#"${line%%[![:space:]]*}"}"
    [[ -z "$line" || "$line" == \#* || "$line" != *=* ]] && continue
    key="${line%%=*}"
    key="${key#export }"
    key="${key%"${key##*[![:space:]]}"}"
    key="${key#"${key%%[![:space:]]*}"}"
    [[ "$key" == "$1" ]] || continue
    value="${line#*=}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    if [[ ${#value} -ge 2 && ( ( "${value:0:1}" == '"' && "${value: -1}" == '"' ) || ( "${value:0:1}" == "'" && "${value: -1}" == "'" ) ) ]]; then
      value="${value:1:${#value}-2}"
    fi
    result="$value"
  done < "$env_file"
  printf '%s' "$result"
}

# YAML single-quoted scalar: only the quote itself needs escaping.
yaml_string() {
  local value="${1//\'/\'\'}"
  printf "'%s'" "$value"
}

cmd_up() {
  require_tool "$minikube" "Install minikube: https://minikube.sigs.k8s.io/docs/start/"
  require_tool "$kubectl" "Install kubectl or set KUBECTL."
  require_tool "$helm" "Install Helm 3 or set HELM."

  if ! cluster_running; then
    $start_cluster || die "minikube profile '$kube_context' is not running. Start it with 'up --start-cluster' or: minikube start -p $kube_context --driver=docker --cpus 4 --memory 8g"
    # --keep-context: never switch the user's current kubectl context to minikube.
    local start_args=(start -p "$kube_context" --keep-context --driver=docker "--cpus=${PARCELFLOW_MINIKUBE_CPUS:-4}" "--memory=${PARCELFLOW_MINIKUBE_MEMORY:-8g}")
    [[ -n "${PARCELFLOW_MINIKUBE_CNI:-}" ]] && start_args+=("--cni=$PARCELFLOW_MINIKUBE_CNI")
    # minikube still switches the current context when it restarts an existing profile: put the
    # user's context back afterwards.
    local previous_context
    previous_context=$("$kubectl" config current-context 2>/dev/null || true)
    step "Starting minikube profile $kube_context"
    "$minikube" "${start_args[@]}"
    if [[ -n "$previous_context" && "$previous_context" != "$kube_context" ]]; then
      "$kubectl" config use-context "$previous_context" >/dev/null
    fi
  fi
  assert_local_context

  [[ -f "$env_file" ]] || die "missing $env_file. Copy .env.example to .env and set every secret."
  local i missing=()
  for i in "${!secret_vars[@]}"; do
    [[ -n "$(dotenv_value "${secret_vars[$i]}")" ]] || missing+=("${secret_vars[$i]}")
  done
  [[ ${#missing[@]} -eq 0 ]] || die "set ${missing[*]} in $env_file."
  local tag
  tag="$(dotenv_value PARCELFLOW_TAG)"
  tag="${tag:-local}"

  if ! $skip_image_load; then
    require_tool docker "Docker is needed to read the locally built images."
    local name image
    for name in "${images[@]}"; do
      image="parcelflow/$name:$tag"
      docker image inspect "$image" > /dev/null 2>&1 || die "image $image not found locally. Build the images first: docker compose build"
      step "Loading $image into minikube"
      "$minikube" -p "$kube_context" image load "$image"
    done
  fi

  # Secrets go through a temporary values file (nothing on the command line), deleted on exit.
  secret_dir="$(mktemp -d)"
  chmod 700 "$secret_dir"
  trap 'rm -rf "$secret_dir"' EXIT
  {
    echo "secrets:"
    echo "  values:"
    for i in "${!secret_keys[@]}"; do
      printf '    %s: %s\n' "${secret_keys[$i]}" "$(yaml_string "$(dotenv_value "${secret_vars[$i]}")")"
    done
  } > "$secret_dir/secrets.yaml"

  step "Installing release $release in namespace $namespace (context $kube_context)"
  "$helm" upgrade --install "$release" "$chart_path" \
    --kube-context "$kube_context" --namespace "$namespace" --create-namespace \
    -f "$values_file" -f "$secret_dir/secrets.yaml" \
    --set-file "infrastructure.keycloak.realm=$realm_file" \
    --set-string "image.tag=$tag" \
    --wait --timeout "$timeout"
  rm -rf "$secret_dir"

  "$kubectl" --context "$kube_context" -n "$namespace" get pods
  echo
  echo "Installed. Run 'deploy/kubernetes/local.sh forward' to reach the platform."
  echo "Images rebuilt with the same tag are not picked up automatically: reload them, then run"
  echo "  kubectl --context $kube_context -n $namespace rollout restart deployment"
}

stop_forwards() {
  [[ -f "$state_file" ]] || return 0
  local port pid
  while read -r port pid; do
    [[ -n "${pid:-}" ]] || continue
    # Only kill the process when it is still kubectl (pid reuse). Git Bash's ps has no -o.
    if { ps -p "$pid" -o args= 2> /dev/null || ps -p "$pid" 2> /dev/null; } | grep -q kubectl; then
      kill "$pid" 2> /dev/null || true
      echo "Stopped port-forward $port (pid $pid)"
    fi
  done < "$state_file"
  rm -f "$state_file"
}

port_in_use() {
  # bash /dev/tcp: a successful connection means something already listens there.
  (exec 3<> "/dev/tcp/$1/$2") 2> /dev/null
}

# Pid of each port-forward, indexed like "forwards" (indexed array: bash 3.2 compatible).
forward_pids=()

start_forward() {
  local index="$1" port service target name url
  IFS='|' read -r port service target name url <<< "${forwards[$index]}"
  "$kubectl" --context "$kube_context" -n "$namespace" port-forward --address "$address" \
    "svc/$service" "$port:$target" > "$state_dir/port-forward-$port.log" 2>&1 &
  forward_pids[$index]=$!
}

write_state() {
  local index port rest
  : > "$state_file"
  for index in "${!forwards[@]}"; do
    IFS='|' read -r port rest <<< "${forwards[$index]}"
    echo "$port ${forward_pids[$index]}" >> "$state_file"
  done
}

cmd_forward() {
  if $stop; then
    stop_forwards
    return
  fi
  require_tool "$kubectl" "Install kubectl or set KUBECTL."
  assert_local_context
  stop_forwards
  mkdir -p "$state_dir"

  local entry port service target name url
  for entry in "${forwards[@]}"; do
    IFS='|' read -r port service target name url <<< "$entry"
    if [[ "$address" =~ ^[0-9.]+$ ]] && port_in_use "$address" "$port"; then
      die "port $address:$port is already in use ($name)."
    fi
    if [[ "$address" == 127.0.0.1 ]] && port_in_use ::1 "$port"; then
      echo "warning: another process listens on [::1]:$port; if localhost resolves to it, use http://127.0.0.1:$port or stop that process." >&2
    fi
  done

  local index
  for index in "${!forwards[@]}"; do
    start_forward "$index"
  done
  write_state

  echo
  for entry in "${forwards[@]}"; do
    IFS='|' read -r port service target name url <<< "$entry"
    printf '  %-18s %s\n' "$name" "$url"
  done
  echo
  echo "Logs: $state_dir"

  if $detach; then
    # Detach the jobs from this shell so that they survive it.
    disown -a
    echo "Port-forwards running in the background. Stop them with: deploy/kubernetes/local.sh forward --stop"
    return
  fi

  echo "Press Ctrl+C to stop the port-forwards."
  trap 'stop_forwards; exit 0' INT TERM
  trap 'stop_forwards' EXIT
  while true; do
    sleep 2
    # A port-forward ends when its pod restarts: start it again.
    for index in "${!forwards[@]}"; do
      if ! kill -0 "${forward_pids[$index]}" 2> /dev/null; then
        IFS='|' read -r port service target name url <<< "${forwards[$index]}"
        echo "warning: port-forward $port ($name) stopped, restarting it." >&2
        start_forward "$index"
        write_state
      fi
    done
  done
}

cmd_down() {
  stop_forwards
  require_tool "$kubectl" "Install kubectl or set KUBECTL."
  require_tool "$helm" "Install Helm 3 or set HELM."
  assert_local_context

  if "$helm" status "$release" --kube-context "$kube_context" --namespace "$namespace" > /dev/null 2>&1; then
    step "Uninstalling release $release from namespace $namespace (context $kube_context)"
    "$helm" uninstall "$release" --kube-context "$kube_context" --namespace "$namespace" --wait
  else
    echo "Release $release not found in namespace $namespace."
  fi

  if $delete_namespace; then
    step "Deleting namespace $namespace (volumes included)"
    "$kubectl" --context "$kube_context" delete namespace "$namespace" --ignore-not-found --wait=true
  else
    echo "PostgreSQL and RabbitMQ volumes are kept (PersistentVolumeClaims in $namespace). Use --delete-namespace to remove them."
  fi

  if $stop_cluster; then
    step "Stopping minikube profile $kube_context"
    "$minikube" stop -p "$kube_context"
  fi
}

case "$command" in
  up) cmd_up ;;
  forward) cmd_forward ;;
  down) cmd_down ;;
  -h|--help) usage 0 ;;
  *) usage 1 ;;
esac
