# Delivery Service

Cinquième service métier ParcelFlow : tournées, affectation des livreurs et preuves de livraison. Quatre projets .NET 8/C# 12 suivent les couches Domain, Application, Infrastructure et Api. Les commandes et requêtes utilisent MediatR, FluentValidation et les ProblemDetails communs.

## Tournées et disponibilité

Un profil livreur contient le sujet Keycloak immutable, un nom d’affichage et un indicateur d’activité. Le livreur est un utilisateur de rôle operator : aucun rôle supplémentaire n’est ajouté. Les administrateurs enregistrent et modifient ces profils ; un livreur affecté à une tournée Planned ou InProgress ne peut pas être désactivé. Une affectation Planned peut être remplacée par un autre livreur actif.

Une tournée contient un nom, une date de service, un livreur affecté et des arrêts ordonnés par leur séquence d’ajout. Son état suit Planned → InProgress → Completed. La planification permet d’ajouter ou retirer des colis et de changer le livreur tant que la tournée est Planned. Le démarrage exige un livreur actif et au moins un colis éligible ; la tournée se termine automatiquement quand tous ses arrêts ont une issue Delivered ou Failed. La date sert de référence de planification ; le démarrage est une action explicite, sans ordonnanceur ni optimisation d’itinéraire.

Les événements ShipmentReadyForPickup et ParcelPickedUp rendent ensemble le colis disponible pour l’affectation. Leur ordre d’arrivée est libre : une prise en charge reçue avant le numéro de suivi reste enregistrée en attente. Les relectures identiques ne réinitialisent ni l’affectation ni l’état livré ; une réaffectation de numéro est refusée. Un colis n’a qu’une tournée active à la fois. Un succès est terminal ; un échec libère le colis pour une nouvelle tournée tout en conservant l’issue de l’ancienne.

Le départ physique du dépôt reste de préférence un scan Exit Warehouse, effectué avant la mise en livraison. Delivery ne consulte pas le stock Warehouse et ne bloque pas le démarrage d’une tournée : si un colis est encore en stock, Warehouse consomme le `ParcelStatusRecorded` OutForDelivery publié au démarrage (ou Delivered et Failed, en filet de sécurité) et enregistre une sortie automatique idempotente, auditée sous le sujet `system:delivery-start` ([ADR 0001](adr/0001-warehouse-exit-on-delivery-start.md)). Le stock Warehouse est donc cohérent à terme. Delivery ne déduit pas de sortie de son événement InTransit, également utilisé pour certains scans d’entrée. Ces deux services disposent de bases et de transactions indépendantes.

## Preuves et confidentialité

La confirmation exige le nom du destinataire et au moins une preuve : signature PNG ou photo PNG/JPEG. Les deux peuvent être fournies ensemble. Les images sont des champs JSON base64, persistés en `bytea` dans PostgreSQL ; aucun service de stockage ni bibliothèque d’image supplémentaire n’est ajouté.

- Signature : PNG, au plus 2 MiB.
- Photo : PNG ou JPEG 8 bits usuel, au plus 5 MiB.
- Dimensions déclarées : 1 à 8192 pixels par axe ; corps HTTP limité à 12 MiB, base64 compris.
- Le contrôle vérifie le conteneur PNG, ses chunks et CRC, ou les marqueurs/segments JPEG, ainsi que les bornes et les fins de fichier. Il ne décode pas les pixels compressés.
- Les téléchargements imposent un type image fixé par le serveur, Content-Disposition attachment, X-Content-Type-Options nosniff et Cache-Control no-store.

Seuls le livreur affecté et les administrateurs peuvent démarrer la tournée, confirmer un arrêt, enregistrer son échec ou consulter ses preuves. Les autres opérateurs peuvent planifier et consulter les données opérationnelles, qui ne contiennent ni nom du destinataire ni image. L’acteur de la tentative est issu du claim JWT sub ; un champ acteur envoyé par le client ne le remplace pas. Les événements sortants ne transportent aucun destinataire, sujet d’opérateur ou document.

OperationId est un UUID fourni par le client et unique globalement. Un rejeu avec le même arrêt, résultat, acteur et contenu renvoie le reçu initial sans mutation ni publication, même après une replanification. Une réutilisation contradictoire retourne 409. Un arrêt ne peut recevoir qu’une seule issue. Les motifs d’échec sont RecipientAbsent, AddressNotFound et RecipientRefused ; aucun texte libre contenant une adresse ou d’autres données personnelles n’est publié.

## API v1

Préfixe : `/api/v1/delivery`. Toutes les routes métier exigent un JWT Keycloak valide pour l’audience delivery-api et un rôle operator ou admin. Le rôle customer n’accède pas à cette API interne.

| Méthode et route relative | Accès et effet |
| --- | --- |
| POST `/drivers` | admin : crée un profil livreur, 201 |
| PUT `/drivers/{subject}` | admin : nom et activité |
| GET `/drivers` | operator/admin : liste paginée |
| POST `/routes` | operator/admin : crée une tournée Planned, 201 |
| GET `/routes` | operator/admin : liste paginée |
| GET `/routes/{routeId}` | operator/admin : détail et séquence des arrêts |
| PUT `/routes/{routeId}/driver` | operator/admin : affecte un livreur actif |
| POST `/routes/{routeId}/stops` | operator/admin : affecte un colis disponible |
| DELETE `/routes/{routeId}/stops/{shipmentId}` | operator/admin : retire un arrêt Planned, 204 |
| POST `/routes/{routeId}/start` | livreur affecté/admin : mise en livraison |
| POST `/routes/{routeId}/stops/{shipmentId}/delivery` | livreur affecté/admin : confirmation et preuves |
| POST `/routes/{routeId}/stops/{shipmentId}/failure` | livreur affecté/admin : échec motivé |
| GET `/routes/{routeId}/stops/{shipmentId}/proof` | livreur affecté/admin : reçu de l’issue et indicateurs de preuves |
| GET `/routes/{routeId}/stops/{shipmentId}/proof/{kind}` | livreur affecté/admin : image Signature ou Photo, 404 si absente |
| GET `/parcels/available` | operator/admin : colis éligibles non affectés et non livrés |

La pagination accepte page=1..1000000 et pageSize=1..100, par défaut 1 et 50. Les lectures opérationnelles et les preuves ne sont pas mises en cache. Les résultats paginés sont lus à l’isolation ReadCommitted ; le total et les éléments peuvent refléter des instants proches sous écritures concurrentes.

Les champs de décision IsActive, ServiceDate, ShipmentId, OperationId et Reason sont explicitement obligatoires dans leurs corps respectifs. Une omission ne choisit pas implicitement une valeur d’énumération ou un indicateur booléen. Les erreurs retournent 400 pour une validation, 401/403 pour l’accès, 404 pour une ressource absente, 409 pour un conflit métier et 413 pour un corps trop volumineux. Swagger est disponible en Development sur `/swagger`, avec des énumérations décrites comme des chaînes conformément au JSON réel.

Exemple de création de tournée :

```json
{ "name": "Lyon Gerland — matin", "serviceDate": "2026-10-18" }
```

Exemple de signalement d’échec :

```json
{ "operationId": "62000000-0000-0000-0000-000000000001", "reason": "RecipientAbsent" }
```

Une confirmation utilise OperationId, RecipientName et les champs optionnels SignaturePng/Photo contenant les octets encodés en base64. Leur absence simultanée est refusée. Aucune URL externe ou type MIME fourni par le client n’est utilisé pour récupérer ou servir les documents.

## Persistance et événements

La base dédiée parcelflow_delivery contient les livreurs, les tournées, les colis connus, les arrêts, les tentatives et les tables MassTransit Inbox/Outbox. Les identités et le numéro de suivi sont uniques ; les arrêts référencent un colis, les tentatives référencent leur arrêt par clé composite et une contrainte garantit une issue par arrêt. Les preuves historiques sont protégées par des clés étrangères restrictives. Les arrêts retirés d’une tournée Planned peuvent être supprimés, avant toute tentative.

Les commandes s’exécutent dans une transaction ReadCommitted. Les consommateurs réutilisent celle de leur Outbox ; les écritures HTTP englobent règles métier, modifications et publications Outbox dans une même transaction. Les verrous PostgreSQL suivent l’ordre tournée → livreur → colis, les colis multiples étant triés par identifiant. La modification d’un livreur ne verrouille que ce livreur et vérifie les tournées ouvertes par lecture. Les projections d’événements ne prennent que le verrou du colis. xmin et Revision protègent également les agrégats.

MassTransit utilise des files durables préfixées delivery-*, Inbox/Outbox EF Core, un démarrage attendu du bus et des retries bornés avant conservation en erreur. Les contrats existants de BuildingBlocks suffisent :

- ShipmentReadyForPickup associe le numéro de suivi au colis.
- ParcelPickedUp enregistre la prise en charge physique.
- Le démarrage publie ParcelStatusRecorded avec OutForDelivery pour chaque arrêt.
- L’issue publie Delivered ou Failed, avec EventId égal à OperationId, CorrelationId égal à ShipmentId et FacilityCode null.

Les dates sont générées côté serveur en UTC, à la microseconde, et progressent pour les événements Delivery d’un même colis même si l’horloge recule. Une erreur annule ensemble l’affectation, l’issue, les octets de preuve et l’Outbox. Polly applique retry et circuit breaker aux lectures PostgreSQL transitoires. Redis reste configuré pour le socle et la readiness, sans cache de documents ou de tournées.

## Lancement à cette étape

Prérequis : .NET 8, PostgreSQL, RabbitMQ et Redis accessibles. Fournir les secrets par variables d’environnement ; le lancement groupé Docker et le provisionnement Keycloak restent aux étapes prévues.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5105'
$env:ConnectionStrings__DeliveryDatabase = 'Host=localhost;Port=5432;Database=parcelflow_delivery;Username=parcelflow;Password=local-password'
$env:ConnectionStrings__Redis = 'localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000'
$env:RabbitMq__Host = 'localhost'
$env:RabbitMq__Username = 'parcelflow'
$env:RabbitMq__Password = 'local-password'
$env:Database__Initialize = 'true'
$env:Database__SeedDemoData = 'true'
dotnet run --project src/Services/Delivery/ParcelFlow.Delivery.Api
```

L’initialisation au démarrage applique les migrations en Development uniquement. Hors Development :

```powershell
dotnet tool restore
dotnet ef database update --project src/Services/Delivery/ParcelFlow.Delivery.Infrastructure
```

Le seed optionnel et idempotent fournit demo-driver-lyon/demo-driver-paris, une tournée Planned non affectée (`60000000-0000-0000-0000-000000000001`, date 2026-10-18) et deux colis pris en charge disponibles PF600000000000000000000001/002. Ces sujets devront correspondre à des utilisateurs Keycloak operator lors du provisionnement ultérieur ; il ne s’agit pas de comptes d’authentification créés par Delivery. Aucune preuve ni publication fictive n’est générée.

Configurer Keycloak__Authority et Keycloak__Audience=delivery-api. Les métadonnées exigent HTTPS hors Development. `/health/live` vérifie le processus ; `/health/ready` vérifie PostgreSQL, Redis et MassTransit. Serilog écrit sur la console ; OpenTelemetry instrumente HTTP et MassTransit, `/metrics` expose Prometheus et OpenTelemetry__Endpoint active l’export OTLP.

## Vérification et organisation

Trois agents ont possédé des lots distincts après fixation des interfaces : Domain/Application/unitaires, Infrastructure/migrations/intégrations et API/tests HTTP. Le parent a fourni la validation des conteneurs d’images, les tests associés, les contrats, la documentation et un workflow sur trois bases séparées : prise en charge et sortie Warehouse → première tournée en échec → replanification → preuve et livraison → suivi public Livré. Les migrations et compilations ont été séquencées.

```powershell
dotnet test ParcelFlow.sln --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Delivery/ParcelFlow.Delivery.Infrastructure --configuration Release
dotnet list ParcelFlow.sln package --vulnerable --include-transitive
```

Résultats vérifiés : compilation Release du monorepo sans avertissement ni erreur ; 360 tests réussis, aucun échec ni test ignoré. Delivery compte 26 tests unitaires, 43 intégrations et 3 contrats, en complément des 288 tests existants. La migration correspond au modèle EF et NuGet ne signale aucun package vulnérable connu, dépendances transitives incluses. Les contrôles couvrent les PNG/JPEG réels et leurs variantes invalides, la confidentialité des preuves, les JWT, les champs obligatoires et Swagger, les affectations et résultats concurrents, le rollback SQL et le parcours de replanification sur trois bases séparées.
