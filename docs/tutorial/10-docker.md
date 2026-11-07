# Chapitre 10 — Docker et plateforme locale

## Objectif

Donner une image à chaque hôte déployable (8 services, gateway, 3 portails, RabbitMQ) et assembler avec `docker-compose.yml` une plateforme locale **complète** : PostgreSQL, Redis, RabbitMQ, Keycloak avec son realm, services, gateway et portails, utilisable de bout en bout dans un navigateur.

## Étape 1 — L'image d'un service .NET

`src/Services/Shipment/ParcelFlow.Shipment.Api/Dockerfile` (les huit services et la gateway suivent le même modèle), construit **depuis la racine** du dépôt :

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
# Shared building blocks are copied whole: every service depends on them and new ones need no Dockerfile change.
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Shipment/ParcelFlow.Shipment.Domain/ParcelFlow.Shipment.Domain.csproj src/Services/Shipment/ParcelFlow.Shipment.Domain/
# ... les trois autres .csproj du service
RUN dotnet restore src/Services/Shipment/ParcelFlow.Shipment.Api/ParcelFlow.Shipment.Api.csproj -r linux-musl-x64
COPY src/Services/Shipment/ParcelFlow.Shipment.Domain/ src/Services/Shipment/ParcelFlow.Shipment.Domain/
# ... sources des quatre projets du service uniquement
RUN dotnet publish ... -c Release -r linux-musl-x64 --no-restore --self-contained false -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false ASPNETCORE_HTTP_PORTS="8080;8081" Management__Port=8081
COPY --from=build /app ./
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8081/health/live || exit 1
ENTRYPOINT ["dotnet", "ParcelFlow.Shipment.Api.dll"]
```

Raisonnement couche par couche :

- **Restauration séparée des sources** : tant qu'aucun `.csproj` ne change, `dotnet restore` reste en cache.
- **`src/BuildingBlocks` copié en entier**, mais seulement les `.csproj` et sources **du service** : modifier Billing n'invalide pas le cache de Shipment, et ajouter un building block ne demande aucune modification des Dockerfiles.
- **Alpine + ICU + tzdata** : image légère, mais formatage culturel et fuseaux horaires corrects.
- **Non root** (`APP_UID`), API sur le port 8080, `/health/*` et `/metrics` sur le seul port de management 8081 (jamais publié ni routé), `HEALTHCHECK` sur ce port, labels OCI alimentés par les arguments `VERSION` et `REVISION`.
- **`Production` par défaut** : l'image ne migre jamais seule.

## Étape 2 — L'image des portails

`frontends/Dockerfile`, une image par portail via l'argument `PORTAL` :

```dockerfile
FROM node:22.23-alpine AS build
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund
COPY angular.json tsconfig.json components.json .postcssrc.json ./
COPY projects ./projects
RUN ... npx ng build "$PORTAL" && node /tools/csp-script-hashes.mjs "dist/$PORTAL/browser/index.html" /tools/csp-script-hashes.txt

FROM nginxinc/nginx-unprivileged:1.30-alpine AS runtime
```

- **nginx non privilégié** (uid 101). Les fichiers statiques restent en lecture seule ; seul `config.json` est inscriptible.
- **`config.json` généré au démarrage** par `docker/entrypoint/16-parcelflow-config.envsh` à partir de `KEYCLOAK_URL`, `KEYCLOAK_REALM`, `KEYCLOAK_CLIENT_ID` et `API_BASE_URL`. Les valeurs sont validées et une valeur invalide arrête le conteneur : c'est la suite logique de la configuration à l'exécution du chapitre 9.
- **Proxy `/api/` vers la gateway** (`GATEWAY_URL`) sur la même origine : pas de CORS à configurer.
- **CSP stricte** : le seul script inline est autorisé par son empreinte SHA-256, recalculée à chaque build.
- **Cache** : `no-store` sur `index.html` et `config.json`, `immutable` sur les fichiers hachés.

## Étape 3 — RabbitMQ avec le plugin de messages différés

La saga Shipment planifie son délai de paiement par livraison différée (chapitre 3). L'image officielle n'a pas le plugin, d'où `deploy/rabbitmq/Dockerfile` :

```dockerfile
FROM rabbitmq:3.13-management-alpine
# Same archive and checksum as the integration tests (tests/Shared/RabbitMqDelayedExchange.cs).
ADD --chown=rabbitmq:rabbitmq --chmod=0644 \
    --checksum=sha256:3479180df03fa830fd59ccf638a60eee8f0e8261524a8616685522d7b8d9a9ee \
    https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/rabbitmq_delayed_message_exchange-3.13.0.ez \
    /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-3.13.0.ez
RUN apk upgrade --no-cache \
 && rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange rabbitmq_prometheus
```

> Même archive et **même empreinte** que les tests : ce qui est testé est ce qui tourne.

## Étape 4 — Le Compose : ancres YAML et secrets obligatoires

Les huit services partagent leurs réglages par ancres (`docker-compose.yml`) :

```yaml
x-dotnet-environment: &dotnet-environment
  ASPNETCORE_ENVIRONMENT: Development
  Database__Initialize: "true"
  Database__SeedDemoData: "true"
  Keycloak__Authority: http://keycloak:8080/realms/parcelflow
  RabbitMq__Host: rabbitmq
  RabbitMq__Password: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD in .env}
  # ...

services:
  # ... infrastructure
  customer:
    <<: *dotnet-service
    image: parcelflow/customer:${PARCELFLOW_TAG:-local}
    environment:
      <<: *dotnet-environment
      ConnectionStrings__CustomerDatabase: Host=postgres;Port=5432;Database=parcelflow_customers;Username=parcelflow;Password=${POSTGRES_PASSWORD}
```

Décisions :

- **Services en `Development`.** Migrations et seeds au démarrage, ainsi que l'autorité Keycloak en HTTP, ne sont acceptés **qu'en Development**. Compose le fixe explicitement, sans toucher au code ; les images restent en `Production` par défaut.
- **Secrets obligatoires** : `${VAR:?message}` fait échouer `docker compose up` si `.env` est incomplet. C'est critique pour `NOTIFICATION_CLIENT_SECRET` : absent, Keycloak garderait le placeholder littéral comme secret, une valeur publique.
- **Une base par service** sur une instance PostgreSQL 16 : `deploy/postgres/init-databases.sh`, monté dans `docker-entrypoint-initdb.d`, crée les huit bases au premier démarrage du volume.
- **Dépendances par santé** (`condition: service_healthy`) ; Keycloak est sondé sur son port de management en bash, l'image n'ayant pas `curl`, sur le statut HTTP 200 et non sur le corps (une réponse DOWN contient aussi `"status": "UP"` pour ses vérifications imbriquées).
- **Ports publiés sur `127.0.0.1` uniquement.**
- **Redis sans persistance** (`--save "" --appendonly no`) : il ne sert que de cache.

## Étape 5 — Un émetteur Keycloak unique

Le problème : le navigateur obtient ses jetons sur `localhost:8080`, alors que les conteneurs joignent Keycloak sur `keycloak:8080`. L'émetteur (`iss`) diffère et la validation échoue.

```yaml
keycloak:
  command: ["start-dev", "--import-realm"]
  environment:
    KC_HOSTNAME: http://localhost:${KEYCLOAK_PORT:-8080}
    KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"
  volumes:
    - ./deploy/keycloak:/opt/keycloak/data/import:ro
```

`KC_HOSTNAME` fixe l'émetteur public ; `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` publie des URL internes pour les clés et l'endpoint de jeton. Services et gateway valident ainsi le même émetteur que celui du navigateur.

Le realm `deploy/keycloak/parcelflow-realm.json` est versionné : rôles `customer`/`operator`/`admin`, un client public PKCE par portail avec mappers d'**audience** vers les API qu'il appelle (la gateway exige `aud`), le client confidentiel `notification-service` et les comptes de démonstration. Keycloak 26 remplace les placeholders `${VARIABLE}` par l'environnement du conteneur.

## Vérifier

```powershell
Copy-Item .env.example .env   # puis remplacer chaque valeur
docker compose up --build -d
docker compose ps             # PostgreSQL, Redis, RabbitMQ et Keycloak "healthy", les autres "running"
curl http://localhost:5100/health/ready
```

| URL | Usage |
| --- | --- |
| http://localhost:4201 / 4202 / 4203 | Portails client, opérations, administration |
| http://localhost:5100 | Gateway |
| http://localhost:8080 | Keycloak (`KEYCLOAK_PORT`) |
| http://localhost:15672 | Console RabbitMQ |

Parcours vérifié à la livraison : seize conteneurs démarrés et `/health/ready` à `Healthy` sur les services et la gateway ; de vrais jetons Keycloak traversent nginx, la gateway et les services ; sans jeton 401 ; client sans accès aux rapports (403) ; un envoi créé par le portail client produit via RabbitMQ la facture `PendingPayment`, les notifications et le suivi public ; connexion réelle code + PKCE dans un Chrome sans interface.

Arrêter la plateforme pour libérer les ports : `docker compose down` (ou `down -v` pour supprimer aussi les volumes).

Référence : [docs/docker.md](../docker.md), `deploy/keycloak/README.md`.

Chapitre suivant : [Kubernetes avec Helm](11-kubernetes-helm.md).
