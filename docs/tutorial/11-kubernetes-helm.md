# Chapitre 11 — Kubernetes avec Helm

## Objectif

Déployer la plateforme sur Kubernetes avec **un chart Helm unique**, `deploy/helm/parcelflow`, qui sert deux profils très différents :

- **local** (minikube) : équivalent du Compose, infrastructure intégrée, mode Development ;
- **production** : infrastructure managée externe, migrations dédiées, secrets externes, TLS, réplicas, NetworkPolicies.

## Étape 1 — Structure du chart

```text
deploy/helm/parcelflow/
├── Chart.yaml                      kubeVersion: ">=1.27.0-0"
├── values.yaml                     valeurs par défaut, orientées production
└── templates/
    ├── _helpers.tpl                noms, labels, chaînes de connexion, garde-fous
    ├── services.yaml gateway.yaml portals.yaml
    ├── ingress.yaml httproute.yaml networkpolicy.yaml pdb.yaml hpa.yaml
    ├── secret.yaml externalsecret.yaml migrations-job.yaml serviceaccount.yaml
    └── infrastructure/             PostgreSQL, Redis, RabbitMQ, Keycloak de développement
deploy/kubernetes/
├── values-local.yaml  values-production.example.yaml
├── external-secrets/               exemple de ClusterSecretStore (Azure Key Vault)
└── local.ps1  local.sh             up / forward / down sur minikube
```

Les huit services sont décrits par une simple table et rendus par une boucle `range` :

```yaml
services:
  customer:
    database: parcelflow_customers
    connectionString: CustomerDatabase
  shipment:
    database: parcelflow_shipments
    connectionString: ShipmentDatabase
  # ...
```

> **Pourquoi un chart unique et pas un chart par service ?** Les services partagent la même forme (image .NET, mêmes variables, mêmes probes). Une boucle garantit l'uniformité ; un neuvième service se déclare en trois lignes.

## Étape 2 — Mêmes variables que Compose, secrets jamais dans les manifestes

Les workloads reçoivent les variables déjà utilisées au chapitre 10 (`ConnectionStrings__<Service>Database`, `RabbitMq__*`, `Keycloak__Authority`, `Gateway__Destinations__*`, `KEYCLOAK_URL`, `GATEWAY_URL`). Les mots de passe arrivent par `secretKeyRef`, puis l'expansion Kubernetes `$(DATABASE_PASSWORD)` les insère dans la chaîne de connexion. C'est pourquoi `.gitleaks.toml` autorise le motif `$(VAR)`.

Trois sources de Secret, **mutuellement exclusives** :

| Source | Usage |
| --- | --- |
| `secrets.create` | Local : rendu par le chart à partir de `.env` |
| `secrets.existingSecret` | Secret créé hors Helm |
| `externalSecrets.enabled` | External Secrets Operator (`ExternalSecret` → Key Vault…) |

L'empreinte du Secret rendu est posée en annotation des pods (`checksum/secret`) : une rotation redémarre les services.

## Étape 3 — Des garde-fous dans le chart

`_helpers.tpl` refuse les combinaisons incohérentes avec un message explicite :

```gotemplate
{{- fail "no application Secret source: set secrets.existingSecret, externalSecrets.enabled=true or (local clusters only) secrets.create=true with secrets.values" -}}
{{- fail (printf "database.initialize=true is only honoured in Development (environment is %q): run migrations with migrations.enabled=true instead" (toString .Values.environment)) -}}
{{- fail "migrations.enabled=true needs an external database: the bundled PostgreSQL (infrastructure.enabled) is only created after the pre-install hooks. ..." -}}
```

Autres refus : deux chemins de migration à la fois, Notification sans URL HTTPS vers Shipment hors Development, hôte de portail manquant, `minReplicas > maxReplicas`.

> **Pourquoi ?** Une erreur de valeurs doit échouer au `helm template`, en CI, plutôt qu'en production à 3 h du matin.

## Étape 4 — Migrations de production : bundles EF Core en hooks Helm

En production, les services **ne migrent jamais au démarrage** (plusieurs réplicas migreraient en concurrence). Chaque service a une image de migration construite par `deploy/migrations/Dockerfile` :

```dockerfile
RUN dotnet ef migrations bundle \
        --project src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure \
        --startup-project src/Services/${SERVICE}/ParcelFlow.${SERVICE}.Infrastructure \
        --context ${SERVICE}DbContext \
        --configuration Release --self-contained --target-runtime linux-musl-x64 \
        --output /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS runtime
ENTRYPOINT ["/app/efbundle"]
```

Le bundle est autonome (pas de SDK ni de runtime .NET dans l'image), non root, compatible racine en lecture seule. Le chart l'exécute dans un Job par service (`templates/migrations-job.yaml`) :

```yaml
annotations:
  helm.sh/hook: pre-install,pre-upgrade
  # After the Secret / ExternalSecret hooks (weight -10).
  helm.sh/hook-weight: "0"
  helm.sh/hook-delete-policy: before-hook-creation,hook-succeeded
# ...
args:
  - --connection
  - {{ include "parcelflow.connectionString" (dict "root" $root "name" $name "database" $svc.database) | quote }}
```

- Le nouveau code ne démarre qu'avec un schéma à jour ; un Job en échec fait échouer la release.
- Contrainte acceptée : une migration doit rester **compatible avec la version précédente** pendant le déploiement progressif (et un rollback Helm n'annule pas une migration).
- Le Secret doit exister **avant** les hooks : un `existingSecret` convient ; sinon l'ExternalSecret devient lui-même un hook de poids `-10`, avec `creationPolicy: Orphan` pour que le Secret survive à sa recréation.
- Les tarifs Shipment étant posés par migration (chapitre 3), la production les reçoit sans seed.

## Étape 5 — Exposition indépendante du contrôleur

ingress-nginx n'étant plus maintenu par Kubernetes depuis mars 2026, le chart ne dépend d'aucun contrôleur :

- **Ingress standard** : `ingress.className` vide = classe par défaut du cluster, un hôte TLS par portail (+ gateway si `gateway.ingress.enabled`), cert-manager par annotation ;
- **Gateway API** : `httpRoute.enabled` génère une `HTTPRoute` par hôte, attachée aux `parentRefs` d'une `Gateway` existante (Envoy Gateway, NGINX Gateway Fabric, Traefik…).

Derrière un edge, la gateway voit l'IP du proxy et ignore `X-Forwarded-For` (chapitre 7). Le chart relève donc ses limites par IP (filet par proxy) et laisse la limite par client à la politique de l'edge ; la limite par utilisateur (`sub`) reste dans la gateway.

## Étape 6 — Sécurité des pods et du réseau

- Services et gateway : uid 1654, racine en lecture seule (`/tmp` en `emptyDir`), aucune capacité, seccomp `RuntimeDefault`, pas de jeton de compte de service.
- Probes : `startupProbe` et `livenessProbe` sur `/health/live`, `readinessProbe` sur `/health/ready` (la distinction posée au chapitre 2 sert ici).
- Production : 2 réplicas, PDB `minAvailable: 1`, HPA optionnel.
- **NetworkPolicy** (activée par défaut) : tout entrant refusé, puis autorisations explicites (edge → portails, portails → gateway, gateway → services, Notification → Shipment, release → infrastructure, edge → Keycloak). Résultat vérifié : un portail ne joint pas un service, un service ne joint pas un autre service.

## Étape 7 — Cluster local sans risque pour vos autres contextes

`deploy/kubernetes/local.ps1` (et `local.sh`) charge les images de `docker compose build` dans minikube et installe le chart avec `values-local.yaml` (`pullPolicy: Never`, `environment: Development`, `secrets.create: true`).

> **Sécurité opérateur (#47) :** le script passe **toujours** `--kube-context` explicitement (profil `PARCELFLOW_KUBE_CONTEXT`, `parcelflow` par défaut), vérifie que ce contexte est bien un cluster minikube et démarre minikube avec `--keep-context`. Le contexte kubectl courant de l'utilisateur, qui peut pointer vers un cluster distant, n'est jamais utilisé ni modifié. Les secrets de `.env` transitent par un fichier de valeurs temporaire supprimé après l'installation.

## Vérifier

```powershell
# Rendu du profil de production (aucun secret requis : il vient de l'External Secrets Operator)
helm lint deploy/helm/parcelflow --strict -f deploy/kubernetes/values-production.example.yaml
helm template parcelflow deploy/helm/parcelflow -f deploy/kubernetes/values-production.example.yaml > "$env:TEMP/parcelflow-production.yaml"
# Le profil local exige des secrets factices et le realm : voir le job "helm" de ci.yml

docker compose build
./deploy/kubernetes/local.ps1 up -StartCluster
./deploy/kubernetes/local.ps1 forward     # Ctrl+C pour arrêter
./deploy/kubernetes/local.ps1 down        # -DeleteNamespace, -StopCluster
```

Résultats à la livraison : 16 pods prêts sur minikube (Kubernetes 1.32), même émetteur OIDC depuis l'hôte et depuis un pod (`http://localhost:8180/realms/parcelflow`), parcours client/opérateur/admin et saga identiques à Compose ; les huit bundles appliquent leurs migrations sur un PostgreSQL vierge, puis une seconde exécution ne change rien ; NetworkPolicies vérifiées avec Calico (`PARCELFLOW_MINIKUBE_CNI=calico`).

Référence : [docs/kubernetes.md](../kubernetes.md).

Chapitre suivant : [CI/CD complète](12-ci-cd.md).
