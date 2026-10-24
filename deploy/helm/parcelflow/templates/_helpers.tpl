{{/*
Shared naming, labelling and endpoint helpers. Every template builds names and addresses through
these helpers so that application and infrastructure components agree on them.
*/}}

{{- define "parcelflow.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "parcelflow.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 40 | trimSuffix "-" -}}
{{- else if contains (include "parcelflow.name" .) .Release.Name -}}
{{- .Release.Name | trunc 40 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "parcelflow.name" .) | trunc 40 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "parcelflow.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* Resource name of a component. Usage: include "parcelflow.componentName" (dict "root" $ "component" "gateway") */}}
{{- define "parcelflow.componentName" -}}
{{- printf "%s-%s" (include "parcelflow.fullname" .root) .component | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* Selector labels of a component. Usage: include "parcelflow.selectorLabels" (dict "root" $ "component" "gateway") */}}
{{- define "parcelflow.selectorLabels" -}}
app.kubernetes.io/name: {{ include "parcelflow.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{/* Common labels of a component. Usage: include "parcelflow.labels" (dict "root" $ "component" "gateway") */}}
{{- define "parcelflow.labels" -}}
helm.sh/chart: {{ include "parcelflow.chart" .root }}
{{ include "parcelflow.selectorLabels" . }}
app.kubernetes.io/version: {{ include "parcelflow.imageTag" .root | quote }}
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
app.kubernetes.io/part-of: parcelflow
{{- end -}}

{{- define "parcelflow.imageTag" -}}
{{- default .Chart.AppVersion .Values.image.tag -}}
{{- end -}}

{{/*
Image reference of a ParcelFlow image: <registry>/<prefix><name>:<tag>, e.g.
ghcr.io/yvahajatiana/parcelflow-gateway:develop (CI) or parcelflow/gateway:local (compose build).
Usage: include "parcelflow.image" (dict "root" $ "name" "gateway")
*/}}
{{- define "parcelflow.image" -}}
{{- $image := .root.Values.image -}}
{{- printf "%s/%s%s:%s" (trimSuffix "/" $image.registry) $image.prefix .name (include "parcelflow.imageTag" .root) -}}
{{- end -}}

{{/* Name of the Secret holding the application secrets (see values.yaml "secrets"). */}}
{{- define "parcelflow.secretName" -}}
{{- if .Values.secrets.existingSecret -}}
{{- .Values.secrets.existingSecret -}}
{{- else -}}
{{- include "parcelflow.componentName" (dict "root" . "component" "secrets") -}}
{{- end -}}
{{- end -}}

{{- define "parcelflow.serviceAccountName" -}}
{{- default (include "parcelflow.fullname" .) .Values.serviceAccount.name -}}
{{- end -}}

{{/* In-cluster base URL of a component (trailing slash), e.g. http://parcelflow-shipment.ns.svc.cluster.local:8080/ */}}
{{- define "parcelflow.componentUrl" -}}
{{- printf "http://%s.%s.svc.%s:8080/" (include "parcelflow.componentName" .) .root.Release.Namespace .root.Values.clusterDomain -}}
{{- end -}}

{{/* PostgreSQL host: the bundled development instance, or the external server. */}}
{{- define "parcelflow.databaseHost" -}}
{{- if .Values.infrastructure.enabled -}}
{{- include "parcelflow.componentName" (dict "root" . "component" "postgres") -}}
{{- else -}}
{{- required "database.host is required when infrastructure.enabled is false" .Values.database.host -}}
{{- end -}}
{{- end -}}

{{/* Redis connection string (without password, see redis.passwordFromSecret). */}}
{{- define "parcelflow.redisConnection" -}}
{{- if .Values.infrastructure.enabled -}}
{{- printf "%s:6379,abortConnect=false,connectTimeout=2000,syncTimeout=2000" (include "parcelflow.componentName" (dict "root" . "component" "redis")) -}}
{{- else -}}
{{- required "redis.connectionString is required when infrastructure.enabled is false" .Values.redis.connectionString -}}
{{- end -}}
{{- end -}}

{{- define "parcelflow.rabbitmqHost" -}}
{{- if .Values.infrastructure.enabled -}}
{{- include "parcelflow.componentName" (dict "root" . "component" "rabbitmq") -}}
{{- else -}}
{{- required "rabbitmq.host is required when infrastructure.enabled is false" .Values.rabbitmq.host -}}
{{- end -}}
{{- end -}}

{{/*
Keycloak authority used by the services and the gateway (back channel). With the bundled
development Keycloak, it is the in-cluster address while tokens carry the public issuer
(KC_HOSTNAME + KC_HOSTNAME_BACKCHANNEL_DYNAMIC, as in docker compose).
*/}}
{{- define "parcelflow.keycloakAuthority" -}}
{{- if .Values.infrastructure.enabled -}}
{{- printf "http://%s:8080/realms/%s" (include "parcelflow.componentName" (dict "root" . "component" "keycloak")) .Values.keycloak.realm -}}
{{- else -}}
{{- printf "%s/realms/%s" (trimSuffix "/" (required "keycloak.url is required" .Values.keycloak.url)) .Values.keycloak.realm -}}
{{- end -}}
{{- end -}}

{{/* Keycloak origin seen by browsers (portal KEYCLOAK_URL, token issuer). */}}
{{- define "parcelflow.keycloakPublicUrl" -}}
{{- trimSuffix "/" (required "keycloak.url is required" .Values.keycloak.url) -}}
{{- end -}}
