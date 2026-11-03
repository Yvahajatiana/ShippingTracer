# Chapitre 1 — Socle du monorepo et BuildingBlocks

## Objectif

Poser les fondations communes **avant** d'écrire le moindre service : une solution unique, des règles de compilation strictes et une petite bibliothèque partagée (BuildingBlocks) qui fixe trois conventions transverses : l'enveloppe des événements, la validation des commandes et le format des erreurs HTTP.

L'idée directrice : partager des **conventions techniques**, jamais du **métier**. Aucun service ne référence le projet d'un autre ; ils ne partagent que des contrats de messages versionnés.

## Étape 1 — La solution et les règles de compilation

```powershell
dotnet new sln -n ParcelFlow
dotnet new globaljson --sdk-version 8.0.400
```

`global.json` verrouille la bande SDK 8.0.4xx et refuse une montée implicite vers .NET 9 ou 10 :

```json
{ "sdk": { "version": "8.0.400", "rollForward": "latestPatch", "allowPrerelease": false } }
```

`Directory.Build.props`, à la racine, s'applique à **tous** les projets, y compris ceux créés plus tard :

```xml
<TargetFramework>net8.0</TargetFramework>
<LangVersion>12.0</LangVersion>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<Deterministic>true</Deterministic>
```

> **Pourquoi `TreatWarningsAsErrors` dès le premier jour ?** Activé tard, il impose une campagne de nettoyage. Activé tôt, il ne coûte rien et garde la CI binaire : zéro avertissement ou échec.

## Étape 2 — L'arborescence cible

Le plan du monorepo (`docs/monorepo.md`) est écrit avant le code, mais **aucun projet vide** n'est ajouté à la solution : chaque dossier naît avec son étape.

```text
src/BuildingBlocks/   bibliothèques partagées
src/Services/<Service>/ParcelFlow.<Service>.{Domain,Application,Infrastructure,Api}
src/Gateway/ParcelFlow.Gateway/
tests/BuildingBlocks/, tests/Services/<Service>/{UnitTests,IntegrationTests,ContractTests}
frontends/  deploy/  .github/workflows/  docs/
```

Règle de dépendance de chaque service : Domain ne dépend de rien ; Application → Domain + BuildingBlocks.Application ; Infrastructure → Application, Domain, BuildingBlocks.Contracts ; Api → Application, Infrastructure, BuildingBlocks.Api.

## Étape 3 — Contracts : l'enveloppe des événements

`src/BuildingBlocks/ParcelFlow.BuildingBlocks.Contracts/IntegrationEvent.cs` :

```csharp
public abstract record IntegrationEvent(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
```

- **EventId** est attribué une seule fois par l'émetteur et conservé lors des redélivrances : c'est la clé de déduplication.
- **CorrelationId** relie tous les messages d'un même flux métier.
- **OccurredAtUtc** est toujours en UTC.

Les contrats métier arrivent ensuite service par service, dans des espaces de noms versionnés (`Contracts/<Service>/V1/`). Exemple ajouté au chapitre 3 (`Shipment/V1/ShipmentEvents.cs`) :

```csharp
public sealed record ShipmentReadyForPickup(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid ShipmentId, string TrackingNumber) : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);
```

> **Décision :** un suffixe `V1` dans l'espace de noms plutôt qu'un champ de version. Un changement incompatible crée `V2` à côté de `V1`, les consommateurs migrent à leur rythme.

## Étape 4 — Application : la validation dans le pipeline MediatR

Chaque commande ou requête passe par MediatR ; la validation FluentValidation est un comportement de pipeline commun (`Validation/ValidationBehavior.cs`) :

```csharp
foreach (var validator in validators)
{
    var result = await validator.ValidateAsync(
        new ValidationContext<TRequest>(request), cancellationToken);
    failures.AddRange(result.Errors);
}
if (failures.Count > 0) throw new ValidationException(failures);
return await next();
```

> **Pourquoi séquentiel et pas `Task.WhenAll` ?** Des validateurs peuvent partager un `DbContext` scoped, qui n'est pas thread-safe. Le coût est négligeable, la sûreté réelle.

Branchement dans un service (repris au chapitre 2) : `AddOpenBehavior(typeof(ValidationBehavior<,>))`, et chaque validateur est enregistré explicitement (`AddScoped<IValidator<T>, TValidator>()`), sans bibliothèque de scan.

## Étape 5 — Api : des erreurs ProblemDetails uniformes

`Errors/ProblemDetailsExtensions.cs` fournit deux points d'extension, appelés par chaque API :

```csharp
public static IServiceCollection AddParcelFlowProblemDetails(this IServiceCollection services)
{
    services.Insert(0, ServiceDescriptor.Singleton<IProblemDetailsWriter, ParcelFlowProblemDetailsWriter>());
    services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    });
    services.AddExceptionHandler<ApiExceptionHandler>();
    return services;
}
```

- Le writer ParcelFlow est inséré **en premier** pour toujours répondre en `application/problem+json`, même si le client envoie `Accept: application/xml`.
- `traceId` permet de rapprocher une erreur vue côté client des traces serveur, sans exposer le message de l'exception.
- `ApiExceptionHandler` traduit les exceptions communes : `ValidationException` → 400, autre → 500 générique journalisé.

> **Point d'extension par service :** chaque API enregistre d'abord **son** `IExceptionHandler` (404/403/409 métier), puis le handler commun traite tout le reste. On obtient des erreurs homogènes sans que BuildingBlocks connaisse le métier.

## Étape 6 — Les tests du socle

```powershell
dotnet new xunit -o tests/BuildingBlocks/ParcelFlow.BuildingBlocks.Tests
dotnet sln add tests/BuildingBlocks/ParcelFlow.BuildingBlocks.Tests
```

`ValidationBehaviorTests.cs` couvre l'agrégation des erreurs et l'annulation. Les tests HTTP réels (`ApiExceptionHandlerTests.cs`, `ProblemDetailsHttpTests.cs`) vérifient les réponses 400/500 avec des en-têtes `Accept` JSON et XML et les deux ordres d'enregistrement MVC/ParcelFlow : un défaut d'ordre d'enregistrement avait été relevé en review de la PR #1.

## Ce qui a été ajouté plus tard au socle

Le socle n'est pas figé ; il a été enrichi par des corrections d'audit (chapitre 8), sans changer les contrats existants :

| Ajout | Fichier | Raison |
| --- | --- | --- |
| Projet `BuildingBlocks.Messaging` | `MessageIdentityFilters.cs`, `MessagingConfigurationExtensions.cs` | `MessageId = EventId` pour que l'Inbox MassTransit et la déduplication métier utilisent la même clé (#14) |
| Corrélation ambiante | `Application/Correlation`, `Api/Correlation` | Propager `X-Correlation-Id` et la corrélation des messages consommés |
| `DeterministicGuid` | `Application/Identity/DeterministicGuid.cs` | Un EventId rejoué reste identique (UUID v8 dérivé de SHA-256) |
| `ApiExceptionMapping`, `UseParcelFlowNoStore` | `Api/Errors` | 400 pour les gardes du domaine, 503 + `Retry-After` pour un circuit Polly ouvert, `Cache-Control: no-store` (#21, #22) |

## Décisions et compromis

| Choix | Alternative écartée | Raison |
| --- | --- | --- |
| BuildingBlocks minimal, technique | Bibliothèque « commons » riche | Moins de couplage entre services, montée de version simple |
| Contrats en `record` immuables | Classes DTO mutables | Égalité par valeur, sérialisation prévisible |
| Outbox/Inbox **par service**, pas d'implémentation mémoire partagée | Bus en mémoire dans le socle | La fiabilité repose sur la transaction de chaque base |

## Vérifier

```powershell
dotnet build ParcelFlow.sln --configuration Release
dotnet test tests/BuildingBlocks/ParcelFlow.BuildingBlocks.Tests --configuration Release
```

Attendu : zéro avertissement, zéro erreur, tous les tests verts.

Référence : [docs/monorepo.md](../monorepo.md), [docs/building-blocks.md](../building-blocks.md).

Chapitre suivant : [Premier service : Customer](02-premier-service-customer.md).
