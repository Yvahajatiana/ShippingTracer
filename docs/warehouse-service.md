# Warehouse Service

Quatrième service métier ParcelFlow : gestion des entrepôts et centres de tri, scans d’entrée/sortie et stock courant. Les projets Domain, Application, Infrastructure et Api utilisent .NET 8/C# 12, MediatR et FluentValidation, avec les ProblemDetails du socle commun.

## Règles métier

Un site possède un code unique immuable, un nom, un type `Warehouse` ou `SortingCenter`, une adresse structurée et un indicateur d’activité. Le code est normalisé en majuscules, limité à 40 caractères ASCII alphanumériques ou tirets, et sert de FacilityCode dans Tracking. Le type reste immuable pour préserver le sens de l’historique. Les champs d’adresse sont Street, City, PostalCode et CountryCode ; le pays est normalisé en deux lettres majuscules.

ShipmentReadyForPickup, publié après acceptation du paiement par la saga Shipment, rend le colis éligible aux scans. Cet événement enregistre une association immuable entre ShipmentId et numéro de suivi. Un événement répété ne réinitialise pas le stock ; une association contradictoire échoue. Un scan avant réception de cette projection retourne 409 : il peut être retenté après propagation de l’événement.

Un scan Entry exige un entrepôt actif et un colis actuellement hors stock. Exit exige que le colis se trouve dans l’entrepôt indiqué. Chaque colis ne peut occuper qu’un site à la fois ; un transfert s’effectue par une sortie puis une entrée. Un site contenant des colis ne peut pas être désactivé. Aucun endpoint de suppression, de réservation ou de livraison n’est ajouté.

ScanId est un UUID fourni par le client et unique globalement. Le sens du scan doit être fourni explicitement : son omission retourne 400. Réutiliser ce ScanId avec le même colis, site, sens et sujet opérateur retourne le reçu initial, même après des mouvements ultérieurs. Une réutilisation contradictoire retourne 409. Un nouveau ScanId représente une nouvelle opération et doit respecter l’état courant.

L’acteur provient exclusivement du claim JWT `sub`. L’heure du scan est générée côté serveur en UTC, à la microseconde ; elle progresse d’au moins une microseconde pour chaque mouvement du même colis, même en cas de recul de l’horloge. L’audit conserve les scans et leur acteur en base interne ; aucun acteur ou renseignement personnel n’est envoyé aux autres services.

## API v1

Toutes les routes métier exigent un JWT Keycloak valide pour l’audience `warehouse-api`. Les rôles du realm sont vérifiés comme dans les services précédents. Le client customer ne dispose d’aucun accès Warehouse.

| Méthode et route | Rôles | Effet |
| --- | --- | --- |
| POST `/api/v1/warehouses` | admin | Crée un site actif, renvoie 201 et Location |
| PUT `/api/v1/warehouses/{warehouseId}` | admin | Modifie le nom, l’adresse et l’activité |
| GET `/api/v1/warehouses` | operator, admin | Liste paginée des sites |
| GET `/api/v1/warehouses/{warehouseId}` | operator, admin | Détail d’un site |
| GET `/api/v1/warehouses/{warehouseId}/stock` | operator, admin | Stock paginé : ShipmentId, TrackingNumber et LastScanAtUtc |
| POST `/api/v1/warehouses/scans` | operator, admin | Enregistre ou rejoue un scan |

La pagination accepte `page=1..1000000` et `pageSize=1..100` ; les valeurs par défaut sont 1 et 50. Les sites sont triés par code et les colis par numéro de suivi. Les réponses métier portent `Cache-Control: no-store`. Le stock est lu directement dans PostgreSQL afin de refléter les mouvements validés, sans délai de cache Redis. Les listes et leur total sont lus à l’isolation ReadCommitted et peuvent refléter des instants proches si des mouvements sont concurrents.

Exemple de création :

```json
{
  "code": "FR-LYON-01",
  "name": "Dépôt Lyon Part-Dieu",
  "kind": "Warehouse",
  "street": "24 avenue des Frères Lumière",
  "city": "Lyon",
  "postalCode": "69008",
  "countryCode": "FR"
}
```

Exemple de scan d’entrée sur le premier site de démonstration :

```json
{
  "scanId": "52000000-0000-0000-0000-000000000001",
  "warehouseId": "50000000-0000-0000-0000-000000000001",
  "trackingNumber": "PF500000000000000000000001",
  "direction": "Entry"
}
```

Une requête invalide retourne 400, un site inconnu 404 et un conflit de stock, d’identité ou de code 409. Kind à la création, Direction au scan et IsActive à la modification doivent être fournis explicitement. Les réponses d’erreur utilisent les ProblemDetails communs, sans exposer les détails PostgreSQL. Swagger est disponible sur `/swagger` en Development ; les énumérations y sont décrites comme des chaînes, conformément aux réponses JSON.

## Événements et atomicité

Les contrats existants de BuildingBlocks suffisent : aucune nouvelle version de contrat partagé n’est nécessaire.

- `ShipmentReadyForPickup` est consommé sur la file durable préfixée `warehouse-*`, avec Inbox/Outbox MassTransit.
- Le premier scan d’entrée publie `ParcelPickedUp`, qui termine la saga Shipment et alimente Tracking. Pour une première entrée en entrepôt normal, cet événement est l’unique fait publié, avec EventId égal à ScanId.
- Une entrée en centre de tri publie `ParcelStatusRecorded` avec AtSortingCenter et le code du site. Si c’est aussi la première entrée du colis, ParcelPickedUp est publié avec un EventId distinct, dérivé du ScanId (`warehouse:parcel-picked-up:`) : la prise en charge et le passage au tri sont deux faits à la même date.
- Une sortie ou une entrée ultérieure en entrepôt normal publie `ParcelStatusRecorded` avec InTransit. Son EventId est ScanId ; CorrelationId est ShipmentId.
- `ParcelStatusRecorded` est aussi consommé (file `warehouse-parcel-delivery-started`) pour la sortie automatique au départ en livraison ([ADR 0001](adr/0001-warehouse-exit-on-delivery-start.md)). Seuls les statuts publiés par Delivery sont pris en compte : OutForDelivery au démarrage d’une tournée, puis Delivered et Failed comme filet de sécurité si le départ a été manqué (consommateur déployé en cours de tournée par exemple) ; les autres, dont ceux publiés par Warehouse, sont ignorés. Si le colis est encore en stock, un scan Exit est enregistré dans son site courant avec le sujet `system:delivery-start`, l’heure de l’événement source et un ScanId UUID v8 dérivé de son EventId : une redélivrance, même sous un nouvel identifiant de transport, ne produit qu’une sortie. Un colis hors stock, inconnu, ou ayant bougé après l’heure de l’événement (retour au dépôt après un échec par exemple) n’est pas modifié : une redélivrance de l’événement Failed ne retire donc pas un colis revenu au dépôt. Cette sortie ne publie aucun événement : Tracking affiche déjà En livraison, Livré ou Échec, et un InTransit tardif ferait régresser l’historique public.

La base dédiée `parcelflow_warehouse` contient les sites, les colis éligibles et leur emplacement courant, les scans, ainsi que les tables Inbox/Outbox. Les scans ont des clés étrangères obligatoires vers le site et le colis. Les suppressions référencées sont interdites ; les identités et les codes uniques sont protégés par des contraintes PostgreSQL.

Les commandes CQRS s’exécutent dans une transaction ReadCommitted. Les consommateurs réutilisent la transaction de leur Outbox ; les écritures HTTP disposent d’une transaction qui englobe lecture, règles métier, scan, stock et Outbox. Un verrou PostgreSQL par numéro de suivi sérialise les mouvements du même colis ; un verrou par site protège aussi la désactivation face aux entrées concurrentes. Les commandes prennent les verrous dans l’ordre colis puis site ; une mise à jour de site ne prend que le verrou du site. xmin protège également les mises à jour concurrentes.

Une erreur annule les changements et les messages en attente dans la même transaction. La publication RabbitMQ est asynchrone et réessayée par l’Outbox après commit. Les consommateurs sont idempotents ; des retries bornés traitent les échecs, puis MassTransit conserve les messages en erreur. Polly applique retry et circuit breaker aux lectures paginées PostgreSQL en cas d’erreur transitoire.

## Lancement à cette étape

Prérequis : .NET 8, PostgreSQL, RabbitMQ et Redis accessibles. Les secrets sont fournis par variables d’environnement. Le lancement groupé Docker et le provisionnement Keycloak restent aux étapes prévues.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5104'
$env:ConnectionStrings__WarehouseDatabase = 'Host=localhost;Port=5432;Database=parcelflow_warehouse;Username=parcelflow;Password=local-password'
$env:ConnectionStrings__Redis = 'localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000'
$env:RabbitMq__Host = 'localhost'
$env:RabbitMq__Username = 'parcelflow'
$env:RabbitMq__Password = 'local-password'
$env:Database__Initialize = 'true'
$env:Database__SeedDemoData = 'true'
dotnet run --project src/Services/Warehouse/ParcelFlow.Warehouse.Api
```

`Database:Initialize=true` applique les migrations au démarrage en Development uniquement. Le seed est distinct, optionnel et idempotent : dépôt FR-LYON-01 (`50000000-0000-0000-0000-000000000001`), centre de tri FR-PARIS-01 (`50000000-0000-0000-0000-000000000002`), et deux colis éligibles PF500000000000000000000001/002 initialement hors stock. Ces données fictives ne publient pas d’événements et ne constituent pas un parcours de paiement réel.

Hors Development, appliquer la migration explicitement :

```powershell
dotnet tool restore
dotnet ef database update --project src/Services/Warehouse/ParcelFlow.Warehouse.Infrastructure
```

Configurer `Keycloak__Authority` avec l’URL du realm et `Keycloak__Audience=warehouse-api`. HTTPS est requis pour les métadonnées hors Development. `/health/live` vérifie le processus ; `/health/ready` vérifie PostgreSQL, Redis et MassTransit. Redis est configuré pour le socle et la readiness, sans cache de stock. Serilog écrit sur la console ; OpenTelemetry instrumente HTTP et MassTransit, `/metrics` expose Prometheus et `OpenTelemetry__Endpoint` active l’export OTLP.

## Vérification et organisation

Trois agents ont travaillé sur des fichiers distincts après fixation des interfaces : Domain/Application/unitaires, Infrastructure/migrations/intégrations et API/tests HTTP. Les compilations et migrations ont été séquencées. Le parent a intégré la solution, les contrats de compatibilité et un test de workflow sur trois bases séparées : ShipmentCreated → paiement accepté → projection Warehouse → prise en charge → sortie → entrée au tri → saga terminée et historique Tracking. Le paiement est simulé par son contrat existant, conformément à l’étape Billing encore à venir.

```powershell
dotnet test ParcelFlow.sln --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Warehouse/ParcelFlow.Warehouse.Infrastructure --configuration Release
dotnet list ParcelFlow.sln package --vulnerable --include-transitive
```

Résultats vérifiés : compilation Release du monorepo sans avertissement ni erreur ; 288 tests réussis à la livraison de l’étape, aucun échec ni test ignoré, dont les 216 tests précédents. Depuis la sortie automatique au départ en livraison (ADR 0001), Warehouse compte 34 tests unitaires, 46 intégrations et 2 contrats, tous réussis. La migration correspond au modèle EF et NuGet ne signale aucun package vulnérable connu, dépendances transitives incluses. Les intégrations couvrent notamment les 20 scans concurrents, l’entrée face à la désactivation, l’annulation stock/scan/Outbox après erreur SQL et le parcours sur trois bases distinctes. Swagger et les champs obligatoires sont vérifiés par HTTP réel.
