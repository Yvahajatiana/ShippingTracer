# CI/CD

Le workflow `.github/workflows/ci.yml` s'exécute sur chaque pull request et chaque push vers `develop` et `main`, ainsi qu'à la demande (`workflow_dispatch`). Une nouvelle exécution sur la même PR annule la précédente. Le jeton GitHub est limité à la lecture du dépôt, sauf pour le job des images qui reçoit en plus `packages: write` (publication GHCR) et `security-events: write` (rapports Trivy).

## Jobs

| Job | Rôle |
| --- | --- |
| Secret scan | gitleaks sur tout l'historique git avec `.gitleaks.toml` : règles par défaut plus une règle `connection-string-password` qui détecte les mots de passe dans les chaînes de connexion, ce que les règles par défaut ne font pas. Seuls le placeholder documenté `local-password` et les valeurs `***REMOVED***` de l'historique nettoyé sont autorisés. |
| Build, unit and contract tests | Restauration, compilation Release de la solution (warnings traités comme erreurs), puis tous les projets de test hors intégration : unitaires, contrats, BuildingBlocks et Gateway. Contrôle de dérive des modèles EF Core pour les 8 services (`has-pending-model-changes`) et échec si NuGet signale un package vulnérable, dépendances transitives incluses. |
| Integration tests (matrice) | Un job par service. Les tests Testcontainers démarrent PostgreSQL, RabbitMQ et Redis réels sur le runner. Les services s'exécutent en parallèle, sans arrêt au premier échec. |
| Gateway Kestrel smoke test | Compile la gateway en Debug et exécute `tests/Gateway/kestrel-smoke.mjs` sous Node 22 : corps chunked malformé, corps lent, limite de taille, flux binaire et annulation. |
| Dockerfile lint and compose validation | hadolint sur tous les `Dockerfile` avec `.hadolint.yaml` (règles ignorées justifiées dans le fichier), puis `docker compose config --quiet` avec des secrets factices. |
| Image (matrice) | Une entrée par image : 9 images .NET (gateway et 8 services) et 3 portails. Build Buildx avec cache GitHub Actions par image, image chargée localement puis scannée par Trivy : vulnérabilités OS et bibliothèques `HIGH`/`CRITICAL` corrigeables, échec du job au moindre résultat. Hors PR, le rapport SARIF est publié dans Code scanning. |

Les résultats TRX sont publiés en artefacts, y compris en cas d'échec. Le SDK installé est le dernier 8.0.4xx, la bande imposée par `global.json`.

Les images ne démarrent qu'après le secret scan, le build .NET, les frontends et le lint Docker. Les tests d'intégration et le smoke test s'exécutent en parallèle ; la protection de branche les impose avant la fusion.

## Images GHCR

Nom : `ghcr.io/<owner en minuscules>/parcelflow-<image>`, par exemple `ghcr.io/yvahajatiana/parcelflow-gateway` ou `ghcr.io/yvahajatiana/parcelflow-admin-portal`.

| Tag | Quand |
| --- | --- |
| `sha-<sha court>` | Chaque build |
| `develop`, `main` | Push sur la branche |
| `latest` | Push sur `main` uniquement |
| `pr-<numéro>` | PR, image locale jamais publiée |

La publication (`docker push` de l'image scannée) n'a lieu que sur un push vers `main` ou `develop` du dépôt d'origine : jamais sur une PR ni depuis un fork. Les build args `VERSION` (branche ou PR) et `REVISION` (commit) alimentent les labels OCI. Pas de tag semver : le workflow ne se déclenche pas sur les tags git.

## Dépendances

`.github/dependabot.yml` propose chaque semaine, sur `develop`, les mises à jour NuGet (groupées : Microsoft, MassTransit, tests) et celles des actions GitHub.

## Étapes suivantes

Le déploiement des images publiées sera ajouté à l'étape Kubernetes.

## Recommandations pour le dépôt

- Protéger `main` et `develop` : PR obligatoire et jobs du workflow requis.
- Activer Secret scanning et Push protection (Settings → Code security).

## Vérifications locales

```powershell
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.21.2 git /repo --config /repo/.gitleaks.toml
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.12
Get-Content src/Gateway/ParcelFlow.Gateway/Dockerfile | docker run --rm -i -v "${PWD}/.hadolint.yaml:/.hadolint.yaml" hadolint/hadolint:v2.15.1 hadolint --config /.hadolint.yaml -
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.70.0 image --scanners vuln --severity CRITICAL,HIGH --ignore-unfixed --exit-code 1 parcelflow/gateway:local
```
