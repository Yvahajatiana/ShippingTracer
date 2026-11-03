# Chapitre 7 — API Gateway avec YARP

## Objectif

Donner aux clients un **point d'entrée unique** vers les huit services : routage par préfixe, validation du JWT au bord, quotas, CORS et erreurs homogènes. La gateway n'a ni base ni messagerie ; elle ne contient aucune règle métier et ne modifie aucun service.

## Étape 1 — Le projet

```powershell
dotnet new web -o src/Gateway/ParcelFlow.Gateway
dotnet sln add src/Gateway/ParcelFlow.Gateway
dotnet add src/Gateway/ParcelFlow.Gateway package Yarp.ReverseProxy
```

Fichiers : `Program.cs` (pipeline et routes), `GatewayOptions.cs` (configuration validée), `GatewayForwarding.cs` (transfert et nettoyage des en-têtes).

## Étape 2 — Une audience par service

Chaque service a sa propre audience Keycloak (`customer-api`, `shipment-api`…). La gateway en garde la table (`GatewayOptions.cs`) :

```csharp
public static IReadOnlyDictionary<string, string> Services { get; } = new Dictionary<string, string>
{
    ["Customer"] = "customer-api", ["Shipment"] = "shipment-api", ["Tracking"] = "tracking-api",
    ["Warehouse"] = "warehouse-api", ["Delivery"] = "delivery-api", ["Billing"] = "billing-api",
    ["Notification"] = "notification-api", ["Reporting"] = "reporting-api"
};
```

Le JWT est validé contre toutes ces audiences, puis une **politique par service** exige l'audience de la route ciblée :

```csharp
options.TokenValidationParameters.ValidAudiences = GatewayRoutes.Services.Values;
// ...
foreach (var audience in GatewayRoutes.Services.Values)
    options.AddPolicy($"audience:{audience}", policy => policy.RequireAuthenticatedUser().RequireClaim("aud", audience));
```

> **Décision :** pas d'audience supplémentaire `gateway-api`. L'imposer aurait obligé à modifier la validation de huit services déjà validés. Le jeton est transmis tel quel : les services gardent le contrôle des rôles et de la propriété. Un jeton destiné uniquement à Customer n'ouvre pas Reporting.

## Étape 3 — Routage par transfert direct

Plutôt qu'une configuration de routes YARP déclarative, la gateway utilise `IHttpForwarder` (transfert direct) derrière des endpoints ASP.NET Core classiques, ce qui permet d'y attacher autorisation et quotas comme sur n'importe quel endpoint :

```csharp
Map("Customer", "/api/v1/customers/{**remainder}");
Map("Shipment", "/api/v1/shipments/{**remainder}");
// ... un Map par préfixe
app.MapGet("/api/v1/tracking/{trackingNumber}", (HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker client) =>
    GatewayForwarding.SendAsync(context, forwarder, client, gateway.Destinations["Tracking"], gateway))
    .AllowAnonymous().RequireRateLimiting("public");

void Map(string service, string pattern) => app.Map(pattern,
    (HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker client) =>
        GatewayForwarding.SendAsync(context, forwarder, client, gateway.Destinations[service], gateway))
    .RequireAuthorization($"audience:{GatewayRoutes.Services[service]}").RequireRateLimiting("private");
```

- Les chemins ne sont pas réécrits : `/api/v1/...` arrive tel quel au service.
- Seul le suivi public à **un** segment est anonyme ; `/tracking/shipments/{id}` reste protégé.
- `/health`, `/metrics` et `/swagger` des services ne sont **pas** routés.
- Les destinations sont des paramètres d'exploitation (`Gateway__Destinations__<Service>`), jamais dérivés de la requête.

## Étape 4 — Quotas, CORS, en-têtes

Trois niveaux de limitation (fenêtre fixe, `Program.cs`) :

```csharp
options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    context.Request.Path.StartsWithSegments("/api")
        ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(gateway.IpPermitLimit))
        : RateLimitPartition.GetNoLimiter("operational"));
options.AddPolicy("public", /* par adresse : PublicPermitLimit */);
options.AddPolicy("private", context => context.User.Identity?.IsAuthenticated == true
    ? RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("sub")!.Value, _ => Window(gateway.PrivatePermitLimit))
    : RateLimitPartition.GetNoLimiter("anonymous"));
```

Un dépassement renvoie 429 avec `Retry-After` en ProblemDetails.

- **En-têtes de transfert non fiables supprimés** : un `X-Forwarded-For` fourni par le client ne définit ni l'identité ni la clé de quota.
- **CORS explicite**, sans joker ni cookie : le Bearer voyage dans `Authorization`. `Location`, `Content-Disposition` et `Retry-After` sont exposés.
- **Corps limité à 12 MiB** (compatible avec les preuves de livraison), timeout d'inactivité amont 30 s, aucune mutation rejouée.
- `Cache-Control: no-store` sur toutes les réponses `/api`.

## Étape 5 — Readiness indépendante des services

```csharp
// Readiness means valid loaded configuration and a running gateway, independently of downstream availability.
app.MapHealthChecks("/health/ready");
```

> **Pourquoi ?** Si la readiness dépendait des huit services, la panne d'un seul retirerait la gateway entière du trafic. Une indisponibilité amont n'affecte que les routes concernées (502/503).

## Étape 6 — Tester sans Docker ni Keycloak

Les tests (`tests/Gateway/ParcelFlow.Gateway.Tests`) démarrent la gateway avec de **vrais serveurs HTTP amont factices** et des JWT signés validés cryptographiquement : frontières de routage et d'audience, suivi public, CORS, quotas, pannes.

Le smoke test `tests/Gateway/kestrel-smoke.mjs` complète sous Kestrel réel ce que `TestServer` ne reproduit pas : corps chunked malformé (400), corps trop lent (408), limite de taille (413), intégrité d'un flux binaire et propagation de l'annulation.

## Décisions et compromis

| Choix | Compromis |
| --- | --- |
| Quotas en mémoire par instance | Non distribués entre réplicas ; une politique d'edge complète au chapitre 11 |
| Pas de confiance dans `X-Forwarded-For` | Derrière un proxy, toutes les IP se confondent : les limites par IP sont relevées en Kubernetes |
| Pas d'agrégation ni de BFF | La gateway reste un proxy sans logique métier |

## Vérifier

```powershell
dotnet test tests/Gateway/ParcelFlow.Gateway.Tests --configuration Release
dotnet build src/Gateway/ParcelFlow.Gateway --configuration Debug
node tests/Gateway/kestrel-smoke.mjs
```

Le smoke test utilise des ports éphémères et arrête ses processus à la fin.

Référence : [docs/gateway.md](../gateway.md).

Chapitre suivant : [Interlude : première CI, audit et durcissement](08-audit-et-durcissement.md).
