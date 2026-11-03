# Chapitre 6 — Notification et Reporting : consommateurs en bout de chaîne

## Objectif

Terminer les huit services avec deux consommateurs « en aval » qui n'émettent aucune commande vers les autres :

- **Notification** transforme les faits métier en emails et SMS **simulés** (reçus `MockSent` en base).
- **Reporting** maintient une projection pour calculer des KPIs (volumes, taux de livraison, délais).

Ce chapitre montre deux sujets nouveaux : l'**appel de service à service authentifié** (client credentials Keycloak) et la **projection analytique** lue avec des agrégations SQL.

## Partie A — Notification

### Étape 1 — Quels faits, quels messages

| Fait reçu | Notification |
| --- | --- |
| ShipmentCreated | Création de l'envoi |
| TrackingStatusChanged | Statut observé (sauf Created) |
| InvoiceIssued, PaymentCompleted, PaymentFailed, CreditNoteIssued | Facture, paiement accepté/refusé, avoir |

Chaque fait produit deux reçus (Email, SMS). Les textes sont bornés (200 / 2 000 / 320 caractères) et le motif technique d'un refus de paiement n'est jamais recopié au client.

### Étape 2 — Résoudre les coordonnées sans les diffuser dans les événements

Les contrats ne contiennent volontairement **aucune** coordonnée. Notification lit donc l'instantané de l'expéditeur auprès de Shipment (`GET /api/v1/shipments/{id}`) avec un compte de service. `Infrastructure/Recipients/ShipmentAccessToken.cs` obtient le jeton :

```csharp
request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret
});
// ...
_token = payload.AccessToken;
_expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, payload.ExpiresIn - 30));
```

`ShipmentRecipientResolver.cs` consulte d'abord Redis (5 min, budget 250 ms), puis appelle Shipment ; un 401 invalide le jeton et déclenche **un seul** renouvellement :

```csharp
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    await accessToken.InvalidateAsync(token, cancellationToken);
    if (authenticationAttempt == 0) continue;
    throw new HttpRequestException("Shipment contact resolution failed.", null, HttpStatusCode.Unauthorized);
}
```

> **Décisions :**
> - Le client Keycloak `notification-service` a le rôle `operator` et l'audience `shipment-api` : il passe les contrôles existants de Shipment, **sans modifier ce service validé**.
> - Le propriétaire et le numéro de suivi présents dans l'événement sont comparés à la source Shipment avant tout enregistrement.
> - HTTPS obligatoire hors Development pour Keycloak et Shipment ; timeouts de 5 s, Polly pour les erreurs réseau, 429 et 5xx, circuit breaker partagé.
> - Ni jeton OAuth ni résultat négatif dans Redis.

### Étape 3 — Idempotence

La clé d'un fait est son `EventId`, verrouillée (`pg_advisory_xact_lock`) avant lecture. La contrainte unique `(SourceEventId, Channel)` garantit un reçu par canal. L'événement, ses deux reçus et les deux `NotificationRecorded` sont écrits dans la même transaction. Un rejeu ne rappelle pas Shipment et ne republie rien.

Limite connue, tracée par l'issue #19 : la résolution HTTP a lieu pendant la transaction PostgreSQL du consommateur.

## Partie B — Reporting

### Étape 4 — Une seule source : TrackingStatusChanged

Reporting ne consomme qu'un contrat, `TrackingStatusChanged` v1, et **reproduit les règles d'ordre de Tracking** (Created tardif sans effet, Delivered prioritaire sauf Returned). Les faits sont identifiés par `SourceEventId` ; une nouvelle enveloppe pour un fait connu est mémorisée (`event_envelopes`, migration `RememberEventEnvelopes`) mais n'ajoute rien.

> **Pourquoi pas de jointure avec les autres bases ?** Chaque service possède sa base. La projection asynchrone accepte un léger retard (délai de messagerie + 30 s de cache) en échange d'une isolation totale.

### Étape 5 — Agréger en SQL, de façon cohérente

`Persistence/ReportingStore.cs` :

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
var cohort = database.Shipments.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end);
var summary = await cohort.GroupBy(_ => 1).Select(group => new
{
    Total = group.LongCount(), Delivered = group.LongCount(x => x.CurrentStatus == ParcelStatus.Delivered),
    // ...
}).SingleOrDefaultAsync(ct);
```

- **RepeatableRead + lecture seule** : résumé, volumes quotidiens et répartition par statut proviennent du même instantané.
- Les agrégations s'exécutent dans PostgreSQL : aucune cohorte n'est chargée en mémoire.
- Le rapport est mis en cache Redis 30 s ; la clé inclut la période et un hash de l'identité de la base. Une entrée incohérente, future ou trop ancienne est ignorée.

Définition des KPIs : la période est une **cohorte par date de création** (`from` inclus, `to` exclu). Le seed de démonstration donne, du 15 au 18 octobre 2026 : volume 3, taux 33,33 %, délai moyen de livraison 26 h et de transit 24 h.

## Décisions et compromis

| Choix | Compromis |
| --- | --- |
| Notifications simulées | Aucun message réel ne sort ; un fournisseur réel remplacerait l'étape d'envoi |
| Appel HTTP Notification → Shipment | Seule dépendance synchrone entre services ; isolée par cache, retries et circuit breaker |
| KPIs sur projection | Fraîcheur éventuelle, mais aucune requête inter-bases |

## Vérifier

```powershell
dotnet test tests/Services/Notification/ParcelFlow.Notification.IntegrationTests --configuration Release
dotnet test tests/Services/Reporting/ParcelFlow.Reporting.IntegrationTests --configuration Release
```

Le test transversal de Notification passe par la **vraie API Shipment protégée** (seul le serveur OAuth est simulé). Celui de Reporting passe par Tracking et son Outbox réelle, puis compare les KPIs au statut de l'API publique.

Référence : [docs/notification-service.md](../notification-service.md), [docs/reporting-service.md](../reporting-service.md).

Chapitre suivant : [API Gateway](07-api-gateway.md).
