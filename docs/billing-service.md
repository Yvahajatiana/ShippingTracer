# Billing Service

Sixième service métier ParcelFlow : factures, paiements simulés et avoirs. Quatre projets .NET 8/C# 12 suivent les couches Domain, Application, Infrastructure et Api, avec MediatR, FluentValidation et les ProblemDetails du socle.

## Factures et paiements

Shipment calcule le montant de l’envoi. Sa saga envoie PaymentRequested à la file durable exacte `billing-payments`. Billing crée alors une facture PendingPayment, sans débiter automatiquement le client. Une facture correspond à un seul envoi : InvoiceId est ShipmentId. Sa référence technique stable est INV- suivie de l’identifiant sans tirets en majuscules.

Le propriétaire, le montant et la devise EUR sont immuables. Un rejeu compatible de PaymentRequested ne change ni la facture, ni son paiement, ni ses avoirs ; une réutilisation contradictoire est rejetée. Les montants sont positifs, bornés à 1 000 000 EUR et ne comportent pas de fraction de centime. Les valeurs sont persistées en decimal PostgreSQL, précision18/échelle2.

Le client choisit explicitement un résultat de simulation Approved ou Declined. Le paiement utilise toujours le montant intégral et la devise de la facture : aucun montant ni numéro de carte n’est attendu dans la requête. Approved publie PaymentCompleted ; Declined publie PaymentFailed avec un motif fixe sans donnée personnelle.

Un seul paiement terminal est accepté par facture. Un rejeu exact restitue le reçu initial, même après émission d’avoirs ; une nouvelle opération ou un résultat différent retourne 409. Le refus est terminal pour rester cohérent avec la saga Shipment existante, dont l’état PaymentRejected ignore les succès ultérieurs. Un nouveau paiement après refus exige donc un nouvel envoi. Un reçu de paiement simulé refusé retourne HTTP200 avec Outcome=Declined : ce résultat métier n’est pas une panne HTTP.

Les références sont techniques et ces documents ne modélisent pas une numérotation fiscale, des taxes ou une réglementation comptable particulière. Aucun prestataire financier, carte, virement ou calcul de TVA n’est ajouté.

## Avoirs

Seul un administrateur peut émettre un avoir sur une facture payée. Les montants peuvent être partiels et leur somme ne dépasse jamais le montant payé. Les états évoluent de Paid vers PartiallyCredited puis Credited quand le solde créditable est nul. Une facture PendingPayment ou PaymentFailed ne peut pas être créditée.

Chaque avoir conserve son identité, sa référence CRN- suivie de l’identifiant, son montant, sa date, son acteur et son motif BillingCorrection ou ServiceIssue. L’historique reste accessible au propriétaire et à l’administrateur. CreditNoteIssued publie les identifiants, le propriétaire technique, le montant et la devise ; il n’expose pas l’acteur ni le motif interne.

Un avoir est une écriture dans ce registre simulé. Il ne déclenche pas de transfert bancaire, ne remet pas la facture en attente de paiement et ne révoque pas la prise en charge du colis dans Shipment ou Warehouse.

OperationId est un UUID fourni par le client. Son unicité et son idempotence sont vérifiées séparément pour les paiements et les avoirs. Un rejeu doit conserver la facture, le contenu et l’acteur initial ; une réutilisation contradictoire retourne 409. Les paiements et avoirs ont des tables distinctes. Les dates serveur sont en UTC, à la microseconde, strictement croissantes par facture grâce à LastActionAtUtc, y compris si l’horloge recule.

## Accès et API v1

Préfixe : `/api/v1/billing`. JWT Keycloak, audience billing-api. Le rôle customer consulte et paie ses propres factures ; admin consulte toutes les factures et peut simuler un paiement au nom d’un client. Les opérateurs sans rôle customer/admin n’accèdent pas aux données financières. Le sujet de l’acteur vient exclusivement du JWT sub ; un champ propriétaire ou acteur fourni par le client ne modifie pas le périmètre d’accès.

| Méthode et route relative | Effet |
| --- | --- |
| GET `/invoices` | Liste paginée des factures, filtrée par propriétaire sauf admin |
| GET `/invoices/{invoiceId}` | Détail, état et solde créditable |
| GET `/invoices/{invoiceId}/payment` | Reçu du paiement terminal, 404 s’il est absent |
| POST `/invoices/{invoiceId}/payments` | Simulation explicite Approved ou Declined |
| GET `/invoices/{invoiceId}/credit-notes` | Historique paginé des avoirs |
| GET `/invoices/{invoiceId}/credit-notes/{creditNoteId}` | Détail d’un avoir de cette facture |
| POST `/invoices/{invoiceId}/credit-notes` | admin uniquement : émission d’un avoir, 201 avec Location |

La facture n’est pas créable ni modifiable par HTTP. Les lectures métier utilisent directement PostgreSQL, avec Cache-Control no-store et sans cache Redis. Les listes acceptent page=1..1000000 et pageSize=1..100, par défaut 1 et 50. Elles sont lues en ReadCommitted ; total et éléments peuvent refléter des instants proches sous écritures concurrentes.

Exemple de paiement simulé :

```json
{ "operationId": "72000000-0000-0000-0000-000000000001", "outcome": "Approved" }
```

Exemple d’avoir :

```json
{ "operationId": "73000000-0000-0000-0000-000000000001", "amount": 2.00, "reason": "ServiceIssue" }
```

OperationId et les champs de décision Outcome, Amount et Reason sont explicitement obligatoires dans leurs corps respectifs. Une omission ne sélectionne pas silencieusement le premier résultat d’énumération. Swagger est disponible sur `/swagger` en Development, avec des énumérations textuelles et les champs requis correspondant au JSON réel.

Les erreurs utilisent les ProblemDetails communs : 400 pour une validation, 401/403 pour l’authentification ou l’autorisation, 404 pour une ressource absente et 409 pour un conflit de paiement, d’identité ou de solde. Les corps HTTP sont limités à 1MiB, avec 413 au-delà. Aucune adresse, donnée de carte ou pièce jointe n’est manipulée par Billing.

## Transactions et événements

La base dédiée parcelflow_billing contient les factures, les paiements, les avoirs et les tables Inbox/Outbox MassTransit. Les paiements ont une contrainte d’unicité sur InvoiceId et tous les documents enfants possèdent une clé étrangère obligatoire restrictive. La facture utilise xmin et Revision pour la concurrence.

Chaque commande s’exécute dans une transaction ReadCommitted et prend un verrou PostgreSQL sur l’identifiant de facture avant lecture, y compris si la ligne n’existe pas encore. Les consommateurs réutilisent la transaction de l’Outbox ; le pipeline des écritures HTTP englobe l’état de facture, les reçus et les messages Outbox. Ce verrou sérialise les paiements et avoirs d’une même facture, sans empêcher les autres factures de progresser. Une erreur annule l’écriture métier et la publication ensemble.

Le consommateur PaymentRequested est lié à `billing-payments`, sans modification du nom par le préfixe des endpoints. La saga peut donc déposer son message avant le premier démarrage de Billing. Le bus attend la connexion au démarrage ; Inbox/Outbox assure les redélivrances et la publication après commit. Les retries sont bornés, puis les messages en erreur restent dans le mécanisme fault/error MassTransit.

Contrats versionnés dans BuildingBlocks :

- PaymentRequested, PaymentCompleted et PaymentFailed restent compatibles avec Shipment.
- InvoiceIssued publie la création d’une nouvelle facture, une seule fois par identité métier, avec un EventId dérivé de InvoiceId et la CorrelationId du PaymentRequested consommé.
- CreditNoteIssued publie chaque nouvel avoir : CreditNoteId est OperationId, tandis qu’EventId en est dérivé de façon déterministe (`billing:credit-note-issued:`) pour distinguer ce fait d’un paiement qui utiliserait la même clé dans son propre périmètre.

Tous les faits ont CorrelationId=ShipmentId, corrélation racine du flux d’envoi (propagée depuis le message consommé pour InvoiceIssued) ; MessageId est égal à EventId (voir [BuildingBlocks](building-blocks.md#identité-des-messages-et-corrélation-14)). Aucun événement ne contient l’acteur ou un motif libre provenant du client. Polly applique retry et circuit breaker aux lectures PostgreSQL transitoires. Redis reste configuré pour le socle et la readiness, sans cache de soldes.

## Lancement à cette étape

Prérequis : .NET 8, PostgreSQL, RabbitMQ et Redis accessibles. Fournir les secrets par variables d’environnement ; Docker et le provisionnement Keycloak seront livrés aux étapes prévues.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5106'
$env:ConnectionStrings__BillingDatabase = 'Host=localhost;Port=5432;Database=parcelflow_billing;Username=parcelflow;Password=local-password'
$env:ConnectionStrings__Redis = 'localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000'
$env:RabbitMq__Host = 'localhost'
$env:RabbitMq__Username = 'parcelflow'
$env:RabbitMq__Password = 'local-password'
$env:Database__Initialize = 'true'
$env:Database__SeedDemoData = 'true'
dotnet run --project src/Services/Billing/ParcelFlow.Billing.Api
```

L’application applique les migrations au démarrage en Development uniquement si Database:Initialize=true. Hors Development :

```powershell
dotnet tool restore
dotnet ef database update --project src/Services/Billing/ParcelFlow.Billing.Infrastructure
```

Le seed optionnel et idempotent crée deux factures PendingPayment, sans paiement, avoir ni événement fictif : IDs 71000000-0000-0000-0000-000000000001/2, propriétaires 10000000-0000-0000-0000-000000000001/2, montants 12.90/18.50EUR, date 2026-10-18. Ces données sont indépendantes d’une saga réelle ; le workflow de test utilise des envois créés pour son propre parcours.

Configurer Keycloak__Authority et Keycloak__Audience=billing-api. Les métadonnées exigent HTTPS hors Development. `/health/live` vérifie le processus ; `/health/ready` vérifie PostgreSQL, Redis et MassTransit. Serilog écrit sur la console ; OpenTelemetry instrumente HTTP et MassTransit, `/metrics` expose Prometheus et OpenTelemetry__Endpoint active l’export OTLP.

## Vérification et branches

Trois agents ont possédé des lots indépendants après fixation des interfaces : Domain/Application/unitaires, Infrastructure/migrations/intégrations et API/tests HTTP. Les compilations et migrations ont été séquencées. Le parent a fourni les contrats, leurs tests, la documentation et un workflow sur quatre bases séparées : saga Shipment réelle → file durable → facture en attente → paiement approuvé → prise en charge Warehouse et suivi Tracking ; un second envoi teste le refus terminal. Les avoirs partiels puis complets et le rejeu du paiement ne changent pas la prise en charge.

```powershell
dotnet test ParcelFlow.sln --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Services/Billing/ParcelFlow.Billing.Infrastructure --configuration Release
dotnet list ParcelFlow.sln package --vulnerable --include-transitive
```

La branche main a été créée à partir de l’état Delivery validé et définie comme branche GitHub par défaut pour les releases. Develop reste la branche d’intégration ; Billing est développé sur feat/billing-service et intégré à develop par PR après tests et review Codex. Une promotion vers main doit également passer par une PR ; aucune release ni étiquette n’est publiée par cette étape.

Résultats vérifiés : compilation Release du monorepo sans avertissement ni erreur ; 437 tests réussis, aucun échec ni test ignoré. Billing compte 27 tests unitaires, 46 intégrations et 4 contrats, en complément des 360 tests existants. Le modèle EF correspond à la migration et NuGet ne signale aucun package vulnérable connu, dépendances transitives incluses. La queue durable avant démarrage, les paiements/avoirs concurrents, les rollbacks SQL, l’isolation des clients, les dates à la microseconde et la saga sur quatre bases sont vérifiés.
