# Chapitre 5 — Delivery et Billing : tournées, preuves, paiements

## Objectif

Compléter le cycle de vie du colis :

- **Delivery** planifie des tournées, affecte des livreurs et enregistre les preuves de livraison.
- **Billing** répond à la commande `PaymentRequested` de la saga Shipment : facture, paiement simulé, avoirs.

Ces deux services ajoutent un pattern important : la **transaction de commande dans le pipeline MediatR**, qui englobe règles métier, écritures et messages Outbox.

## Partie A — Delivery

### Étape 1 — Le modèle

Une tournée suit `Planned → InProgress → Completed`. Un colis devient disponible quand **deux** faits sont connus, dans n'importe quel ordre : `ShipmentReadyForPickup` (numéro de suivi) et `ParcelPickedUp` (prise en charge physique). Le livreur est un utilisateur Keycloak de rôle `operator` : aucun rôle supplémentaire n'est créé.

### Étape 2 — Une transaction par commande

`Infrastructure/DeliveryTransactionBehavior.cs` est un comportement MediatR appliqué aux commandes (`IDeliveryCommand`) :

```csharp
if (request is not IDeliveryCommand || database.Database.CurrentTransaction is not null)
    return await next();
await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
try
{
    var result = await next();
    await transaction.CommitAsync(cancellationToken);
    return result;
}
catch (DbUpdateConcurrencyException)
{
    throw new DeliveryConflictException("Delivery state changed concurrently. Retry the operation.");
}
catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
{
    throw new DeliveryConflictException("The shipment identity, route stop or operation identifier already exists.");
}
```

> **Pourquoi `CurrentTransaction is not null` ?** Dans un consommateur, l'Outbox MassTransit a déjà ouvert la transaction : le comportement la réutilise. Une requête HTTP obtient la sienne. Le même code de handler fonctionne dans les deux contextes, et les erreurs de concurrence ou d'unicité deviennent des 409 métier.

Verrous dans un ordre fixe : tournée → livreur → colis (triés par identifiant), pour exclure les interblocages.

### Étape 3 — Preuves de livraison sans bibliothèque d'image

La confirmation exige le nom du destinataire et au moins une preuve (signature PNG ≤ 2 MiB, photo PNG/JPEG ≤ 5 MiB), envoyées en base64 et stockées en `bytea`. `Application/ProofImages.cs` valide la **structure** des fichiers sans décoder les pixels :

```csharp
// Checks bounded image containers; compressed pixels are never decoded by this service.
public static bool IsValidSignature(byte[]? content) => content is null ||
    content.Length <= MaxSignatureBytes && IsPng(content);
public static bool IsValidPhoto(byte[]? content) => content is null ||
    content.Length <= MaxPhotoBytes && (IsPng(content) || IsJpeg(content));
```

Signature PNG, chunks et CRC, marqueurs JPEG, dimensions 1 à 8192 px. Au téléchargement, le type MIME est fixé par le serveur, avec `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff` et `no-store`.

> **Confidentialité :** seuls le livreur affecté et les administrateurs voient les preuves. Les événements sortants ne transportent ni destinataire, ni sujet d'opérateur, ni image. Les motifs d'échec sont une énumération fermée (`RecipientAbsent`, `AddressNotFound`, `RecipientRefused`) : aucun texte libre ne peut fuiter.

### Étape 4 — Événements publiés

Au démarrage d'une tournée : `ParcelStatusRecorded` OutForDelivery par arrêt ; à l'issue : Delivered ou Failed. Aucun nouveau contrat n'est nécessaire, le contrat Tracking existant suffit. Les EventId sont déterministes (`delivery:out-for-delivery:` + tournée + envoi, `delivery:delivered:` + envoi + OperationId).

## Partie B — Billing

### Étape 5 — Consommer la commande de la saga sur sa file exacte

`Infrastructure/DependencyInjection.cs` :

```csharp
bus.AddConsumer<PaymentRequestedConsumer>().Endpoint(endpoint => endpoint.Name = "billing-payments");
```

Le nom n'est pas préfixé par le formateur d'endpoints : il doit correspondre exactement à `queue:billing-payments` utilisé par la saga (chapitre 3). Un test vérifie qu'un message déposé **avant** le premier démarrage de Billing est bien traité.

### Étape 6 — Un domaine financier strict

`Domain/Entities.cs` :

```csharp
public static bool IsAmount(decimal value) => value > 0 && value <= 1_000_000m && decimal.Round(value, 2) == value;

public Invoice(Guid shipmentId, string customerSubject, decimal amount, string currency, DateTimeOffset issuedAtUtc)
{
    BillingRules.RequireId(shipmentId); BillingRules.RequireSubject(customerSubject); BillingRules.RequireMoney(amount, currency);
    Id = shipmentId; /* ... */
}
public string Number => "INV-" + Id.ToString("N").ToUpperInvariant();
```

- **InvoiceId = ShipmentId** : une facture par envoi, et un `PaymentRequested` rejoué retrouve la même facture.
- Le paiement est **simulé** : le client choisit `Approved` ou `Declined`, toujours pour le montant intégral. Aucun numéro de carte ni montant n'est accepté dans la requête.
- Un refus est **terminal**, par cohérence avec l'état `PaymentRejected` de la saga.
- Les avoirs (admin uniquement) sont partiels ou complets, jamais au-delà du montant payé : `Paid → PartiallyCredited → Credited`.

### Étape 7 — Verrou sur la facture, même absente

`Persistence/BillingStore.cs` refuse toute mutation hors transaction et verrouille la facture avant de la lire :

```csharp
if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Billing mutations require a transaction.");
var key = "billing:invoice:" + id;
await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
```

Paiements et avoirs d'une même facture sont sérialisés, les autres factures progressent en parallèle. Billing utilise le même comportement transactionnel que Delivery (`BillingTransactionBehavior`), comme Warehouse, Notification et Reporting, enregistré par `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(...TransactionBehavior<,>))`.

## Organisation et branches

Même découpage en trois lots que les services précédents. Lors de l'étape Billing, la branche `main` a été créée à partir de l'état validé et définie comme branche par défaut ; `develop` reste la branche d'intégration et les promotions vers `main` passent aussi par PR.

## Décisions et compromis

| Choix | Compromis accepté |
| --- | --- |
| Images stockées en `bytea` | Simple et transactionnel ; rétention et chiffrement RGPD restent à traiter (issue #17) |
| Delivery ne consulte pas le stock Warehouse | Le stock devient cohérent à terme via l'ADR 0001 |
| Paiement simulé, pas de prestataire | Le flux de saga est complet et testable ; un PSP réel remplacerait le handler de paiement |

## Vérifier

```powershell
dotnet test ParcelFlow.sln --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Delivery/ParcelFlow.Delivery.Infrastructure --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Billing/ParcelFlow.Billing.Infrastructure --configuration Release
```

Les workflows d'intégration traversent plusieurs bases réelles : pour Delivery, prise en charge → première tournée en échec → replanification → livraison avec preuve → suivi public « Livré » ; pour Billing, **saga Shipment réelle → file durable → facture → paiement approuvé → prise en charge Warehouse et suivi Tracking**, plus un second envoi pour le refus terminal.

Référence : [docs/delivery-service.md](../delivery-service.md), [docs/billing-service.md](../billing-service.md).

Chapitre suivant : [Notification et Reporting](06-notification-et-reporting.md).
