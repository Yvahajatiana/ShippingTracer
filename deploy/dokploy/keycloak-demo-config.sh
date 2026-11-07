#!/bin/bash
# Adapts the imported `parcelflow` realm to the public demo. Idempotent: run by the
# `keycloak-demo-config` one-shot service after every deployment and after the nightly reset.
#   - portal clients: redirect URIs, web origins and post-logout URIs limited to the https demo origins;
#   - brute force protection off: the demo passwords are public, a lockout would only let visitors
#     lock each other out;
#   - no self-service on the shared accounts: account console management roles removed from the
#     default roles, and every required action except the locale one disabled, so that no visitor
#     can change the password, the profile or add an OTP through an application-initiated action;
#   - login page banner (realm displayNameHtml) listing the demo accounts and their password.
#
# Environment:
#   KEYCLOAK_URL             internal Keycloak URL, e.g. http://keycloak:8080
#   KEYCLOAK_ADMIN_PASSWORD  password of the bootstrap administrator `admin` (master realm)
#   DEMO_DOMAIN              public domain, portals on app./ops./admin.<DEMO_DOMAIN>
#   DEMO_USER_PASSWORD       shared password of the demo accounts, shown on the login page
set -euo pipefail

realm=parcelflow
kcadm=/opt/keycloak/bin/kcadm.sh
config=/tmp/kcadm.config

log() { echo "keycloak-demo-config: $*"; }
fail() {
  echo "keycloak-demo-config: ERROR: $*" >&2
  exit 1
}

: "${KEYCLOAK_URL:?KEYCLOAK_URL is required}"
: "${KEYCLOAK_ADMIN_PASSWORD:?KEYCLOAK_ADMIN_PASSWORD is required}"
: "${DEMO_DOMAIN:?DEMO_DOMAIN is required}"
: "${DEMO_USER_PASSWORD:?DEMO_USER_PASSWORD is required}"

label='[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?'
[[ "$DEMO_DOMAIN" =~ ^${label}(\.${label})+$ ]] || fail "DEMO_DOMAIN must be a domain name such as example.com, got '$DEMO_DOMAIN'."

kc() { "$kcadm" "$@" --config "$config"; }

# Escapes a value for an HTML text node or attribute. Replacements are quoted: since bash 5.2
# (patsub_replacement), an unquoted & in a replacement stands for the matched text.
html_escape() {
  local value=$1
  value=${value//&/"&amp;"}
  value=${value//</"&lt;"}
  value=${value//>/"&gt;"}
  value=${value//\"/"&quot;"}
  value=${value//\'/"&#39;"}
  printf '%s' "$value"
}

log "signing in to $KEYCLOAK_URL as the bootstrap administrator"
for attempt in $(seq 1 30); do
  if kc config credentials --server "$KEYCLOAK_URL" --realm master --user admin \
    --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null 2>&1; then
    break
  fi
  [[ "$attempt" -lt 30 ]] || fail "unable to sign in to $KEYCLOAK_URL (check KEYCLOAK_ADMIN_PASSWORD)."
  sleep 5
done

configure_client() {
  local client_id=$1 origin=$2 id
  id=$(kc get clients -r "$realm" -q "clientId=$client_id" --fields id --format csv --noquotes)
  [[ -n "$id" ]] || fail "client $client_id not found in realm $realm."
  kc update "clients/$id" -r "$realm" \
    -s "redirectUris=[\"$origin/*\"]" \
    -s "webOrigins=[\"$origin\"]" \
    -s "attributes.\"post.logout.redirect.uris\"=$origin/*"
  log "client $client_id limited to $origin"
}

configure_client parcelflow-customer "https://app.$DEMO_DOMAIN"
configure_client parcelflow-operations "https://ops.$DEMO_DOMAIN"
configure_client parcelflow-admin "https://admin.$DEMO_DOMAIN"

# Numeric entities keep the banner ASCII whatever the locale of the JVM.
escaped_password=$(html_escape "$DEMO_USER_PASSWORD")
banner="<strong>ParcelFlow</strong><br><small>D&#233;mo publique, donn&#233;es r&#233;initialis&#233;es chaque nuit.<br>"
banner+="Client : <strong>emma.martin</strong> &#183; Op&#233;rateur : <strong>chloe.leroy</strong> &#183; "
banner+="Admin : <strong>admin.parcelflow</strong><br>Mot de passe : <strong>$escaped_password</strong></small>"
kc update "realms/$realm" -s bruteForceProtected=false -s "displayNameHtml=$banner"
log "realm $realm: brute force protection off, demo banner set"

# manage-account also carries manage-account-links as a composite; removing absent roles is a no-op.
kc remove-roles -r "$realm" --rname "default-roles-$realm" --cclientid account \
  --rolename manage-account --rolename manage-account-links
log "account console management roles removed from default-roles-$realm"

# Captured first so that a failed request stops the script (set -e does not see process substitutions).
actions=$(kc get authentication/required-actions -r "$realm" --fields alias,enabled --format csv --noquotes)
while IFS=, read -r alias enabled; do
  [[ -n "$alias" && "$alias" != update_user_locale && "$enabled" == true ]] || continue
  kc update "authentication/required-actions/$alias" -r "$realm" -s enabled=false
  log "required action $alias disabled"
done <<< "$actions"

log "done"
