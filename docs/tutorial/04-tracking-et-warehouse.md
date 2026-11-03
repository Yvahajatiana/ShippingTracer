# Chapitre 4 — Tracking et Warehouse : projections et scans

## Objectif

Ajouter les deux premiers services **consommateurs** d'événements :

- **Tracking** construit l'historique d'un colis à partir des faits publiés par les autres services, et expose un suivi public anonyme.
- **Warehouse** gère sites et stock, et produit les faits physiques (prise en charge, transit, centre de tri).

Ce chapitre introduit les patterns qui reviennent dans tous les services suivants : consommateurs idempotents avec Inbox, verrous consultatifs PostgreSQL, identifiants fournis par le client pour l'idempotence, et invalidation de cache après commit.

## Partie A — Tracking

### Étape 1 — Des consommateurs fins, une commande MediatR

Chaque consommateur traduit le contrat en commande applicative (`Infrastructure/Consumers/TrackingConsumers.cs`) :

```csharp
public sealed class ShipmentCreatedConsumer(ISender sender) : IConsumer<ShipmentCreated>
{
    public async Task Consume(ConsumeContext<ShipmentCreated> context)
    {
        var message = context.Message;
        await sender.Send(new RecordTrackingEvent(message.EventId, message.ShipmentId, message.TrackingNumber,
            ParcelStatus.Created, message.OccurredAtUtc, null), context.CancellationToken);
    }
}
```

La logique reste dans l'Application et se teste sans RabbitMQ. Les files sont préfixées par service :

```csharp
bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("tracking", false));
```

### Étape 2 — Ordre des faits et idempotence

Les messages arrivent dans n'importe quel ordre : un `ParcelPickedUp` peut précéder `ShipmentCreated`. Le domaine trie les faits par date, puis par un rang sémantique déterministe (Created < PickedUp < … < Delivered < Returned), puis par EventId. `Delivered` reste prioritaire sauf sur `Returned`, qui est terminal.

La clé d'un fait est son `EventId`, unique globalement en base : un rejeu identique ne modifie rien et ne republie rien.

### Étape 3 — Sérialiser par colis avec un verrou consultatif

`Infrastructure/Persistence/TrackingStore.cs` :

```csharp
if (database.Database.CurrentTransaction is not null)
{
    await database.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({shipmentId.ToString()}, 0))", cancellationToken);
    // PostgreSQL aborts the whole transaction on error, so an in-transaction retry cannot succeed:
    // fail fast and let the MassTransit message retry replay the consumer in a fresh transaction.
    return await LoadByShipmentAsync(shipmentId, cancellationToken);
}
```

> **Pourquoi un verrou consultatif ?** Trois files peuvent traiter simultanément des faits du même colis, y compris avant que sa ligne existe. Le verrou est pris sur l'identifiant, même absent, ce qui évite d'épuiser les retries optimistes. Les autres colis restent parallèles. Le verrou est libéré automatiquement au commit. Ce motif est repris dans Warehouse, Delivery, Billing, Notification et Reporting.

> **Pourquoi pas Polly dans la transaction ?** Après une erreur, PostgreSQL annule la transaction : retenter dedans est inutile. C'est le retry MassTransit qui rejoue le consommateur dans une transaction neuve.

### Étape 4 — Cache public invalidé après le commit

Le suivi public est mis en cache Redis 15 s. Version initiale : invalidation au `SaveChanges`. L'audit (#15, PR #36) a montré qu'une lecture concurrente pouvait remettre en cache l'ancienne projection **entre la sauvegarde et le commit**. Correction : un intercepteur de transaction EF Core (`Persistence/TrackingCacheInvalidator.cs`) :

```csharp
public sealed class TrackingCacheInvalidator(IDistributedCache cache, ILogger<TrackingCacheInvalidator> logger)
    : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // The commit is durable at this point: never let the caller's token or Redis abort the flush.
        var numbers = Take(eventData.Context);
        if (numbers.Length > 0) await InvalidateAsync(numbers, CancellationToken.None);
    }
    // TransactionRolledBackAsync: drop the pending keys without touching Redis
}
```

La réponse publique ne contient aucune donnée personnelle (ni nom, ni adresse, ni identifiant interne). La consultation interne (`/tracking/shipments/{id}`) exige le rôle operator ou admin.

## Partie B — Warehouse

### Étape 5 — Le modèle

Un site a un code unique immuable (qui sert de `FacilityCode` dans Tracking) et un type `Warehouse` ou `SortingCenter`. Un colis occupe au plus un site ; un transfert est une sortie suivie d'une entrée. Le colis devient éligible aux scans à la réception de `ShipmentReadyForPickup` (paiement accepté).

### Étape 6 — Idempotence par identifiant client

Le `ScanId` est un UUID **fourni par le client** :

```json
{ "scanId": "52000000-0000-0000-0000-000000000001", "warehouseId": "50000000-0000-0000-0000-000000000001",
  "trackingNumber": "PF500000000000000000000001", "direction": "Entry" }
```

- Même `ScanId` + même contenu + même opérateur → le reçu initial est renvoyé.
- Même `ScanId` + contenu différent → 409.

> **Pourquoi ?** Un scanner qui perd la réponse réseau peut renvoyer la requête sans risque de double mouvement. Le même principe (`OperationId`) est appliqué aux livraisons, paiements et avoirs.

### Étape 7 — Les faits publiés

| Scan | Événements publiés |
| --- | --- |
| Première entrée en entrepôt | `ParcelPickedUp` (EventId = ScanId) : termine la saga Shipment |
| Entrée en centre de tri | `ParcelStatusRecorded` AtSortingCenter (+ `ParcelPickedUp` dérivé si première entrée) |
| Sortie, ou entrée ultérieure | `ParcelStatusRecorded` InTransit |

Les verrous sont pris dans un ordre fixe (colis puis site) pour éviter les interblocages ; un site contenant des colis ne peut pas être désactivé.

### Étape 8 — Sortie automatique au départ en livraison (ADR 0001)

Ajouté après le service Delivery (#16, PR #37) : une tournée pouvait démarrer avec un colis encore en stock. Plutôt que de coupler Delivery au stock, Warehouse réagit à l'événement que Delivery publie déjà (`Consumers/ParcelDeliveryStartedConsumer.cs`) :

```csharp
if (message.Status is not (TrackingStatus.OutForDelivery or TrackingStatus.Delivered or TrackingStatus.Failed)) return;
var outcome = await sender.Send(new RecordDeliveryExit(message.EventId, message.ShipmentId,
    message.TrackingNumber, message.OccurredAtUtc), context.CancellationToken);
```

Le scan automatique porte le sujet `system:delivery-start` et un `ScanId` dérivé de l'EventId source (UUID v8) : une redélivrance ne crée jamais une seconde sortie. Il ne publie rien, pour ne pas faire régresser l'historique public. Les alternatives écartées (appel synchrone, nouveau contrat, projection du stock dans Delivery) sont argumentées dans l'ADR.

## Vérifier

```powershell
dotnet test ParcelFlow.sln --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Tracking/ParcelFlow.Tracking.Infrastructure --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Warehouse/ParcelFlow.Warehouse.Infrastructure --configuration Release
```

Points couverts par les intégrations : prise en charge avant création, 22 faits concurrents sur trois files avec une publication par fait, invalidation du cache après commit et pas après rollback, 20 scans concurrents, et un **workflow sur trois bases séparées** : ShipmentCreated → paiement → projection Warehouse → prise en charge → sortie → tri → saga terminée et historique Tracking.

Référence : [docs/tracking-service.md](../tracking-service.md), [docs/warehouse-service.md](../warehouse-service.md), [ADR 0001](../adr/0001-warehouse-exit-on-delivery-start.md).

Chapitre suivant : [Delivery et Billing](05-delivery-et-billing.md).
