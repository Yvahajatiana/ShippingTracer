# ParcelFlow — arborescence du monorepo

Le monorepo regroupe les BuildingBlocks, les huit services, la gateway, les trois portails Angular, les déploiements Docker et Kubernetes/Helm ainsi que la CI/CD. L'arborescence ci-dessous en donne une vue simplifiée. Aucun projet vide n'est ajouté à la solution.

Prérequis de développement local : SDK .NET 8.0.4xx. `global.json` fixe la version 8.0.400 avec `rollForward: latestPatch` : seul un SDK de la bande 8.0.4xx est accepté.

```text
ParcelFlow/
├── ParcelFlow.sln
├── global.json
├── Directory.Build.props
├── .editorconfig
├── .gitignore
├── .gitattributes
├── src/
│   ├── BuildingBlocks/
│   │   ├── ParcelFlow.BuildingBlocks.Contracts/
│   │   │   ├── ParcelFlow.BuildingBlocks.Contracts.csproj
│   │   │   └── IntegrationEvent.cs
│   │   ├── ParcelFlow.BuildingBlocks.Application/
│   │   │   ├── ParcelFlow.BuildingBlocks.Application.csproj
│   │   │   └── Validation/ValidationBehavior.cs
│   │   └── ParcelFlow.BuildingBlocks.Api/
│   │       ├── ParcelFlow.BuildingBlocks.Api.csproj
│   │       └── Errors/
│   │           ├── ApiExceptionHandler.cs
│   │           ├── ApiExceptionMapping.cs
│   │           └── ProblemDetailsExtensions.cs
│   ├── Services/
│   │   ├── Customer/
│   │   ├── Shipment/
│   │   ├── Tracking/
│   │   ├── Warehouse/
│   │   ├── Delivery/
│   │   ├── Billing/
│   │   ├── Notification/
│   │   └── Reporting/
│   └── Gateway/ParcelFlow.Gateway/
├── frontends/
│   ├── customer-portal/
│   ├── operations-portal/
│   └── admin-portal/
├── tests/
│   ├── BuildingBlocks/ParcelFlow.BuildingBlocks.Tests/
│   │   ├── ParcelFlow.BuildingBlocks.Tests.csproj
│   │   ├── ValidationBehaviorTests.cs
│   │   └── ApiExceptionHandlerTests.cs
│   └── Services/
├── deploy/
│   ├── keycloak/
│   ├── observability/
│   ├── kubernetes/
│   └── helm/parcelflow/
├── .github/workflows/
├── docker-compose.yml
├── docs/
│   ├── monorepo.md
│   └── building-blocks.md
└── README.md
```

## Convention des services

Chaque dossier `src/Services/<Service>/` contient quatre projets :

```text
ParcelFlow.<Service>.Domain/
ParcelFlow.<Service>.Application/
ParcelFlow.<Service>.Infrastructure/
ParcelFlow.<Service>.Api/
    Dockerfile
```

Domain reste indépendant. Application référence Domain et BuildingBlocks.Application. Infrastructure référence Application, Domain et BuildingBlocks.Contracts. Api référence Application, Infrastructure et BuildingBlocks.Api. Aucun service ne référence le projet d'un autre service ; les échanges asynchrones utilisent BuildingBlocks.Contracts. Les migrations EF Core appartiennent à Infrastructure/Persistence/Migrations. Les tests propres au service résident dans `tests/Services/<Service>/` avec des projets `.UnitTests`, `.IntegrationTests` et `.ContractTests`.

Les contrats métier versionnés sont placés dans `BuildingBlocks.Contracts/<Service>/V1/`. Customer désigne les profils, adresses et entreprises ; Keycloak conserve la responsabilité d'authentification.

Les huit services ont été implémentés dans l'ordre indiqué ci-dessus, puis gateway, frontends, Docker, Kubernetes/Helm, CI/CD et README, chaque étape étant soumise à validation.
