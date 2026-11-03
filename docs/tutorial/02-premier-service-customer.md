# Chapitre 2 — Premier service : Customer, le gabarit

## Objectif

Construire le premier microservice de bout en bout. Customer est volontairement simple (profils, adresses, entreprises), ce qui permet de figer le **gabarit** que les sept services suivants reprendront : quatre couches, MediatR + FluentValidation, EF Core + PostgreSQL, Outbox MassTransit, Redis, Polly, JWT Keycloak, observabilité et tests Testcontainers.

## Étape 1 — Créer les quatre projets

```powershell
$s = "src/Services/Customer"
dotnet new classlib -o $s/ParcelFlow.Customer.Domain
dotnet new classlib -o $s/ParcelFlow.Customer.Application
dotnet new classlib -o $s/ParcelFlow.Customer.Infrastructure
dotnet new web      -o $s/ParcelFlow.Customer.Api
dotnet sln add (Get-ChildItem $s -Recurse -Filter *.csproj)
```

Puis les références selon la règle du chapitre 1 (Api → Application + Infrastructure + BuildingBlocks.Api, etc.). Fichiers produits :

```text
Domain/Entities.cs                         agrégats et gardes
Application/Contracts.cs, Handlers.cs, Validation.cs, DependencyInjection.cs
Infrastructure/Persistence/{CustomerDbContext, CustomerDbContextFactory, CustomerStore, DatabaseInitialization}.cs
Infrastructure/DependencyInjection.cs, HealthChecks.cs
Api/Program.cs, CustomerEndpoints.cs, CustomerExceptionHandler.cs, HttpCurrentUser.cs
```

## Étape 2 — Un domaine qui se protège seul

Les invariants vivent dans les constructeurs et méthodes des entités (`Domain/Entities.cs`), pas dans les contrôleurs :

```csharp
public CustomerProfile(string keycloakSubject, string firstName, string lastName, string email, string? phone)
{
    Id = Guid.NewGuid();
    KeycloakSubject = Guard.Text(keycloakSubject, 200, nameof(keycloakSubject));
    Update(firstName, lastName, email, phone);
    CreatedAtUtc = DateTimeOffset.UtcNow;
}
```

`Guard` normalise (trim), borne les longueurs et valide l'email. Une violation lève `ArgumentException`, traduite en 400 par le socle.

> **Décision produit :** Keycloak reste propriétaire de l'identité (mots de passe, rôles). Customer ne stocke que le sujet `sub`, unique et immuable. Pas de suppression exposée : les envois et factures futurs doivent garder leurs références.

## Étape 3 — Application : commandes, validateurs, enregistrement explicite

`Application/DependencyInjection.cs` :

```csharp
services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
services.AddScoped<IValidator<CreateProfile>, CreateProfileValidator>();
// ... un AddScoped par validateur
```

Les contrôles d'accès métier (« un client ne lit que son profil ») sont dans les handlers (`CustomerAccess`) **et** dans les politiques HTTP : défense en profondeur.

## Étape 4 — Infrastructure : base dédiée, Outbox, cache, résilience

**Une base par service** (`parcelflow_customers`) : aucun autre service n'y accède. La concurrence optimiste utilise la colonne système PostgreSQL `xmin` :

```csharp
entity.Property<uint>("Version").IsRowVersion();
modelBuilder.AddInboxStateEntity();
modelBuilder.AddOutboxMessageEntity();
modelBuilder.AddOutboxStateEntity();
```

**Outbox transactionnelle** (`Infrastructure/DependencyInjection.cs`) : l'événement est écrit dans la même transaction que les données, puis livré à RabbitMQ par un worker.

```csharp
bus.AddEntityFrameworkOutbox<CustomerDbContext>(outbox =>
{
    outbox.UsePostgres();
    outbox.UseBusOutbox();
    outbox.QueryDelay = TimeSpan.FromSeconds(1);
});
```

> **Pourquoi l'Outbox ?** Sans elle, un `SaveChanges` réussi suivi d'un `Publish` échoué laisse un état sans événement (ou l'inverse). Avec elle, les deux réussissent ou échouent ensemble ; la livraison devient « au moins une fois », d'où la déduplication côté consommateur.

**Polly** protège uniquement les **lectures** PostgreSQL : deux tentatives sur erreur transitoire, puis circuit breaker.

```csharp
.AddRetry(new RetryStrategyOptions
{
    ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(exception => exception.IsTransient)
        .Handle<TimeoutException>(),
    MaxRetryAttempts = 2, Delay = TimeSpan.FromMilliseconds(100),
    BackoffType = DelayBackoffType.Exponential, UseJitter = true
})
.AddCircuitBreaker(new CircuitBreakerStrategyOptions { FailureRatio = 0.5, MinimumThroughput = 10, /* ... */ })
```

> **Pourquoi pas les écritures ?** Rejouer une écriture non idempotente peut créer un doublon. Un conflit retourne 409 et c'est au client de réessayer.

**Redis** ne met en cache que la liste des entreprises (60 s), invalidée après validation de la transaction. Une panne Redis dégrade en lecture PostgreSQL, sans erreur.

**Migrations** : `dotnet ef migrations add InitialCustomer --project src/Services/Customer/ParcelFlow.Customer.Infrastructure`. La `CustomerDbContextFactory` (design time) lit `ConnectionStrings__CustomerDatabase`.

## Étape 5 — Api : minimal APIs versionnées, JWT Keycloak

Les endpoints sont des minimal APIs groupées et versionnées dans l'URL (`Api/CustomerEndpoints.cs`) :

```csharp
var api = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(versions).MapToApiVersion(1, 0)
    .RequireAuthorization("customer-or-staff");
customers.MapPost("", async (ProfileBody body, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new CreateProfile(body.FirstName, body.LastName, body.Email, body.Phone), ct);
    return TypedResults.Created($"/api/v1/customers/{result.Id}", result);
});
```

L'authentification (`Api/Program.cs`) valide le JWT Keycloak puis convertit `realm_access.roles` en rôles .NET :

```csharp
options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
options.MapInboundClaims = false;
// OnTokenValidated : refus sans "sub", puis
identity.AddClaim(new Claim(ClaimTypes.Role, name));   // pour chaque rôle de realm_access.roles
```

Ordre du pipeline, repris par tous les services (Warehouse, Delivery, Billing, Notification et Reporting insèrent en plus `app.UseParcelFlowNoStore()` après les ProblemDetails) :

```csharp
app.UseParcelFlowCorrelation();
app.UseParcelFlowProblemDetails();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
```

Le handler métier ne traite que ses exceptions (`CustomerExceptionHandler.cs`) et laisse le reste au socle :

```csharp
CustomerNotFoundException => (404, "Resource not found"),
CustomerConflictException => (409, "Resource conflict"),
CustomerForbiddenException => (403, "Access denied"),
```

## Étape 6 — Exploitation : santé, métriques, migrations

- `/health/live` (processus) et `/health/ready` (PostgreSQL, Redis, bus) : la distinction servira aux probes Kubernetes (chapitre 11).
- `/metrics` : OpenTelemetry au format Prometheus ; l'export OTLP s'active avec `OpenTelemetry__Endpoint`.
- Migrations au démarrage **uniquement en Development** et sur demande (`Database:Initialize`) ; en production, migrations explicites (`dotnet ef database update`, puis bundles au chapitre 11).

## Étape 7 — Tests : unitaires, contrats, intégration réelle

Trois projets par service :

| Projet | Contenu |
| --- | --- |
| `ParcelFlow.Customer.UnitTests` | Règles du domaine, validateurs |
| `ParcelFlow.Customer.ContractTests` | Forme JSON des événements `Customer.V1` |
| `ParcelFlow.Customer.IntegrationTests` | API HTTP, JWT signés, PostgreSQL/RabbitMQ/Redis réels |

L'infrastructure de test démarre de vrais conteneurs (`InfrastructureFixture.cs`) :

```csharp
public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine").Build();
public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
    .WithUsername("rabbitmq").WithPassword("rabbitmq").Build();
public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();
```

L'API est hébergée par `WebApplicationFactory<Program>` (d'où `public partial class Program;` à la fin de `Program.cs`), avec les chaînes de connexion des conteneurs injectées en configuration.

> **Pourquoi pas de base en mémoire ?** `xmin`, les contraintes uniques, les transactions de l'Outbox et le comportement de RabbitMQ ne sont pas simulables fidèlement. Les tests échouent si Docker est absent : aucun test d'intégration n'est ignoré silencieusement.

## Vérifier

```powershell
dotnet build ParcelFlow.sln --configuration Release
dotnet test ParcelFlow.sln --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/Services/Customer/ParcelFlow.Customer.Infrastructure
dotnet list ParcelFlow.sln package --vulnerable --include-transitive
```

À la livraison de l'étape : 48 tests verts (10 BuildingBlocks, 15 unitaires, 2 contrats, 21 intégrations), aucune dérive de modèle EF, aucun package vulnérable.

Référence : [docs/customer-service.md](../customer-service.md).

Chapitre suivant : [Shipment et la saga](03-shipment-et-saga.md).
