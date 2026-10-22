# Tracking Service

Troisième service métier de ParcelFlow : historique des colis et consultation publique. Quatre projets .NET 8/C# 12 suivent les mêmes couches Domain, Application, Infrastructure et Api que les services existants. Les commandes et requêtes passent par MediatR et FluentValidation ; les erreurs HTTP utilisent les ProblemDetails communs.

## API v1 et confidentialité

| Route GET | Accès | Résultat |
| --- | --- | --- |
| `/api/v1/tracking/{trackingNumber}` | Public | Statut courant, libellés français, dates UTC et codes de centre |
| `/api/v1/tracking/shipments/{shipmentId}` | JWT Keycloak, rôle `operator` ou `admin` | Historique interne avec identifiants techniques, même avant réception du numéro de suivi |

Le numéro accepte les espaces extérieurs et les minuscules, puis est normalisé en `PF` suivi de 24 caractères hexadécimaux majuscules. La réponse publique ne contient aucun nom, adresse, e-mail, identifiant client, identifiant d’envoi ou identifiant d’événement. Les dates sont normalisées en UTC à la précision PostgreSQL de la microseconde.

Exemple de réponse publique pour le premier colis de démonstration :

```json
{
  "trackingNumber": "PF400000000000000000000001",
  "currentStatus": "InTransit",
  "currentStatusLabel": "En transit",
  "updatedAtUtc": "2026-10-16T10:00:00+00:00",
  "history": [
    { "status": "Created", "statusLabel": "Créé", "occurredAtUtc": "2026-10-15T08:00:00+00:00", "facilityCode": null },
    { "status": "InTransit", "statusLabel": "En transit", "occurredAtUtc": "2026-10-16T10:00:00+00:00", "facilityCode": "FR-LYON-01" }
  ]
}
```

`400` signale une requête invalide, `404` un colis inconnu, `401/403` protègent la consultation interne. Les réponses portent `Cache-Control: no-store`. Swagger est disponible en environnement Development sur `/swagger`.

## Historique et événements

| Statut contractuel | Libellé |
| --- | --- |
| Created | Créé |
| PickedUp | Pris en charge |
| InTransit | En transit |
| AtSortingCenter | En centre de tri |
| OutForDelivery | En livraison |
| Delivered | Livré |
| Failed | Échec |
| Returned | Retour |

Les faits sont conservés et triés par date de survenue, puis par ordre sémantique déterministe et identifiant d’événement. Un fait Created tardif ne réinitialise pas un colis déjà pris en charge. Delivered conserve la priorité sur les événements ultérieurs sauf Returned ; Returned est terminal. Les autres statuts suivent la chronologie. Un échec peut donc être suivi d’une nouvelle mise en livraison. À date identique, la priorité croît dans cet ordre : Created, PickedUp, InTransit, AtSortingCenter, OutForDelivery, Failed, Delivered, Returned. `updatedAtUtc` correspond au fait qui détermine le statut courant.

Les contrats versionnés résident dans `ParcelFlow.BuildingBlocks.Contracts` :

- `ShipmentCreated` enregistre le numéro et le fait Created.
- `ParcelPickedUp` enregistre PickedUp, y compris s’il arrive avant ShipmentCreated. Le dossier reste alors interne jusqu’à son association à un numéro.
- `ParcelStatusRecorded` reçoit les faits des futurs services Warehouse et Delivery. Le code de centre optionnel est limité à 40 caractères ASCII majuscules, chiffres et tirets ; aucun texte libre ne transporte de données personnelles.
- `TrackingStatusChanged` publie chaque nouveau fait enregistré et son `SourceEventId`, avec la date de survenue initiale. À la première association d’un numéro, les faits précédemment en attente sont publiés une fois. Les consommateurs doivent dédupliquer les faits par SourceEventId et utiliser OccurredAtUtc pour leur chronologie.

Aucun endpoint HTTP de modification n’est ajouté. Le paiement et ShipmentReadyForPickup ne constituent pas une prise en charge physique et ne changent pas le suivi.

## Persistance, concurrence et cache

La base dédiée `parcelflow_tracking` contient les dossiers, les faits et les tables MassTransit Inbox/Outbox. La migration initiale définit une clé globale EventId, un numéro unique et une clé étrangère obligatoire pour chaque fait. Un rejeu identique ne modifie rien et ne republie rien ; une réutilisation contradictoire d’identifiant ou une réaffectation du numéro échoue.

Les files durables préfixées `tracking-*` utilisent les consommateurs MassTransit avec Inbox/Outbox EF Core et retries. L’enregistrement métier et les publications sont atomiques. Un verrou transactionnel PostgreSQL par identifiant d’envoi sérialise les mises à jour du même colis, même lors de sa création, tandis que les autres colis restent parallélisables. L’isolation ReadCommitted permet de lire les changements après acquisition du verrou ; xmin et Revision protègent aussi les modifications concurrentes. Les événements invalides épuisent les retries puis suivent le mécanisme de fault/error de MassTransit.

Polly applique retry et circuit breaker aux lectures PostgreSQL transitoires hors transaction (API). Dans la transaction de l’Outbox consommateur, aucun retry Polly n’est appliqué : PostgreSQL annule la transaction à la première erreur, un nouvel essai y serait voué à l’échec ; c’est le retry de message MassTransit qui rejoue le consommateur dans une transaction neuve.

Redis conserve uniquement le DTO public pendant 15 secondes sous `parcelflow:tracking:public:v1:{NUMERO}`. L’invalidation a lieu après le commit de la transaction ouverte par l’Outbox : la sauvegarde ne fait qu’enregistrer les numéros concernés, puis l’intercepteur EF Core `TrackingCacheInvalidator` (`IDbTransactionInterceptor`) supprime les clés lorsque le commit est confirmé et les abandonne en cas de rollback ou d’échec. Une écriture hors transaction explicite est déjà validée à la fin de la sauvegarde et invalide donc immédiatement. Ainsi, une lecture concurrente ne peut plus remettre en cache la projection antérieure pendant la fenêtre entre sauvegarde et commit. Chaque opération de cache dispose de 250 ms ; une panne Redis lors de l’invalidation est journalisée sans remettre en cause l’écriture validée, et une panne ou un JSON illisible en lecture déclenche une lecture en base. Les absences ne sont pas cachées. Une lecture commencée avant le commit et terminée après l’invalidation peut encore réinsérer une ancienne projection : la cohérence du cache reste donc éventuelle, bornée par son expiration de 15 secondes.

## Exécution du service

Prérequis à ce stade : SDK .NET 8, PostgreSQL, RabbitMQ et Redis accessibles. Le lancement groupé Docker et le provisionnement Keycloak seront livrés aux étapes prévues. Les secrets sont fournis par variables d’environnement, jamais enregistrés dans appsettings.json.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5103'
$env:ConnectionStrings__TrackingDatabase = 'Host=localhost;Port=5432;Database=parcelflow_tracking;Username=parcelflow;Password=local-password'
$env:ConnectionStrings__Redis = 'localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000'
$env:RabbitMq__Host = 'localhost'
$env:RabbitMq__Username = 'parcelflow'
$env:RabbitMq__Password = 'local-password'
$env:Database__Initialize = 'true'
$env:Database__SeedDemoData = 'true'
dotnet run --project src/Services/Tracking/ParcelFlow.Tracking.Api
```

En Development uniquement, `Database:Initialize=true` applique les migrations au démarrage. Le seed, distinct et optionnel, est idempotent : `PF400000000000000000000001` est en transit et `PF400000000000000000000002` est livré. Ces données fictives ne publient pas d’événements métier. Hors Development, appliquer la migration explicitement :

```powershell
dotnet tool restore
dotnet ef database update --project src/Services/Tracking/ParcelFlow.Tracking.Infrastructure
```

Configurer `Keycloak__Authority` avec l’URL du realm et `Keycloak__Audience=tracking-api`. Les jetons internes exigent une signature valide, une expiration valide, un sujet non vide et les rôles du realm. HTTPS est requis pour les métadonnées hors Development.

Serilog écrit les journaux sur la console. OpenTelemetry instrumente HTTP et MassTransit ; `/metrics` expose Prometheus et `OpenTelemetry__Endpoint` active l’export OTLP. `/health/live` vérifie le processus ; `/health/ready` vérifie PostgreSQL, Redis et le bus MassTransit. Aucun secret ou détail d’infrastructure n’est retourné dans les health checks.

## Vérifications

Les projets UnitTests, IntegrationTests et ContractTests couvrent les règles de projection, les huit statuts sur le fil JSON, les ProblemDetails, les JWT réels, la confidentialité et le cache dégradé. Les tests Testcontainers utilisent PostgreSQL, RabbitMQ et Redis réels : migrations, seed répété, transaction annulée, collision globale EventId, prise en charge avant création, rejeu à la microseconde, invalidation du cache après commit (et non après rollback) et 22 faits concurrents sur trois files avec une publication par fait.

```powershell
dotnet test ParcelFlow.sln --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Tracking/ParcelFlow.Tracking.Infrastructure --configuration Release
dotnet list ParcelFlow.sln package --vulnerable --include-transitive
```

Les tâches Domain/Application, Infrastructure et API ont été déléguées sur des fichiers distincts après accord sur les interfaces. Les migrations et compilations ont été séquencées, puis la solution entière a été vérifiée avant ouverture de la PR.

Résultat vérifié : 216 tests monorepo réussis, aucun échec ni test ignoré, dont 31 unitaires, 29 intégrations et 11 contrats Tracking. Compilation Release sans avertissement ni erreur, aucune dérive du modèle EF et aucun package vulnérable connu signalé par NuGet, dépendances transitives incluses.
