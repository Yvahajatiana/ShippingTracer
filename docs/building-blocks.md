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
| `BrokenCircuitException` Polly (y compris en InnerException, et `IsolatedCircuitException`) | 503 | `Service temporarily unavailable`, en-tête `Retry-After` en secondes arrondies au supérieur (durée restante du circuit, 30 s par défaut) |
| Annulation par le client | — | non traitée, aucune réponse écrite |
| Autre | 500 | message générique, exception journalisée en Error |

Le writer ParcelFlow pose `Cache-Control: no-store` sur toute réponse ProblemDetails, quelle que soit son origine (handler d'exception, status code pages, `Results.Problem`). `app.UseParcelFlowNoStore()` (préfixe `/api` par défaut), placé juste après `UseParcelFlowProblemDetails()`, applique `no-store` à toutes les réponses métier via `Response.OnStarting`, ce qui survit à la remise à zéro des en-têtes par le gestionnaire d'exceptions. Billing, Delivery, Warehouse, Notification et Reporting l'utilisent à la place de leurs filtres ou middlewares dédiés.
