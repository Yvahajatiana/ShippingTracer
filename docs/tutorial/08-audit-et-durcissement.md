# Chapitre 8 — Interlude : première CI, audit et durcissement

## Objectif

Une fois les huit services et la gateway fusionnés, et **avant** d'attaquer les frontends, le projet a fait une pause pour :

1. relire l'ensemble de façon critique et transformer chaque constat en **ticket GitHub** ;
2. corriger un secret présent dans l'historique git ;
3. installer une **première CI** qui empêche les régressions ;
4. corriger les défauts transverses par des PR `fix/...` ciblées.

Cette étape n'apparaît pas dans le plan initial, mais elle est caractéristique d'un projet réel : les défauts d'intégration n'apparaissent qu'une fois toutes les briques en place.

## Étape 1 — L'audit transformé en tickets

Les constats ont été ouverts en issues (#11 à #27), étiquetées par service (`svc:tracking`, `svc:shipment`…) et par nature (`bug`, `security`, `performance`, `tests`, `documentation`). Les étapes restantes du plan sont aussi devenues des tickets `roadmap` (#28 à #31). Extraits :

| Ticket | Constat |
| --- | --- |
| #11 | Identifiants SQL Server d'un ancien projet présents dans l'historique git |
| #13 | La saga ignore sans trace un paiement au montant ou à la devise incorrects |
| #14 | EventId non repris comme MessageId, CorrelationId non propagée |
| #15 | Cache Redis de Tracking invalidé avant le commit |
| #16 | Une tournée peut démarrer pour un colis encore en stock Warehouse |
| #21, #22 | Exceptions de domaine et circuit ouvert renvoyés en 500 ; `no-store` absent des ProblemDetails |

> **Pourquoi des tickets plutôt que corriger au fil de l'eau ?** Chaque correction touche un composant déjà validé. Le ticket documente le constat, la PR documente la correction et ses tests : la règle « ne pas modifier un composant validé sans demande explicite » reste traçable.

## Étape 2 — Le secret dans l'historique (#11)

Un commit ancien contenait une chaîne de connexion avec mot de passe. Traitement : rotation du mot de passe, réécriture de l'historique (`git filter-repo --replace-text`, valeurs remplacées par `***REMOVED***`) et force-push de toutes les branches. Ensuite, la CI a été conçue pour qu'une telle fuite ne puisse plus être fusionnée.

Les règles par défaut de gitleaks ne détectaient pas ce format. `.gitleaks.toml` ajoute donc une règle dédiée :

```toml
[extend]
useDefault = true

[[rules]]
id = "connection-string-password"
regex = '''(?i)\b(?:password|pwd)\s*=\s*([^;"'\s]{4,})'''
secretGroup = 1
keywords = ["password", "pwd"]

[allowlist]
regexes = [ '''^local-password$''', '''^\*\*\*REMOVED\*\*\*$''', '''^\$\([A-Z][A-Z0-9_]*\)$''' ]
```

La règle a été validée **dans les deux sens** : aucune alerte sur l'historique nettoyé, détection des trois fichiers fuités sur une sauvegarde de l'ancien historique.

## Étape 3 — La première CI (#32)

Premier `.github/workflows/ci.yml` (branche `feat/ci-cd`), exécuté sur chaque PR et chaque push vers `develop`/`main` :

| Job | Contrôle |
| --- | --- |
| Secret scan | gitleaks sur tout l'historique |
| Build, unit and contract tests | Build Release (avertissements = erreurs), tests hors intégration, dérive des modèles EF (`has-pending-model-changes`) sur les 8 services, packages NuGet vulnérables |
| Integration tests (matrice) | Un job par service, Testcontainers sur le runner |
| Gateway Kestrel smoke test | `node tests/Gateway/kestrel-smoke.mjs` |

Plus `.github/dependabot.yml` (NuGet et actions, cible `develop`). Images et déploiement sont volontairement reportés aux étapes Docker et Kubernetes (chapitre 12).

## Étape 4 — Les corrections transverses

Chaque correction a sa branche, sa PR, ses tests et une mise à jour de la documentation du service :

| PR | Branche | Correction |
| --- | --- | --- |
| #36 | `fix/tracking-cache-invalidation` | Invalidation Redis par intercepteur de transaction, après commit (chapitre 4) |
| #37 | `fix/warehouse-exit-on-delivery` | Sortie Warehouse automatique au départ en livraison, ADR 0001 (chapitre 4) |
| #38 | `fix/shipment-payment-saga` | Journalisation des paiements incohérents et délai de paiement (chapitre 3) |
| #40 | `fix/api-error-mapping` | `ApiExceptionMapping` commun : 400 pour les gardes, 503 + `Retry-After` pour un circuit ouvert, `no-store` |
| #41 | `fix/message-identity-correlation` | Projet `BuildingBlocks.Messaging`, `MessageId = EventId`, corrélation propagée, EventId déterministes |

### Focus sur #41 : une seule identité de message

Avant la correction, l'Inbox MassTransit dédupliquait sur `MessageId` (aléatoire) alors que les consommateurs dédupliquaient sur `EventId`. Une redélivrance avec un nouvel identifiant de transport passait l'Inbox. Le filtre de `BuildingBlocks.Messaging/MessageIdentityFilters.cs` aligne les deux :

```csharp
public static void Apply<T>(SendContext<T> context) where T : class
{
    if (context.Message is not IntegrationEvent integrationEvent) return;
    if (integrationEvent.EventId != Guid.Empty) context.MessageId = integrationEvent.EventId;
    if (integrationEvent.CorrelationId != Guid.Empty) context.CorrelationId = integrationEvent.CorrelationId;
}
```

Chaque service l'active par une ligne, avant `ConfigureEndpoints` :

```csharp
rabbit.UseParcelFlowMessageConventions(context);
rabbit.ConfigureEndpoints(context);
```

Pour qu'une opération rejouée publie **le même** EventId, les identifiants sont dérivés de façon déterministe (`DeterministicGuid.Create("shipment:payment-requested:", shipmentId)`). Le préfixe `service:fait:` évite qu'un même `OperationId` client produise la même clé dans deux contextes. La table complète est dans `docs/building-blocks.md`.

## Ce qui est resté ouvert

Certains tickets ont été laissés ouverts car ils dépassent le périmètre ou demandent un arbitrage produit, par exemple : ETag/If-Match côté Customer (#24), rétention et chiffrement des preuves (#17), appels HTTP de Notification dans la transaction (#19), paiement traité après expiration (#39). Ils restent visibles dans le suivi GitHub.

## Vérifier

```powershell
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.21.2 git /repo --config /repo/.gitleaks.toml
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.12
dotnet test ParcelFlow.sln --configuration Release
```

Référence : [docs/ci-cd.md](../ci-cd.md), [docs/building-blocks.md](../building-blocks.md#identité-des-messages-et-corrélation-14).

Chapitre suivant : [Frontends](09-frontends.md).
