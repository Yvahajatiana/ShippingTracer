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

{{/* ---------------------------------------------------------------------------------------------
Application workload helpers (services, gateway, portals, migration jobs).
--------------------------------------------------------------------------------------------- */}}

{{- define "parcelflow.isDevelopment" -}}
{{- if eq (lower (toString .Values.environment)) "development" -}}true{{- end -}}
{{- end -}}

{{/* True when the chart itself renders the Secret from secrets.values. */}}
{{- define "parcelflow.rendersSecret" -}}
{{- if and .Values.secrets.create (not .Values.secrets.existingSecret) (not .Values.externalSecrets.enabled) -}}true{{- end -}}
{{- end -}}

{{/*
Global consistency checks, rendered (as nothing) once by services.yaml. Each failure explains the
expected configuration instead of letting the pods crash at runtime.
*/}}
{{- define "parcelflow.validate" -}}
{{- $secrets := .Values.secrets -}}
{{- if and $secrets.existingSecret (or $secrets.create .Values.externalSecrets.enabled) -}}
{{- fail "secrets.existingSecret is set: disable secrets.create and externalSecrets.enabled (the chart must not render a second Secret)" -}}
{{- end -}}
{{- if and $secrets.create .Values.externalSecrets.enabled -}}
{{- fail "secrets.create and externalSecrets.enabled are mutually exclusive: choose one source for the application Secret" -}}
{{- end -}}
{{- if not (or $secrets.existingSecret $secrets.create .Values.externalSecrets.enabled) -}}
{{- fail "no application Secret source: set secrets.existingSecret, externalSecrets.enabled=true or (local clusters only) secrets.create=true with secrets.values" -}}
{{- end -}}
{{- if and .Values.database.initialize (not (include "parcelflow.isDevelopment" .)) -}}
{{- fail (printf "database.initialize=true is only honoured in Development (environment is %q): run migrations with migrations.enabled=true instead" (toString .Values.environment)) -}}
{{- end -}}
{{- if and .Values.migrations.enabled .Values.database.initialize -}}
{{- fail "migrations.enabled and database.initialize are both true: choose one migration path (hook jobs, or startup migrations in Development)" -}}
{{- end -}}
{{- if and .Values.migrations.enabled .Values.infrastructure.enabled -}}
{{- fail "migrations.enabled=true needs an external database: the bundled PostgreSQL (infrastructure.enabled) is only created after the pre-install hooks. Use environment=Development with database.initialize=true instead" -}}
{{- end -}}
{{- if and .Values.httpRoute.enabled (empty .Values.httpRoute.parentRefs) -}}
{{- fail "httpRoute.enabled=true requires httpRoute.parentRefs (the Gateway API Gateway the routes attach to)" -}}
{{- end -}}
{{- end -}}

{{/*
Effective settings of a workload: a shallow merge of the per-workload overrides over the defaults.
Shallow on purpose: an override such as "resources" replaces the default block entirely, and false
or 0 overrides (topologySpread: false) are honoured, which Sprig's mergeOverwrite would ignore.
Usage: include "parcelflow.workloadSettings" (dict "defaults" .Values.dotnetDefaults "overrides" $svc) | fromYaml
*/}}
{{- define "parcelflow.workloadSettings" -}}
{{- $settings := deepCopy (default dict .defaults) -}}
{{- range $key, $value := (default dict .overrides) -}}
{{- $_ := set $settings $key $value -}}
{{- end -}}
{{- toYaml $settings -}}
{{- end -}}

{{/* imagePullSecrets entries; accepts plain names or { name: ... } objects. */}}
{{- define "parcelflow.imagePullSecrets" -}}
{{- with .Values.image.pullSecrets }}
imagePullSecrets:
{{- range . }}
{{- if kindIs "string" . }}
  - name: {{ . }}
{{- else }}
  - {{- toYaml . | nindent 4 }}
{{- end }}
{{- end }}
{{- end }}
{{- end -}}

{{/* Pod security context. Usage: include "parcelflow.podSecurityContext" 1654 */}}
{{- define "parcelflow.podSecurityContext" -}}
securityContext:
  runAsNonRoot: true
  runAsUser: {{ . }}
  runAsGroup: {{ . }}
  fsGroup: {{ . }}
  seccompProfile:
    type: RuntimeDefault
{{- end -}}

{{/* Container security context. Usage: include "parcelflow.containerSecurityContext" (dict "readOnlyRootFilesystem" true) */}}
{{- define "parcelflow.containerSecurityContext" -}}
securityContext:
  allowPrivilegeEscalation: false
  readOnlyRootFilesystem: {{ .readOnlyRootFilesystem }}
  capabilities:
    drop: ["ALL"]
{{- end -}}

{{/*
Pod annotations: Secret checksum (only when the chart renders the Secret, so a rotation through
secrets.values rolls the pods; skipped for workloads reading no secret) plus the podAnnotations.
Usage: include "parcelflow.podAnnotations" (dict "root" $ "settings" $settings "usesSecret" true)
*/}}
{{- define "parcelflow.podAnnotations" -}}
{{- $annotations := dict -}}
{{- if and .usesSecret (include "parcelflow.rendersSecret" .root) -}}
{{- $_ := set $annotations "checksum/secret" (include (print .root.Template.BasePath "/secret.yaml") .root | sha256sum) -}}
{{- end -}}
{{- range $key, $value := (default dict .settings.podAnnotations) -}}
{{- $_ := set $annotations $key $value -}}
{{- end -}}
{{- with $annotations }}
annotations:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- end -}}

{{/*
Npgsql connection string of a service database. The password is not inlined: it references the
DATABASE_PASSWORD variable through Kubernetes $(VAR) expansion, so DATABASE_PASSWORD must be
declared before the variable or argument using it.
Usage: include "parcelflow.connectionString" (dict "root" $ "name" "customer" "database" "parcelflow_customers")
*/}}
{{- define "parcelflow.connectionString" -}}
{{- $db := .root.Values.database -}}
{{- $value := printf "Host=%s;Port=%v;Database=%s;Username=%s;Password=$(DATABASE_PASSWORD)" (include "parcelflow.databaseHost" .root) $db.port (required (printf "services.%s.database is required" .name) .database) $db.username -}}
{{- if $db.options -}}
{{- $value = printf "%s;%s" $value (trimPrefix ";" $db.options) -}}
{{- end -}}
{{- $value -}}
{{- end -}}

{{/* Environment variable read from the application Secret. Usage: include "parcelflow.secretEnv" (dict "root" $ "name" "DATABASE_PASSWORD" "key" "postgres-password") */}}
{{- define "parcelflow.secretEnv" -}}
- name: {{ .name }}
  valueFrom:
    secretKeyRef:
      name: {{ include "parcelflow.secretName" .root }}
      key: {{ .key }}
{{- end -}}

{{/*
Environment of a service, mirroring docker-compose.yml. Order matters: a variable referenced
through $(VAR) must be declared before the variable using it.
Usage: include "parcelflow.serviceEnv" (dict "root" $ "name" "notification" "service" $svc)
*/}}
{{- define "parcelflow.serviceEnv" -}}
{{- $root := .root -}}
{{- $svc := .service -}}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ $root.Values.environment | quote }}
{{ include "parcelflow.secretEnv" (dict "root" $root "name" "DATABASE_PASSWORD" "key" "postgres-password") }}
- name: ConnectionStrings__{{ required (printf "services.%s.connectionString is required" .name) $svc.connectionString }}
  value: {{ include "parcelflow.connectionString" (dict "root" $root "name" .name "database" $svc.database) | quote }}
{{- if $root.Values.redis.passwordFromSecret }}
{{ include "parcelflow.secretEnv" (dict "root" $root "name" "REDIS_PASSWORD" "key" "redis-password") }}
- name: ConnectionStrings__Redis
  value: {{ printf "%s,password=$(REDIS_PASSWORD)" (include "parcelflow.redisConnection" $root) | quote }}
{{- else }}
- name: ConnectionStrings__Redis
  value: {{ include "parcelflow.redisConnection" $root | quote }}
{{- end }}
- name: RabbitMq__Host
  value: {{ include "parcelflow.rabbitmqHost" $root | quote }}
- name: RabbitMq__Port
  value: {{ $root.Values.rabbitmq.port | toString | quote }}
- name: RabbitMq__VirtualHost
  value: {{ $root.Values.rabbitmq.virtualHost | quote }}
- name: RabbitMq__Username
  value: {{ $root.Values.rabbitmq.username | quote }}
{{ include "parcelflow.secretEnv" (dict "root" $root "name" "RabbitMq__Password" "key" "rabbitmq-password") }}
- name: Keycloak__Authority
  value: {{ include "parcelflow.keycloakAuthority" $root | quote }}
- name: Database__Initialize
  value: {{ $root.Values.database.initialize | toString | quote }}
- name: Database__SeedDemoData
  value: {{ $root.Values.database.seedDemoData | toString | quote }}
{{- if $svc.serviceClientId }}
- name: ServiceAuthentication__ClientId
  value: {{ $svc.serviceClientId | quote }}
{{ include "parcelflow.secretEnv" (dict "root" $root "name" "ServiceAuthentication__ClientSecret" "key" "notification-client-secret") }}
- name: ShipmentService__BaseUrl
  value: {{ include "parcelflow.shipmentBaseUrl" (dict "root" $root "service" $svc) | quote }}
{{- end }}
{{- end -}}

{{/*
Shipment base URL of the Notification service. Outside Development the service only accepts
HTTPS, which the in-cluster shipment Service does not serve: use the public gateway URL (same
/api/v1/shipments routes, and the gateway accepts the shipment-api token audience).
*/}}
{{- define "parcelflow.shipmentBaseUrl" -}}
{{- $url := toString (default "" .service.shipmentBaseUrl) -}}
{{- if $url -}}
{{- if and (not (include "parcelflow.isDevelopment" .root)) (not (hasPrefix "https://" $url)) -}}
{{- fail (printf "services.notification.shipmentBaseUrl must be an https URL outside Development (got %q), e.g. the public gateway https://api.example.com/" $url) -}}
{{- end -}}
{{- $url -}}
{{- else if include "parcelflow.isDevelopment" .root -}}
{{- include "parcelflow.componentUrl" (dict "root" .root "component" "shipment") -}}
{{- else -}}
{{- fail "services.notification.shipmentBaseUrl is required outside Development: the Notification service refuses plain HTTP, so set the HTTPS public gateway URL, e.g. https://api.example.com/ (routes /api/v1/shipments are identical and the gateway accepts the shipment-api token audience)" -}}
{{- end -}}
{{- end -}}

{{/* Environment of the gateway: one destination per service (key in PascalCase) and the gateway.config entries. */}}
{{- define "parcelflow.gatewayEnv" -}}
{{- $root := . -}}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ $root.Values.environment | quote }}
- name: Keycloak__Authority
  value: {{ include "parcelflow.keycloakAuthority" $root | quote }}
{{- range $name, $_ := $root.Values.services }}
- name: Gateway__Destinations__{{ title $name }}
  value: {{ include "parcelflow.componentUrl" (dict "root" $root "component" $name) | quote }}
{{- end }}
{{- range $key, $value := $root.Values.gateway.config }}
- name: Gateway__{{ $key }}
  value: {{ $value | toString | quote }}
{{- end }}
{{- end -}}

{{/*
Deployment and ClusterIP Service of a .NET workload (services and gateway).
Usage: include "parcelflow.dotnetWorkload" (dict "root" $ "component" "gateway" "settings" $settings "env" $envYaml "usesSecret" false)
*/}}
{{- define "parcelflow.dotnetWorkload" -}}
{{- $root := .root -}}
{{- $settings := .settings -}}
{{- $ctx := dict "root" $root "component" .component -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "parcelflow.componentName" $ctx }}
  labels:
    {{- include "parcelflow.labels" $ctx | nindent 4 }}
spec:
  {{- if not $root.Values.autoscaling.enabled }}
  replicas: {{ $settings.replicas }}
  {{- end }}
  revisionHistoryLimit: 5
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 25%
      maxUnavailable: 0
  selector:
    matchLabels:
      {{- include "parcelflow.selectorLabels" $ctx | nindent 6 }}
  template:
    metadata:
      labels:
        {{- include "parcelflow.labels" $ctx | nindent 8 }}
      {{- with (include "parcelflow.podAnnotations" (dict "root" $root "settings" $settings "usesSecret" .usesSecret) | trim) }}
      {{- . | nindent 6 }}
      {{- end }}
    spec:
      serviceAccountName: {{ include "parcelflow.serviceAccountName" $root }}
      # The workloads never call the Kubernetes API.
      automountServiceAccountToken: false
      # No *_SERVICE_HOST / *_PORT variables: configuration comes only from the env below.
      enableServiceLinks: false
      {{- with (include "parcelflow.imagePullSecrets" $root | trim) }}
      {{- . | nindent 6 }}
      {{- end }}
      # 1654 is APP_UID, the non-root user of the mcr.microsoft.com/dotnet/aspnet images.
      {{- include "parcelflow.podSecurityContext" 1654 | nindent 6 }}
      terminationGracePeriodSeconds: 30
      containers:
        - name: {{ .component }}
          image: {{ include "parcelflow.image" (dict "root" $root "name" .component) }}
          imagePullPolicy: {{ $root.Values.image.pullPolicy }}
          ports:
            - name: http
              containerPort: 8080
              protocol: TCP
          env:
            {{- .env | nindent 12 }}
            # Read-only root filesystem: temporary files (diagnostics IPC socket, request body
            # buffering) go to the /tmp emptyDir. The Data Protection key ring stays in memory,
            # which is fine for these token-authenticated APIs (no cookies, no antiforgery).
            - name: TMPDIR
              value: /tmp
            {{- with $settings.extraEnv }}
            {{- toYaml . | nindent 12 }}
            {{- end }}
          startupProbe:
            httpGet:
              path: /health/live
              port: http
            periodSeconds: 5
            timeoutSeconds: 3
            failureThreshold: 30
          livenessProbe:
            httpGet:
              path: /health/live
              port: http
            periodSeconds: 10
            timeoutSeconds: 3
            failureThreshold: 3
          readinessProbe:
            httpGet:
              path: /health/ready
              port: http
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 3
          {{- with $settings.resources }}
          resources:
            {{- toYaml . | nindent 12 }}
          {{- end }}
          {{- include "parcelflow.containerSecurityContext" (dict "readOnlyRootFilesystem" true) | nindent 10 }}
          volumeMounts:
            - name: tmp
              mountPath: /tmp
      volumes:
        - name: tmp
          emptyDir:
            sizeLimit: 256Mi
      {{- with (include "parcelflow.scheduling" (dict "ctx" $ctx "settings" $settings) | trim) }}
      {{- . | nindent 6 }}
      {{- end }}
---
{{ include "parcelflow.clusterIpService" $ctx }}
{{- end -}}

{{/*
Scheduling fields of a pod spec: nodeSelector, tolerations, affinity and, when
settings.topologySpread is true, a best-effort spread of the replicas over nodes.
Usage: include "parcelflow.scheduling" (dict "ctx" (dict "root" $ "component" "gateway") "settings" $settings)
*/}}
{{- define "parcelflow.scheduling" -}}
{{- with .settings.nodeSelector }}
nodeSelector:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- with .settings.tolerations }}
tolerations:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- with .settings.affinity }}
affinity:
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- if .settings.topologySpread }}
topologySpreadConstraints:
  - maxSkew: 1
    topologyKey: kubernetes.io/hostname
    whenUnsatisfiable: ScheduleAnyway
    labelSelector:
      matchLabels:
        {{- include "parcelflow.selectorLabels" .ctx | nindent 8 }}
{{- end }}
{{- end -}}

{{/* ClusterIP Service on port 8080 (http). Usage: include "parcelflow.clusterIpService" (dict "root" $ "component" "gateway") */}}
{{- define "parcelflow.clusterIpService" -}}
apiVersion: v1
kind: Service
metadata:
  name: {{ include "parcelflow.componentName" . }}
  labels:
    {{- include "parcelflow.labels" . | nindent 4 }}
spec:
  type: ClusterIP
  selector:
    {{- include "parcelflow.selectorLabels" . | nindent 4 }}
  ports:
    - name: http
      port: 8080
      targetPort: http
      protocol: TCP
{{- end -}}

{{/*
Workloads deployed by the chart with their effective settings, as YAML { items: [{ component,
settings }] }: the services, the gateway and the portals. Used by the PDB and HPA templates.
*/}}
{{- define "parcelflow.workloads" -}}
{{- $root := . -}}
{{- $items := list -}}
{{- range $name, $svc := $root.Values.services -}}
{{- $items = append $items (dict "component" $name "settings" (include "parcelflow.workloadSettings" (dict "defaults" $root.Values.dotnetDefaults "overrides" $svc) | fromYaml)) -}}
{{- end -}}
{{- $gatewayOverrides := omit (default dict $root.Values.gateway) "config" "ingress" -}}
{{- $items = append $items (dict "component" "gateway" "settings" (include "parcelflow.workloadSettings" (dict "defaults" $root.Values.dotnetDefaults "overrides" $gatewayOverrides) | fromYaml)) -}}
{{- range $name, $portal := $root.Values.portals -}}
{{- $items = append $items (dict "component" $name "settings" (include "parcelflow.workloadSettings" (dict "defaults" $root.Values.portalDefaults "overrides" $portal) | fromYaml)) -}}
{{- end -}}
{{- toYaml (dict "items" $items) -}}
{{- end -}}

{{/* Gateway origin used by the portals' nginx proxy: FQDN (nginx's resolver ignores search domains), no trailing slash. */}}
{{- define "parcelflow.gatewayOrigin" -}}
{{- printf "http://%s.%s.svc.%s:8080" (include "parcelflow.componentName" (dict "root" . "component" "gateway")) .Release.Namespace .Values.clusterDomain -}}
{{- end -}}

{{/* Hook annotations of the Secret / ExternalSecret when the migration hook Jobs need it first. */}}
{{- define "parcelflow.secretHookAnnotations" -}}
{{- if .Values.migrations.enabled }}
# The migration hook Jobs read this Secret, so it must exist before them and is a hook too.
# Consequences: Helm does not track hook resources, so "helm uninstall" leaves it in place
# (delete it manually), and every install / upgrade recreates it (before-hook-creation).
helm.sh/hook: pre-install,pre-upgrade
helm.sh/hook-weight: "-10"
helm.sh/hook-delete-policy: before-hook-creation
{{- end }}
{{- end -}}
