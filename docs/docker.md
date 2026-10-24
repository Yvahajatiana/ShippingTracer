# Docker

Chaque hôte déployable dispose d'une image, et `docker-compose.yml` assemble une plateforme locale complète : infrastructure, Keycloak, les huit services, la gateway et les trois portails. Le déploiement en production (TLS, migrations dédiées, secrets gérés) relève de l'étape Kubernetes.

## Images

| Image | Dockerfile | Contexte |
| --- | --- | --- |
| `gateway` | `src/Gateway/ParcelFlow.Gateway/Dockerfile` | racine du dépôt |
| `customer`, `shipment`, `tracking`, `warehouse`, `delivery`, `billing`, `notification`, `reporting` | `src/Services/<Service>/ParcelFlow.<Service>.Api/Dockerfile` | racine du dépôt |
| `customer-portal`, `operations-portal`, `admin-portal` | `frontends/Dockerfile` (argument `PORTAL`) | `frontends/` |

Images .NET :

- Construction multi-étapes `sdk:8.0-alpine`, exécution `aspnet:8.0-alpine` avec ICU et tzdata, utilisateur non root (`APP_UID`), port 8080.
- La restauration ne copie que les `csproj` du graphe de dépendances : modifier un service n'invalide pas le cache des autres.
- `HEALTHCHECK` sur `/health/live`. Les arguments `VERSION` et `REVISION` alimentent les labels OCI.
- L'environnement par défaut est `Production`.

Images des portails :

- Build Angular sur `node:22-alpine`, service par `nginx-unprivileged` (utilisateur 101, port 8080).
- `config.json` est généré au démarrage à partir de `KEYCLOAK_URL`, `KEYCLOAK_REALM`, `KEYCLOAK_CLIENT_ID` et `API_BASE_URL`. Les valeurs sont validées, une valeur invalide arrête le conteneur.
- nginx relaie `/api/` vers `GATEWAY_URL` sur la même origine, sans CORS. Il gère le repli SPA, `no-store` sur `index.html` et `config.json`, et un cache immuable sur les fichiers hachés.
- Une CSP stricte est appliquée : le seul script inline (chargement différé des styles) est autorisé par son empreinte SHA-256, calculée à chaque build.
- `GATEWAY_URL` est résolu à chaque requête : sous Kubernetes, utiliser le nom DNS complet du service.

## Plateforme locale

```bash
cp .env.example .env   # puis remplacer chaque valeur
docker compose up --build -d
```

| URL | Usage |
| --- | --- |
| http://localhost:4201 | Portail client |
| http://localhost:4202 | Portail opérations |
| http://localhost:4203 | Portail d'administration |
| http://localhost:5100 | Gateway |
| http://localhost:8080 | Keycloak (`KEYCLOAK_PORT`) |
| http://localhost:15672 | Console RabbitMQ |

Les ports sont publiés sur `127.0.0.1` uniquement. Les comptes de démonstration et les clients Keycloak sont décrits dans `deploy/keycloak/README.md`. Leur mot de passe commun est `DEMO_USER_PASSWORD`.

### Secrets

`.env` n'est pas versionné. Compose refuse de démarrer si `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `KEYCLOAK_ADMIN_PASSWORD`, `NOTIFICATION_CLIENT_SECRET` ou `DEMO_USER_PASSWORD` manque. L'oubli du secret Notification laisserait sinon à Keycloak la chaîne littérale du placeholder comme secret. Le mot de passe de démonstration doit respecter la politique du realm, sinon Keycloak refuse l'import.

### Choix de la plateforme locale

- **Mode `Development` pour les services.** Les migrations et seeds au démarrage, ainsi que l'autorité Keycloak en HTTP, ne sont acceptés qu'en développement. Compose fixe donc `ASPNETCORE_ENVIRONMENT=Development`, `Database__Initialize` et `Database__SeedDemoData`, sans modifier le code des services. Les images restent en `Production` par défaut.
- **Émetteur unique.** Le navigateur obtient ses jetons sur `localhost:<KEYCLOAK_PORT>`, alors que les conteneurs joignent Keycloak sur `keycloak:8080`. `KC_HOSTNAME` fixe l'émetteur public et `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` publie des URL internes pour les clés et l'endpoint de jeton. Services et gateway valident ainsi l'émetteur annoncé par la découverte OIDC.
- **Une base par service.** Une seule instance PostgreSQL 16 est utilisée, et `deploy/postgres/init-databases.sh` crée les huit bases au premier démarrage du volume.
- **Keycloak en `start-dev` sans volume.** Le realm est réimporté à chaque recréation du conteneur, et une modification de `deploy/keycloak/parcelflow-realm.json` s'applique par `docker compose up -d --force-recreate keycloak`.
- **Redis sans persistance.** Il ne sert que de cache.

### Opérations courantes

| Commande | Effet |
| --- | --- |
| `docker compose ps` | État et santé des conteneurs |
| `docker compose logs -f <service>` | Journaux d'un service |
| `docker compose up -d --build <service>` | Reconstruire et redémarrer un service |
| `docker compose down` | Arrêter la plateforme en conservant les données |
| `docker compose down -v` | Arrêter et supprimer les volumes PostgreSQL et RabbitMQ |

## Vérifications effectuées

- **Santé :** les seize conteneurs sont sains, et `/health/ready` répond `Healthy` sur les huit services et la gateway (PostgreSQL, Redis et RabbitMQ joints).
- **Émetteur :** la découverte OIDC donne le même émetteur depuis l'hôte et depuis le réseau Compose. Les clés et l'endpoint de jeton y sont annoncés sur `keycloak:8080`.
- **Bout en bout :** de vrais jetons Keycloak traversent le nginx de chaque portail, la gateway et les services.
  - **Client :** profil, factures et notifications seedés ; rapports refusés (403).
  - **Opérateur :** entrepôts, tournées et colis disponibles.
  - **Administrateur :** entreprises, clients, tarifs, factures et livreurs.
  - **Sans jeton :** 401.
- **Navigateur :** connexion réelle (code + PKCE) dans Chrome sans interface, sur le portail opérations (camille.martin, tableau de bord alimenté par Reporting) et sur le portail client (emma.martin), thème et données compris.
