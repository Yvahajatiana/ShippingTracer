# API Gateway

La gateway est le point d’entrée HTTP des huit services ParcelFlow. Elle utilise ASP.NET Core 8 et YARP, sans base de données ni messagerie. Les contrats et les règles métier restent dans les services. Les chemins ne sont pas réécrits ; les paramètres, corps, jetons Bearer, statuts, en-têtes de contenu et fichiers traversent le proxy.

## Routage

| Préfixes publics de l’API | Destination | Audience JWT | Port local proposé |
| --- | --- | --- | --- |
| `/api/v1/customers`, `/api/v1/companies` | Customer | `customer-api` | 5101 |
| `/api/v1/shipments`, `/api/v1/tariffs` | Shipment | `shipment-api` | 5102 |
| `/api/v1/tracking` | Tracking | `tracking-api` | 5103 |
| `/api/v1/warehouses` | Warehouse | `warehouse-api` | 5104 |
| `/api/v1/delivery` | Delivery | `delivery-api` | 5105 |
| `/api/v1/billing` | Billing | `billing-api` | 5106 |
| `/api/v1/notifications` | Notification | `notification-api` | 5107 |
| `/api/v1/reports` | Reporting | `reporting-api` | 5108 |

Chaque préfixe conserve ses sous-chemins. Les routes inconnues et les chemins d’exploitation des services (`/health`, `/metrics`, `/swagger`) ne sont pas routés. La gateway peut écouter sur le port 5100 via `ASPNETCORE_URLS` ; les services doivent être démarrés sur les adresses configurées, ces ports ne modifient pas leur configuration validée.

## Authentification et accès navigateur

Seul `GET /api/v1/tracking/{trackingNumber}`, avec un unique segment après `tracking`, est anonyme. Le suivi privé `/api/v1/tracking/shipments/{shipmentId}` reste authentifié. La gateway valide signature, émetteur, durée de validité, audience et sujet du JWT Keycloak. L’audience doit correspondre au service ciblé ; un jeton exclusivement destiné à Customer n’autorise pas Reporting.

Le même jeton est transmis au service, qui conserve le contrôle des rôles, du propriétaire et des permissions métier. Une audience supplémentaire `gateway-api` n’est pas imposée. Cette décision préserve les audiences existantes et évite une modification des services déjà validés.

Les origines CORS sont explicites. Aucun joker ni cookie d’authentification interorigine n’est nécessaire : les clients transmettent le Bearer dans `Authorization`. `Location`, `Content-Disposition` et `Retry-After` sont exposés aux clients navigateur. CORS ne remplace pas la validation JWT.

Les en-têtes de transfert fournis par le client ne définissent ni son identité ni son adresse de limitation. La gateway utilise la connexion reçue et produit elle-même les métadonnées de transfert. L’intégration d’un ingress de confiance sera configurée à l’étape Kubernetes ; aucun proxy arbitraire n’est implicitement approuvé.

## Limites et erreurs

La taille maximale par défaut est de 12 MiB, compatible avec les preuves de livraison. Les services appliquent également leurs propres limites. Le timeout d’inactivité amont est de trente secondes. Aucun rejeu automatique des mutations HTTP n’est effectué. Les réponses sont transmises en streaming ; une panne après le début d’une réponse ne peut pas être remplacée par un nouveau document JSON.

Les requêtes publiques sont limitées par adresse de connexion, les requêtes privées par utilisateur, avec une limite supplémentaire par adresse. Les compteurs résident dans chaque instance de gateway ; les quotas ne sont pas distribués entre réplicas. Une limite dépassée renvoie 429 avec `Retry-After`. Les erreurs produites par la gateway utilisent les ProblemDetails communs, notamment 401, 403, 404, 413, 429 et les erreurs de transfert amont. Les réponses API portent `Cache-Control: no-store`.

## Configuration

| Variable d’environnement | Valeur / usage |
| --- | --- |
| `ASPNETCORE_URLS` | Adresse d’écoute, par exemple `http://localhost:5100` en local |
| `Keycloak__Authority` | URL du realm Keycloak ; HTTPS hors Development |
| `Gateway__Destinations__Customer` | URL de base Customer |
| `Gateway__Destinations__Shipment` | URL de base Shipment |
| `Gateway__Destinations__Tracking` | URL de base Tracking |
| `Gateway__Destinations__Warehouse` | URL de base Warehouse |
| `Gateway__Destinations__Delivery` | URL de base Delivery |
| `Gateway__Destinations__Billing` | URL de base Billing |
| `Gateway__Destinations__Notification` | URL de base Notification |
| `Gateway__Destinations__Reporting` | URL de base Reporting |
| `Gateway__MaximumRequestBodyBytes` | 12582912 par défaut |
| `Gateway__InactivityTimeoutSeconds` | 30 par défaut |
| `Gateway__PublicPermitLimit` | 60 par fenêtre et par adresse |
| `Gateway__PrivatePermitLimit` | 300 par fenêtre et par utilisateur |
| `Gateway__IpPermitLimit` | 1000 par fenêtre et par adresse |
| `Gateway__RateLimitWindowSeconds` | 60 par défaut |
| `Gateway__CorsOrigins__0`, `__1`, etc. | Origines navigateur autorisées |
| `OpenTelemetry__Endpoint` | Export OTLP optionnel |

La configuration est validée au démarrage. Les destinations sont des paramètres d’exploitation, jamais des URL fournies par une requête cliente. Aucun secret n’est stocké dans la configuration du dépôt.

## Exploitation et vérification

`/health/live` vérifie la vivacité de la gateway. `/health/ready` porte sur sa capacité à accepter les requêtes avec sa configuration chargée ; il ne conditionne pas toute la gateway à la disponibilité simultanée des huit services. Une indisponibilité amont affecte le service concerné. `/metrics` expose ses métriques Prometheus ; traces OTLP et logs Serilog complètent l’observabilité.

Les tests utilisent la gateway et de vrais serveurs HTTP amont factices, avec JWT signés et validation cryptographique. Ils couvrent les frontières de routage et d’audience, le suivi public, le transfert des requêtes/réponses, CORS, quotas et pannes. Aucun Docker ni serveur Keycloak n’est nécessaire pour ces tests.

Une vérification complémentaire sous Kestrel réel contrôle le rejet JSON 413 d’un corps chunked dépassant la limite, l’intégrité d’un flux binaire et la propagation de l’annulation du client vers l’amont.

Références techniques : [YARP et authentification](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz), [transfert direct avec YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/direct-forwarding). Le tutoriel de construction global reste réservé à l’étape finale avec le README.
