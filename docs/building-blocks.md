# BuildingBlocks — étape 1

## Compilation et tests

Prérequis : SDK .NET 8, bande 8.0.4xx. `global.json` refuse un passage implicite au SDK 9 ou 10. Tous les projets ciblent net8.0 et C# 12, avec nullable et avertissements traités comme erreurs.

```powershell
dotnet restore ParcelFlow.sln
dotnet build ParcelFlow.sln --configuration Release --no-restore
dotnet test ParcelFlow.sln --configuration Release --no-build
```

Sur cette machine, le SDK 8 a été installé dans `.dotnet/` (ignoré par Git). Utiliser `./.dotnet/dotnet.exe` à la place de `dotnet` pour ces commandes. Aucun SDK n'est embarqué dans les livrables.

## Responsabilités

- Contracts : métadonnées techniques immuables des événements d'intégration. L'émetteur attribue EventId une seule fois et le conserve pour les nouvelles livraisons ; CorrelationId relie les messages d'un flux ; OccurredAtUtc utilise UTC. Aucun contrat métier n'est encore défini.
- Application : ValidationBehavior exécute les validateurs FluentValidation asynchrones avant le handler MediatR, agrège les erreurs et propage l'annulation. L'exécution séquentielle permet l'utilisation de dépendances scoped communes.
- Api : erreurs de validation en 400 via HttpValidationProblemDetails, erreurs inattendues en 500 via ProblemDetails. Les erreurs inattendues sont journalisées côté serveur sans exposition de leur message. traceId permet le rapprochement avec les traces. Les réponses de statut sans corps utilisent aussi ProblemDetails.

## Branchement dans les futures API

Dans chaque API, enregistrer MediatR avec l'assembly Application du service et ajouter le pipeline ouvert `ValidationBehavior<,>` via `AddOpenBehavior`. Enregistrer explicitement chaque validateur avec `AddScoped<IValidator<TRequest>, TValidator>()`, sans bibliothèque de scan supplémentaire.

Appeler `builder.Services.AddParcelFlowProblemDetails()` avant la construction de l'application, puis `app.UseParcelFlowProblemDetails()` avant les middlewares et endpoints du service. Les API recevront leurs règles métier et mappings 404/409 lors de leur implémentation.

L'Outbox EF Core et l'inbox MassTransit appartiendront à chaque base de service ; elles partageront la transaction métier. La saga sera persistée dans le service responsable du flux d'envoi. Ces mécanismes ne sont pas remplacés par une implémentation mémoire dans les BuildingBlocks. Les paramètres métier et contrats nécessaires seront précisés à l'étape du service concerné.

## Corrections de review

Le writer ParcelFlow est enregistré avant ceux de MVC et préserve le type HttpValidationProblemDetails. Les erreurs sont toujours sérialisées en application/problem+json, même si Accept ne contient pas JSON ; elles ne dépendent donc pas de la négociation du writer par défaut. Huit tests HTTP réels couvrent les réponses 400/500, les en-têtes Accept JSON/XML et les deux ordres d'enregistrement MVC/ParcelFlow.

## Mapping commun des erreurs (#21, #22)

`ApiExceptionMapping` centralise les erreurs communes à toutes les API. Chaque service enregistre d'abord son propre `IExceptionHandler`, limité à ses exceptions métier (404, 403, 409), qui écrit via `IProblemDetailsService.WriteProblemAsync`. Toute autre exception est traitée par `ApiExceptionHandler` :

| Exception | Statut | Réponse |
|---|---|---|
| `ValidationException` (FluentValidation) | 400 | HttpValidationProblemDetails, erreurs groupées par propriété |
| `ArgumentException` et dérivées (gardes du domaine) | 400 | HttpValidationProblemDetails, détail générique ; seule la clé `ParamName` est exposée si c'est un identifiant simple, avec le message `The value is invalid.` Le message de l'exception reste dans les logs (Warning). |
| `BadHttpRequestException` 413 | 413 | `Request too large` |
| `JsonException`, autre `BadHttpRequestException` | 400 | `Invalid request` |
| `BrokenCircuitException` Polly (y compris `IsolatedCircuitException` et en InnerException, prioritaire sur les mappings 400/413 de l'exception englobante) | 503 | `Service temporarily unavailable`, en-tête `Retry-After` en secondes arrondies au supérieur (durée restante du circuit, 30 s par défaut) |
| Annulation par le client | — | non traitée, aucune réponse écrite |
| Autre | 500 | message générique, exception journalisée en Error |

Le writer ParcelFlow pose `Cache-Control: no-store` sur toute réponse ProblemDetails, quelle que soit son origine (handler d'exception, status code pages, `Results.Problem`). `app.UseParcelFlowNoStore()` (préfixe `/api` par défaut), placé juste après `UseParcelFlowProblemDetails()`, applique `no-store` à toutes les réponses métier via `Response.OnStarting`, ce qui survit à la remise à zéro des en-têtes par le gestionnaire d'exceptions. Billing, Delivery, Warehouse, Notification et Reporting l'utilisent à la place de leurs filtres ou middlewares dédiés.

## Identité des messages et corrélation (#14)

L'enveloppe d'un événement est le record abstrait `IntegrationEvent` (EventId, CorrelationId, OccurredAtUtc), dont dérivent tous les contrats `*.V1` ; un test BuildingBlocks le vérifie par réflexion. Aucun contrat ni format JSON n'a changé.

- Messaging (`ParcelFlow.BuildingBlocks.Messaging`, MassTransit) : `rabbit.UseParcelFlowMessageConventions(context)`, appelé avant `ConfigureEndpoints` par chaque service, enregistre des filtres de publication et d'envoi qui fixent `MessageId = EventId` et `CorrelationId = CorrelationId` pour tout `IntegrationEvent`, y compris les événements de la saga et ceux écrits dans l'Outbox EF Core. L'Inbox MassTransit (clé MessageId) et la déduplication métier (clé EventId) reposent donc sur le même identifiant. Un `MessageId` fixé explicitement par l'appelant reste prioritaire (simulation de redélivrance dans les tests). Les messages sans enveloppe, comme le message de délai interne de la saga, ne sont pas modifiés.
- Corrélation ambiante : `ICorrelationContext` (Application, enregistré par `AddParcelFlowCorrelation()`) est scoped. Dans un consommateur, un filtre de consommation y place la CorrelationId du message reçu (corps, sinon en-tête de transport). Dans une requête HTTP, `app.UseParcelFlowCorrelation()`, placé en premier, accepte un en-tête `X-Correlation-Id` contenant un GUID non vide, sinon en génère un, le renvoie dans la réponse (y compris les erreurs) et l'ajoute au scope de journalisation. Sans source, une valeur est générée une fois pour toute l'opération.
- Règle de corrélation : un événement publié en réaction à un message reprend la corrélation de ce message ; les handlers ne génèrent plus de CorrelationId. Le flux d'envoi garde ShipmentId comme corrélation racine (ShipmentCreated, opérations HTTP sur un envoi : paiement, avoir, scan, tournée, livraison) ; elle est ensuite propagée par la saga, Billing, Tracking, Notification et Reporting. Customer, sans flux métier, utilise la corrélation de la requête HTTP.
- EventId déterministes : `DeterministicGuid.Create(scope, source...)` dérive un UUID version 8 de SHA-256(scope, identifiants), avec le même algorithme que le ScanId de sortie automatique de Warehouse. Une opération rejouée publie ainsi le même EventId. Les EventId restent aléatoires uniquement pour les mises à jour Customer, sans identité d'opération.

| Événement | EventId |
|---|---|
| ShipmentCreated | `shipment:created:` + ShipmentId |
| PaymentRequested, ShipmentReadyForPickup, ShipmentPaymentTimedOut (saga) | `shipment:payment-requested:`, `shipment:ready-for-pickup:`, `shipment:payment-timed-out:` + ShipmentId |
| InvoiceIssued, CreditNoteIssued | `billing:invoice-issued:` + InvoiceId, `billing:credit-note-issued:` + CreditNoteId |
| PaymentCompleted, PaymentFailed | `billing:payment-completed:`, `billing:payment-failed:` + InvoiceId + OperationId |
| ParcelStatusRecorded Delivered, Failed | `delivery:delivered:`, `delivery:failed:` + ShipmentId + OperationId |
| ParcelPickedUp d'une première entrée en centre de tri | `warehouse:parcel-picked-up:` + ScanId |
| ParcelStatusRecorded OutForDelivery | `delivery:out-for-delivery:` + RouteId + ShipmentId |
| TrackingStatusChanged | `tracking:status-changed:` + EventId du fait source |
| NotificationRecorded | `notification:recorded:` + NotificationId |
| ShipmentMetricsUpdated | `reporting:metrics-updated:` + SourceEventId |
| CustomerProfileCreated, CompanyCreated | `customer:profile-created:` + CustomerId, `customer:company-created:` + CompanyId |

L'OperationId fourni par le client n'est jamais repris tel quel comme EventId : il est combiné au type d'événement et à l'agrégat. Un rejeu de la même opération sur le même agrégat garde son EventId, tandis que la même clé réutilisée sur un autre agrégat ou dans un autre service ne peut pas entrer en collision dans l'Inbox ni dans la déduplication des consommateurs. Les enregistrements d'idempotence par OperationId (paiements, avoirs, tentatives de livraison) sont inchangés. Les scans Warehouse gardent ScanId comme EventId. Le Gateway transmet l'en-tête `X-Correlation-Id` du client sans le générer.
