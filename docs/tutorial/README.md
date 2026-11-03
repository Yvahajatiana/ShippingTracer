# Tutoriel — construire ParcelFlow pas à pas

Ce tutoriel retrace la construction de ParcelFlow, une plateforme d'expédition de colis en microservices .NET 8 avec trois portails Angular, dans l'ordre réel du développement. Il ne remplace pas la documentation de référence (`docs/*.md`) : il explique **pourquoi** chaque brique existe, **comment** elle a été posée et **comment la vérifier**, avec des extraits tirés du dépôt.

## Public visé

Un développeur .NET / Angular qui veut comprendre, puis reproduire, la construction d'une plateforme de ce type : services autonomes avec leur base, messagerie fiable (Outbox/Inbox), saga, passerelle d'API, portails SPA authentifiés par Keycloak, conteneurs, Helm et pipeline CI/CD.

## Ce que vous allez construire

```text
Navigateur ──► portails Angular (nginx) ──► Gateway YARP ──► 8 services ASP.NET Core
                     │                         │                 │   │
                     └──── Keycloak (OIDC, PKCE) ◄─ JWT ─────────┘   ├─ PostgreSQL (1 base / service)
                                                                     ├─ Redis (cache)
                                                                     └─ RabbitMQ (MassTransit, Outbox/Inbox, saga)
```

| Domaine | Service | Rôle |
| --- | --- | --- |
| Clients | Customer | Profils, adresses, entreprises |
| Envois | Shipment | Devis, création, étiquette PDF, saga de paiement |
| Suivi | Tracking | Historique et suivi public |
| Entrepôts | Warehouse | Sites, scans entrée/sortie, stock |
| Livraison | Delivery | Tournées, livreurs, preuves |
| Facturation | Billing | Factures, paiements simulés, avoirs |
| Notifications | Notification | Emails/SMS simulés |
| Pilotage | Reporting | KPIs agrégés |

## Prérequis

- SDK .NET 8, bande `8.0.4xx` (imposée par `global.json`), outil `dotnet-ef` (manifeste `.config/dotnet-tools.json`, `dotnet tool restore`).
- Docker (obligatoire pour les tests d'intégration Testcontainers et la plateforme Compose).
- Node.js 22+ et npm pour `frontends/`.
- Pour le chapitre Kubernetes : minikube, kubectl, Helm 3.
- Connaissances : ASP.NET Core minimal APIs, EF Core, notions de DDD/CQRS, Angular standalone.

## Chapitres

| # | Chapitre | Branche / PR d'origine |
| --- | --- | --- |
| 1 | [Socle du monorepo et BuildingBlocks](01-socle-et-building-blocks.md) | `feat/building-blocks` (#1) |
| 2 | [Premier service : Customer, le gabarit](02-premier-service-customer.md) | `feat/customer-service` (#2) |
| 3 | [Shipment : tarification, étiquette et saga](03-shipment-et-saga.md) | `feat/shipment-service` (#3) |
| 4 | [Tracking et Warehouse : projections et scans](04-tracking-et-warehouse.md) | #4, #5 |
| 5 | [Delivery et Billing : tournées, preuves, paiements](05-delivery-et-billing.md) | #6, #7 |
| 6 | [Notification et Reporting : consommateurs en bout de chaîne](06-notification-et-reporting.md) | #8, #9 |
| 7 | [API Gateway avec YARP](07-api-gateway.md) | `feat/api-gateway` (#10) |
| 8 | [Interlude : première CI, audit et durcissement](08-audit-et-durcissement.md) | #32, #36 à #41 |
| 9 | [Frontends : trois portails Angular](09-frontends.md) | `feat/frontend-portals` (#43, #44) |
| 10 | [Docker et plateforme locale](10-docker.md) | `feat/docker` (#45) |
| 11 | [Kubernetes avec Helm](11-kubernetes-helm.md) | `feat/kubernetes` (#46, #47) |
| 12 | [CI/CD complète et déploiement continu](12-ci-cd.md) | `feat/ci-cd-deploy` (#53) |

## Méthode de travail suivie

La construction a respecté quelques règles qui expliquent la forme du dépôt :

- **Une branche par bloc fonctionnel** (`feat/<bloc>`, `fix/<sujet>`), jamais de développement direct sur `develop` ou `main`. `develop` est la branche d'intégration ; `main` reçoit les promotions par PR (#33).
- **Toute fusion passe par une pull request**, avec résumé, résultats des vérifications et review automatisée (bot Codex). Les remarques pertinentes sont corrigées et poussées avant fusion ; les suggestions écartées sont justifiées dans le fil.
- **Une étape à la fois, validée avant la suivante** : BuildingBlocks, puis chaque service séparément, gateway, frontends, Docker, Kubernetes, CI/CD et README. Un composant validé n'est plus modifié sans raison explicite (les corrections passent par des tickets).
- **Tickets GitHub pour les constats d'audit** (issues #11 et suivantes), corrigés par des PR `fix/...` dédiées (chapitre 8).
- **Décisions documentées** : chaque service a sa page dans `docs/`, et les décisions transverses ont un ADR (`docs/adr/`).
- **Travail parallèle cadré** : pour chaque service, les interfaces étaient fixées d'abord, puis trois lots indépendants (Domain/Application, Infrastructure/migrations, API/tests HTTP) étaient développés en parallèle ; migrations, compilations et intégration finale étaient séquencées.

## Comment suivre ce tutoriel

1. Lisez le chapitre, puis ouvrez les fichiers cités : les chemins sont relatifs à la racine du dépôt.
2. Pour voir l'état du code à une étape donnée, utilisez l'historique de `develop` :

   ```powershell
   git log --first-parent --oneline develop
   git show --stat 0caa8b0   # fusion de feat/customer-service (#2)
   ```

3. Exécutez la section « Vérifier » de chaque chapitre. La commande commune est :

   ```powershell
   dotnet restore ParcelFlow.sln
   dotnet build ParcelFlow.sln --configuration Release --no-restore
   dotnet test ParcelFlow.sln --configuration Release --no-build
   ```

4. Pour l'ensemble de la plateforme, passez directement au chapitre 10 (`docker compose up --build -d`).

## Documentation de référence

[Monorepo](../monorepo.md) · [BuildingBlocks](../building-blocks.md) · [Customer](../customer-service.md) · [Shipment](../shipment-service.md) · [Tracking](../tracking-service.md) · [Warehouse](../warehouse-service.md) · [Delivery](../delivery-service.md) · [Billing](../billing-service.md) · [Notification](../notification-service.md) · [Reporting](../reporting-service.md) · [Gateway](../gateway.md) · [Frontends](../frontends.md) · [Docker](../docker.md) · [Kubernetes](../kubernetes.md) · [CI/CD](../ci-cd.md) · [ADR 0001](../adr/0001-warehouse-exit-on-delivery-start.md)
