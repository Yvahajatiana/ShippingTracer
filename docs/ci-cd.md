# CI/CD

Le workflow `.github/workflows/ci.yml` s'exécute sur chaque pull request et chaque push vers `develop` et `main`, ainsi qu'à la demande (`workflow_dispatch`). Une nouvelle exécution sur la même PR annule la précédente. Le jeton GitHub est limité à la lecture du dépôt.

## Jobs

| Job | Rôle |
| --- | --- |
| Secret scan | gitleaks sur tout l'historique git avec `.gitleaks.toml` : règles par défaut plus une règle `connection-string-password` qui détecte les mots de passe dans les chaînes de connexion, ce que les règles par défaut ne font pas. Seuls le placeholder documenté `local-password` et les valeurs `***REMOVED***` de l'historique nettoyé sont autorisés. |
| Build, unit and contract tests | Restauration, compilation Release de la solution (warnings traités comme erreurs), puis tous les projets de test hors intégration : unitaires, contrats, BuildingBlocks et Gateway. Contrôle de dérive des modèles EF Core pour les 8 services (`has-pending-model-changes`) et échec si NuGet signale un package vulnérable, dépendances transitives incluses. |
| Integration tests (matrice) | Un job par service. Les tests Testcontainers démarrent PostgreSQL, RabbitMQ et Redis réels sur le runner. Les services s'exécutent en parallèle, sans arrêt au premier échec. |
| Gateway Kestrel smoke test | Compile la gateway en Debug et exécute `tests/Gateway/kestrel-smoke.mjs` sous Node 20 : corps chunked malformé, corps lent, limite de taille, flux binaire et annulation. |

Les résultats TRX sont publiés en artefacts, y compris en cas d'échec. Le SDK installé est le dernier 8.0.4xx, la bande imposée par `global.json`.

## Dépendances

`.github/dependabot.yml` propose chaque semaine, sur `develop`, les mises à jour NuGet (groupées : Microsoft, MassTransit, tests) et celles des actions GitHub.

## Étapes suivantes

La construction et la publication des images sur GHCR, le scan des images et le déploiement seront ajoutés aux étapes Docker et Kubernetes, puisque les Dockerfiles n'existent pas encore.

## Recommandations pour le dépôt

- Protéger `main` et `develop` : PR obligatoire et jobs du workflow requis.
- Activer Secret scanning et Push protection (Settings → Code security).

## Vérifications locales

```powershell
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.21.2 git /repo --config /repo/.gitleaks.toml
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.7
```
