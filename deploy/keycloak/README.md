# Realm Keycloak `parcelflow` (local)

`parcelflow-realm.json` est importé au démarrage par `quay.io/keycloak/keycloak:26.8.1` avec `start-dev --import-realm`, le dossier étant monté en lecture seule sur `/opt/keycloak/data/import`. Il est réservé au développement local et à Docker Compose : aucun secret réel n'est versionné.

## Variables d'environnement

Keycloak 26 remplace les placeholders `${VARIABLE}` du fichier importé par les variables d'environnement du conteneur (syntaxe avec valeur par défaut `${VARIABLE:defaut}` non utilisée ici).

| Variable | Usage |
| --- | --- |
| `NOTIFICATION_CLIENT_SECRET` | Secret du client confidentiel `notification-service`, identique à `ServiceAuthentication__ClientSecret` du service Notification |
| `DEMO_USER_PASSWORD` | Mot de passe commun des comptes de démonstration, non temporaire |
| `KC_BOOTSTRAP_ADMIN_USERNAME`, `KC_BOOTSTRAP_ADMIN_PASSWORD` | Administrateur temporaire du realm `master` |

Comportements vérifiés :

- `DEMO_USER_PASSWORD` absent ou non conforme à la politique (12 caractères minimum, majuscule, minuscule, chiffre, caractère spécial, différent de l'identifiant et de l'email) : l'import échoue et Keycloak ne démarre pas.
- `NOTIFICATION_CLIENT_SECRET` absent : Keycloak ne remplace pas le placeholder et le secret devient la chaîne littérale `${NOTIFICATION_CLIENT_SECRET}`, qui est publique. Le Compose doit donc rendre la variable obligatoire (`${NOTIFICATION_CLIENT_SECRET:?...}`).

## Paramètres du realm

`sslRequired: external`, inscription et réinitialisation de mot de passe désactivées, protection contre la force brute (5 échecs, attente progressive jusqu'à 15 min), access token 5 min, session SSO inactive 30 min et maximale 10 h, rotation des refresh tokens sans réutilisation, événements de connexion et d'administration conservés 7 jours. Rôles du realm : `customer`, `operator`, `admin`.

## Clients

Les jetons portent `sub`, `realm_access.roles`, `preferred_username` et `email` via les scopes par défaut `basic`, `roles`, `profile`, `email`. Chaque client ajoute par mapper `oidc-audience-mapper` (access token uniquement) les audiences des API réellement appelées par son portail, car la gateway exige `aud` égal au service ciblé. `fullScopeAllowed` est désactivé : un client ne transmet que les rôles de son portail.

| Client | Type | Rôles transmis | Audiences | Origine |
| --- | --- | --- | --- | --- |
| `parcelflow-customer` | public, code + PKCE S256 | customer | customer-api, shipment-api, billing-api, notification-api | `http://localhost:4201` |
| `parcelflow-operations` | public, code + PKCE S256 | operator, admin | customer-api, shipment-api, tracking-api, warehouse-api, delivery-api, reporting-api | `http://localhost:4202` |
| `parcelflow-admin` | public, code + PKCE S256 | admin | customer-api, shipment-api, warehouse-api, delivery-api, billing-api, notification-api, reporting-api | `http://localhost:4203` |
| `notification-service` | confidentiel, client_credentials uniquement | operator | shipment-api | aucune |

Le portail client n'utilise que le suivi public anonyme de Tracking : `tracking-api` n'est pas dans son audience. Le portail d'administration n'appelle pas Tracking. Les clients publics n'autorisent ni flux implicite, ni mot de passe direct, ni compte de service ; redirections et déconnexion sont limitées à `http://localhost:420x/*`, les web origins à l'origine exacte du portail.

## Utilisateurs de démonstration

| Identifiant (`sub`) | Username | Nom | Rôle | Correspondance |
| --- | --- | --- | --- | --- |
| `10000000-0000-0000-0000-000000000001` | emma.martin | Emma Martin | customer | seeds Customer, Billing, Notification |
| `10000000-0000-0000-0000-000000000002` | lucas.bernard | Lucas Bernard | customer | seeds Customer, Billing, Notification |
| `demo-driver-lyon` | camille.martin | Camille Martin | operator | livreur du seed Delivery |
| `demo-driver-paris` | alex.durand | Alex Durand | operator | livreur du seed Delivery |
| `20000000-0000-0000-0000-000000000001` | chloe.leroy | Chloé Leroy | operator | opératrice sans profil livreur |
| `20000000-0000-0000-0000-000000000002` | admin.parcelflow | David Moreau | admin | administration |

Les identifiants sont fixés dans le fichier pour correspondre aux sujets attendus par les seeds ; Keycloak accepte à l'import les identifiants non UUID des livreurs. Les emails utilisent `example.test` et sont marqués vérifiés. Le compte de service `service-account-notification-service` reçoit le rôle `operator`.

## Réinitialisation

L'import utilise la stratégie `IGNORE_EXISTING` : un realm déjà présent dans la base Keycloak n'est pas réimporté et les changements du fichier ne sont pas appliqués. Pour repartir du fichier, supprimer les données Keycloak (conteneur `start-dev` sans volume : le recréer ; avec base persistante : supprimer son volume) ou supprimer le realm `parcelflow` dans la console d'administration puis redémarrer Keycloak.

## Émetteur

L'émetteur des jetons est l'URL publique vue par le navigateur, `http://localhost:8080/realms/parcelflow`, valeur de `Keycloak__Authority` dans les services et la gateway. Dans un réseau de conteneurs, l'émetteur doit rester cette URL publique (`KC_HOSTNAME`), sinon la validation `iss` échoue.
