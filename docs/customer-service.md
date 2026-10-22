# Customer Service — étape 2

## Périmètre et décisions produit

Le service conserve les coordonnées logistiques. Keycloak reste propriétaire des identifiants de connexion, mots de passe et rôles ; aucune copie de mot de passe ni administration parallèle d'identité n'est ajoutée.

Un profil correspond à un sujet Keycloak `sub` unique et immuable. Il peut avoir plusieurs adresses et appartenir à zéro ou une entreprise. L'affectation à une entreprise est administrative : un client ne peut pas se rattacher seul à un compte professionnel. Les inscriptions créent le profil de l'utilisateur connecté. La suppression de profils et d'entreprises n'est pas exposée : les futurs envois et factures devront pouvoir conserver leurs références.

| Donnée | Champs obligatoires | Champs facultatifs |
|---|---|---|
| Profil | Prénom (100), nom (100), email de contact (254) | Téléphone (30), entreprise affectée par admin |
| Adresse | Libellé (80), ligne 1 (200), code postal (20), ville (100), pays ISO 3166-1 alpha-2 en majuscules | Ligne 2 (200) |
| Entreprise | Nom (200), immatriculation (80), email (254) | Aucun |

Les nombres indiquent les longueurs maximales. Les chaînes obligatoires ne peuvent pas être blanches. Les chaînes facultatives absentes sont `null`. Les coordonnées sont normalisées par suppression des espaces en début et fin ; l'email est validé après cette normalisation, donc `" marie@example.test "` est accepté et stocké sans espaces. L'immatriculation est normalisée en majuscules et unique ; elle reste un identifiant international générique, sans contrainte nationale de type SIRET. Les identifiants des ressources sont des GUID générés par le serveur. Les dates sont UTC.

| Opération | customer | operator | admin |
|---|---|---|---|
| Créer son profil | Oui | Oui | Oui |
| Lire son profil, modifier ses coordonnées | Oui | Oui | Oui |
| Lire les profils tiers et la liste paginée | Non | Oui | Oui |
| Modifier les coordonnées d'un tiers | Non | Non | Oui |
| Ajouter/modifier/supprimer une adresse | Propre profil | Tous profils | Tous profils |
| Consulter les entreprises | Non | Oui | Oui |
| Créer/modifier une entreprise, affecter un client | Non | Non | Oui |

Les contrôles existent dans les handlers et dans les politiques HTTP. Les tokens sans sujet `sub`, de signature inconnue, expirés, avec mauvais émetteur ou mauvaise audience sont refusés. Les rôles proviennent de `realm_access.roles` dans un JWT Keycloak validé.

## API v1

| Méthode | Route | Résultat |
|---|---|---|
| POST | `/api/v1/customers` | Création de son profil, 201 |
| GET | `/api/v1/customers/me` | Profil de l'utilisateur connecté |
| GET | `/api/v1/customers?offset=0&limit=20` | Liste staff, limite comprise entre 1 et 100 |
| GET, PUT | `/api/v1/customers/{id}` | Lecture ou modification des coordonnées |
| PUT | `/api/v1/customers/{id}/company` | Affectation ; `companyId: null` détache l'entreprise |
| POST | `/api/v1/customers/{id}/addresses` | Création d'une adresse, 201 |
| PUT, DELETE | `/api/v1/customers/{id}/addresses/{addressId}` | Modification ou suppression, suppression 204 |
| GET, POST | `/api/v1/companies` | Liste ou création d'entreprise |
| GET, PUT | `/api/v1/companies/{id}` | Lecture ou modification d'entreprise |
| GET | `/health/live`, `/health/ready` | Vie du processus et disponibilité des dépendances |
| GET | `/metrics` | Métriques OpenTelemetry au format Prometheus |

Les erreurs utilisent ProblemDetails avec `traceId` : 400 validation/requête incorrecte (y compris une règle du domaine, par exemple un sujet `sub` de plus de 200 caractères), 401 token absent/invalide, 403 accès interdit, 404 ressource absente, 409 doublon ou concurrence, 503 avec `Retry-After` si le circuit PostgreSQL est ouvert. Toutes les erreurs portent `Cache-Control: no-store`. Les détails techniques inattendus restent dans les logs, avec une réponse 500 générique. Swagger est disponible à `/swagger` en Development.

## Persistance et événements

Les tables `customers`, `addresses`, `companies` résident uniquement dans PostgreSQL `parcelflow_customers`. Les migrations sont dans Infrastructure/Persistence/Migrations. Les contraintes uniques portent sur le sujet Keycloak et l'immatriculation d'entreprise ; la référence à l'entreprise est une clé étrangère. `xmin` protège les mises à jour concurrentes des trois entités.

Chaque commande métier publie son événement via l'Outbox EF Core MassTransit : les données et le message sont enregistrés dans la même transaction. Un échec de la transaction ne laisse aucun événement publié. Le worker Outbox livre les messages à RabbitMQ avec reprise après indisponibilité du broker. La livraison est au moins une fois ; les futurs consommateurs devront utiliser l'inbox MassTransit pour la déduplication. Customer ne consomme encore aucun événement et n'ajoute pas de consommateur artificiel.

Contrats dans `ParcelFlow.BuildingBlocks.Contracts.Customer.V1` : `CustomerProfileCreated`, `CustomerProfileUpdated`, `CompanyCreated`, `CompanyUpdated`. Chaque message contient les métadonnées communes et l'identifiant concerné ; la création de profil contient aussi le sujet Keycloak. Aucun email, téléphone ou contenu d'adresse n'est diffusé.

Redis met en cache uniquement la liste des entreprises pendant 60 secondes. Les écritures invalident cette entrée après validation de la transaction. Une panne Redis laisse la lecture fonctionner via PostgreSQL. Un lecteur concurrent à l'invalidation peut observer l'ancienne liste jusqu'à expiration du cache. Les profils clients ne sont pas mis en cache.

Polly protège les lectures PostgreSQL avec deux nouvelles tentatives transitoires et un circuit breaker. Les écritures ne sont pas rejouées automatiquement : un doublon ou une modification concurrente retourne 409. Serilog journalise les requêtes ; OpenTelemetry instrumente HTTP et expose les métriques Prometheus. L'export OTLP peut être raccordé à un récepteur configuré ; il n'est pas nécessaire au fonctionnement métier. Les health checks couvrent PostgreSQL, Redis et le bus MassTransit.

## Configuration et exécution hors Docker Compose

Prérequis : SDK .NET 8, PostgreSQL, RabbitMQ, Redis et un realm Keycloak `parcelflow`. Le déploiement commun, son realm importable et Docker Compose seront livrés à l'étape Docker. Les tests d'intégration démarrent leurs propres PostgreSQL/RabbitMQ/Redis et vérifient les JWT sans serveur Keycloak externe.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5101'
$env:ConnectionStrings__CustomerDatabase = Read-Host 'Connexion PostgreSQL Customer, mot de passe inclus'
$env:ConnectionStrings__Redis = Read-Host 'Connexion Redis'
$env:RabbitMq__Host = Read-Host 'Hôte RabbitMQ'
$env:RabbitMq__Username = Read-Host 'Utilisateur RabbitMQ'
$env:RabbitMq__Password = Read-Host 'Mot de passe RabbitMQ'
$env:Database__Initialize = 'true'
$env:Database__SeedDemoData = 'true'
dotnet run --project src/Services/Customer/ParcelFlow.Customer.Api
```

`Keycloak__Authority` et `Keycloak__Audience` peuvent remplacer les valeurs de développement. Le client Keycloak doit produire l'audience `customer-api`, les rôles du realm et un sujet stable. En dehors de Development, les métadonnées OIDC doivent être servies en HTTPS. Les secrets sont fournis par environnement ; aucun mot de passe de service n'est versionné.

`Database:Initialize` n'est pris en compte au démarrage qu'en Development. En déploiement, appliquer explicitement les migrations avant de démarrer les instances :

```powershell
dotnet tool restore
dotnet ef database update --project src/Services/Customer/ParcelFlow.Customer.Infrastructure
dotnet run --project src/Services/Customer/ParcelFlow.Customer.Api
```

La factory EF lit `ConnectionStrings__CustomerDatabase`. Le seed est une initialisation locale explicite et répétable, jamais un compte activé automatiquement en production. Il crée l'entreprise fictive Brume Logistique et deux clients fictifs : Emma Martin à Lyon et Lucas Bernard à Bruxelles. Les sujets Keycloak réservés pour les futurs comptes de démonstration sont `10000000-0000-0000-0000-000000000001` et `10000000-0000-0000-0000-000000000002`. Les identifiants de connexion seront livrés avec le realm à l'étape Docker. Le seed n'émet pas de notifications métier.

## Vérification

```powershell
dotnet build ParcelFlow.sln --configuration Release
dotnet test ParcelFlow.sln --configuration Release --no-build
```

Docker doit être actif pour Testcontainers. Aucun test d'intégration n'est ignoré si Docker est absent. Les tests couvrent les règles du domaine, les validations, les contrats JSON, l'autorisation HTTP, de vrais JWT signés, les contraintes PostgreSQL, la concurrence, les transactions Outbox et le seed répétable.

L'architecture reste séparée en Domain, Application, Infrastructure et Api. Cette étape n'ajoute ni gateway, ni frontend, ni Dockerfile, ni configuration Kubernetes/CI. Ces livraisons suivent les validations prévues.

## Résultat de validation de l'étape

Compilation Release sous SDK 8.0.426 : zéro avertissement et zéro erreur. Tests réussis : 10 BuildingBlocks, 15 unitaires Customer, 2 contrats, 21 intégrations, soit 48 tests, sans test ignoré. Aucun changement de modèle EF en attente de migration. Le contrôle NuGet incluant les dépendances transitives ne signale plus de package vulnérable connu après mise à jour de Testcontainers en 4.16.0.

L'adaptateur Prometheus officiel est épinglé en version `1.15.3-beta.1`, compatible avec les composants OpenTelemetry 1.15.3. Cette préversion concerne l'exporteur Prometheus ; les projets restent en .NET 8 / C# 12. Les précédentes dépendances de tests exposaient les avis [SSH.NET GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284) et [GHSA-mggc-4xg6-vcxf](https://github.com/advisories/GHSA-mggc-4xg6-vcxf), retirés avec la mise à jour de Testcontainers.
