# Kubernetes

La plateforme se déploie avec le chart Helm `deploy/helm/parcelflow`. Il contient les huit services, la gateway et les trois portails. Une infrastructure de développement optionnelle fournit PostgreSQL, Redis, RabbitMQ et Keycloak. `deploy/kubernetes/` contient deux profils de valeurs, les scripts du cluster local et un exemple de magasin de secrets externe.

## Arborescence

| Chemin | Contenu |
| --- | --- |
| `deploy/helm/parcelflow/values.yaml` | Valeurs par défaut, orientées production |
| `deploy/helm/parcelflow/templates/` | Services, gateway, portails, Ingress / HTTPRoute, NetworkPolicy, PDB, HPA, Secret / ExternalSecret, jobs de migration |
| `deploy/helm/parcelflow/templates/infrastructure/` | PostgreSQL, Redis, RabbitMQ et Keycloak de développement (`infrastructure.enabled`) |
| `deploy/kubernetes/values-local.yaml` | Profil minikube, équivalent à Docker Compose |
| `deploy/kubernetes/values-production.example.yaml` | Exemple de profil de production |
| `deploy/kubernetes/external-secrets/` | Exemple de `ClusterSecretStore` (Azure Key Vault, identité de workload) |
| `deploy/kubernetes/local.ps1`, `local.sh` | `up`, `forward` et `down` sur le cluster local |
| `deploy/migrations/Dockerfile` | Image de migration EF Core par service (argument `SERVICE`) |

## Profils

| | Local (`values-local.yaml`) | Production (par défaut) |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` |
| Infrastructure | intégrée au chart | externe : `database.host`, `redis.connectionString`, `rabbitmq.host`, `keycloak.url` |
| Schéma des bases | migrations et seeds au démarrage des services | jobs de migration (hooks Helm) |
| Secrets | rendus par le chart depuis `.env` (`secrets.create`) | `secrets.existingSecret` ou External Secrets Operator |
| Exposition | `kubectl port-forward` sur `127.0.0.1` | Ingress ou HTTPRoute en TLS |
| Réplicas | 1 | 2, PDB `minAvailable: 1`, HPA optionnel |
| Images | `parcelflow/<nom>:local` (`docker compose build`), `pullPolicy: Never` | `ghcr.io/yvahajatiana/parcelflow-<nom>:<tag>` publiées par la CI |

Le chart refuse les combinaisons incohérentes avec un message explicite :

- aucune source de secrets, ou plusieurs ;
- `database.initialize` hors Development, ou en même temps que les jobs de migration ;
- jobs de migration avec l'infrastructure intégrée ;
- Notification sans URL HTTPS vers Shipment hors Development ;
- hôte de portail manquant ;
- `minReplicas` supérieur à `maxReplicas`.

## Configuration des workloads

Les variables d'environnement reprennent celles de Docker Compose :

- `ConnectionStrings__<Service>Database`, `ConnectionStrings__Redis`, `RabbitMq__*`, `Keycloak__Authority` et `Database__*` pour les services ;
- `Gateway__Destinations__*` pour la gateway ;
- `KEYCLOAK_URL` et `GATEWAY_URL` pour les portails.

Les mots de passe ne figurent jamais dans les manifestes. Ils arrivent par `secretKeyRef`, et l'expansion `$(DATABASE_PASSWORD)` de Kubernetes les insère dans les chaînes de connexion. Un mot de passe ne doit donc pas contenir `;`, qui couperait la chaîne Npgsql.

| Clé du Secret | Usage |
| --- | --- |
| `postgres-password` | Rôle PostgreSQL `database.username` |
| `rabbitmq-password` | Utilisateur RabbitMQ `rabbitmq.username` |
| `notification-client-secret` | Client confidentiel Keycloak `notification-service` |
| `redis-password` | Optionnelle (`redis.passwordFromSecret`) |
| `keycloak-admin-password`, `demo-user-password` | Keycloak de développement uniquement |

Sécurité des pods :

- **Services et gateway :** non root (uid 1654), racine en lecture seule avec `/tmp` en `emptyDir`, aucune capacité, seccomp `RuntimeDefault`, pas de jeton de compte de service.
- **Portails :** uid 101. Leur racine reste inscriptible, car l'entrypoint écrit `config.json` dans le répertoire du bundle.
- **Probes :** `startupProbe` et `livenessProbe` sur `/health/live`, `readinessProbe` sur `/health/ready`, `/healthz` pour les portails.
- **Secret rendu par le chart :** son empreinte est en annotation des services, si bien qu'une rotation redémarre les pods.

### Notification vers Shipment

Hors Development, Notification refuse une URL HTTP vers Shipment. En production, `services.notification.shipmentBaseUrl` désigne donc l'URL HTTPS publique de la gateway, par exemple `https://api.example.com/` avec `gateway.ingress.enabled`. Les routes `/api/v1/shipments` sont identiques et la gateway accepte l'audience `shipment-api` du compte de service. En local, l'URL interne de Shipment est utilisée.

## Migrations

En production, les services ne migrent pas au démarrage. Un job par service applique le bundle de migration EF Core de l'image `<service>-migrations` (`deploy/migrations/Dockerfile`, entrypoint `/app/efbundle --connection ...`).

- **Ordre :** les jobs sont des hooks `pre-install,pre-upgrade`, donc le nouveau code ne démarre qu'avec un schéma à jour.
- **Contraintes sur les migrations :** elles doivent rester compatibles avec la version précédente pendant le déploiement progressif.
- **Disponibilité du Secret :** il doit exister avant les hooks. Un `existingSecret` remplit cette condition. Sinon, l'ExternalSecret (ou le Secret rendu) devient lui-même un hook de poids `-10`.
- **ExternalSecret en hook :** il est recréé à chaque upgrade et sa cible est `Orphan`, donc le Secret survit et les pods ne le perdent jamais. `helm uninstall` ne supprime pas ces hooks : supprimer le Secret à la main.
- **Tarifs Shipment :** ils sont posés par une migration. Les seeds de démonstration n'existent qu'en Development.

Chaque service n'a qu'un `DbContext` : les tables outbox et inbox de MassTransit en font partie. Le bundle est autonome (`linux-musl-x64`) sur `runtime-deps:8.0-alpine`, non root, et fonctionne avec une racine en lecture seule.

## Exposition

ingress-nginx n'est plus maintenu par Kubernetes depuis mars 2026. Le chart ne dépend donc d'aucun contrôleur :

- **Ingress standard :** `ingress.className` vide signifie la classe par défaut du cluster. Un hôte TLS est créé par portail, plus la gateway si `gateway.ingress.enabled`. Le certificat est `<hôte>-tls` ou `ingress.tls.secretName` (wildcard). cert-manager s'active par annotation.
- **Gateway API :** `httpRoute.enabled` avec `parentRefs` vers une `Gateway` existante (Envoy Gateway, NGINX Gateway Fabric, Traefik, etc.).

Le realm `deploy/keycloak/parcelflow-realm.json` n'autorise que `http://localhost:420x`. En production, les clients Keycloak doivent déclarer les hôtes réels des portails.

### Limitation de débit derrière l'edge

La gateway limite par adresse distante. Elle ignore volontairement `X-Forwarded-For`, qu'elle considère comme non fiable. Derrière l'edge et le nginx des portails, cette adresse est celle d'un proxy. Toutes les requêtes anonymes de suivi partageraient alors le quota public de 60 requêtes par minute.

Le chart relève donc les limites par IP de la gateway (`gateway.config` : 3 000 requêtes publiques, 20 000 au total), qui ne servent plus que de filet par proxy. La limite par client relève de la politique du contrôleur d'edge : `BackendTrafficPolicy` d'Envoy Gateway, middleware Traefik, etc. La limite par utilisateur authentifié (`sub`) reste appliquée par la gateway.

Une évolution de la gateway reste possible : faire confiance aux en-têtes transférés depuis des réseaux de proxies configurés. Elle modifierait un composant déjà validé et n'est pas incluse.

## NetworkPolicy

`networkPolicy.enabled` (activé par défaut) refuse par défaut tout trafic entrant vers les pods de la release, puis autorise uniquement :

- l'edge vers les portails (`edgeNamespaceSelector`, vide signifie tous les namespaces) ;
- les portails vers la gateway ;
- l'edge vers la gateway publique ;
- la gateway vers les services ;
- Notification vers Shipment ;
- les pods de la release vers PostgreSQL, Redis, RabbitMQ et Keycloak ;
- l'edge vers Keycloak.

Le trafic sortant n'est pas restreint : DNS, bases managées, Keycloak et URL publique de la gateway. Le scraping Prometheus est hors périmètre. Le plugin réseau par défaut de minikube n'applique pas les NetworkPolicy : il faut `PARCELFLOW_MINIKUBE_CNI=calico` pour les tester.

## Cluster local (minikube)

Pré-requis : Docker, minikube, kubectl, Helm 3 et le fichier `.env` de Docker Compose (mêmes secrets).

```powershell
docker compose build
./deploy/kubernetes/local.ps1 up -StartCluster   # profil minikube "parcelflow", chargement des images, helm upgrade --install
./deploy/kubernetes/local.ps1 forward            # Ctrl+C pour arrêter, ou -Detach / -Stop
./deploy/kubernetes/local.ps1 down               # -DeleteNamespace supprime aussi les volumes, -StopCluster arrête minikube
```

`local.sh` offre les mêmes commandes, avec les options en `--kebab-case`.

Les scripts passent toujours `--kube-context` explicitement (contexte et profil `PARCELFLOW_KUBE_CONTEXT`, `parcelflow` par défaut). Ils refusent tout contexte qui n'est pas un cluster minikube : le contexte kubectl courant n'est jamais utilisé. Les secrets de `.env` transitent par un fichier de valeurs temporaire, supprimé après l'installation.

| URL | Usage |
| --- | --- |
| http://localhost:4201 | Portail client |
| http://localhost:4202 | Portail opérations |
| http://localhost:4203 | Portail d'administration |
| http://localhost:5100 | Gateway |
| http://localhost:8180 | Keycloak (émetteur des jetons) |
| http://localhost:15672 | Console RabbitMQ |

Comme sous Compose, l'émetteur public est `http://localhost:8180/realms/parcelflow` (`KC_HOSTNAME`). Les pods joignent Keycloak sur `parcelflow-keycloak:8080` (`KC_HOSTNAME_BACKCHANNEL_DYNAMIC`).

Au premier démarrage, les services redémarrent une ou deux fois le temps que PostgreSQL, RabbitMQ et Keycloak soient prêts. Une image reconstruite avec le même tag n'est pas reprise automatiquement : relancer `up`, puis `kubectl --context parcelflow -n parcelflow rollout restart deployment`.

## Production

`deploy/kubernetes/values-production.example.yaml` sert de point de départ :

- images figées sur un tag `sha-…` ;
- PostgreSQL en `SSL Mode=VerifyFull`, Redis en TLS ;
- ExternalSecret depuis Azure Key Vault ;
- Ingress TLS avec cert-manager ;
- gateway publique ;
- HPA.

```bash
helm upgrade --install parcelflow deploy/helm/parcelflow -n parcelflow --create-namespace \
  -f deploy/kubernetes/values-production.example.yaml --wait --timeout 15m
```

Prérequis du cluster :

- External Secrets Operator et son `ClusterSecretStore` ;
- un contrôleur Ingress ou Gateway API ;
- cert-manager si les certificats sont émis automatiquement ;
- un broker RabbitMQ avec le plugin `rabbitmq_delayed_message_exchange` (délai de paiement de la saga Shipment) ;
- un Keycloak HTTPS dont le realm déclare les hôtes des portails.

Les images de migration ne sont pas encore publiées par la CI : leur publication et le déploiement automatisé relèvent de l'étape CI/CD.

## Vérifications effectuées

- **Rendu :** `helm lint`, `helm template` et kubeconform en mode strict (Kubernetes 1.30 et 1.32). Le profil local donne 48 ressources valides. Le profil de production en donne 63 valides, l'ExternalSecret n'ayant pas de schéma. Les variantes HTTPRoute, autoscaling, Secret en hook et certificat wildcard sont valides, et les cas d'erreur produisent leur message.
- **Migrations :** les images des huit services sont construites. Chacune applique ses migrations sur un PostgreSQL 16 vierge (code de sortie 0, `__EFMigrationsHistory` renseignée), avec `--read-only --tmpfs /tmp`. Une seconde exécution ne change rien.
- **Cluster minikube (Kubernetes 1.32, profil local) :**
  - les 16 pods sont prêts et `/health/ready` de la gateway répond `Healthy` ;
  - la découverte OIDC annonce le même émetteur depuis l'hôte et depuis un pod, avec l'endpoint de jeton interne ;
  - de vrais jetons Keycloak traversent le nginx des portails, la gateway et les services ;
  - **sans jeton :** 401 ;
  - **client (emma.martin) :** profil, factures et notifications ; rapports refusés (403) ;
  - **opératrice (camille.martin) :** entrepôts, tournées et indicateurs ;
  - **administrateur :** clients et livreurs ;
  - **saga :** un envoi créé par le portail client produit, via RabbitMQ, la facture `PendingPayment` (8,50 EUR), les notifications associées et le suivi public anonyme.
- **NetworkPolicy (minikube avec Calico) :** le parcours de bout en bout ci-dessus passe à l'identique.
  - **Flux autorisés :** gateway vers un service, Notification vers Shipment, portail vers gateway et vers Keycloak.
  - **Flux bloqués :** portail vers un service, service vers un autre service, et un pod étranger à la release vers la gateway, un service ou PostgreSQL.
