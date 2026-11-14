# CI/CD

Le workflow `.github/workflows/ci.yml` s'exécute sur chaque pull request et chaque push vers `develop` et `main`, ainsi qu'à la demande (`workflow_dispatch`). Une nouvelle exécution sur la même PR annule la précédente. Le jeton GitHub est limité à la lecture du dépôt, sauf pour le job des images qui reçoit en plus `packages: write` (publication GHCR) et `security-events: write` (rapports Trivy).

## Jobs

| Job | Rôle |
| --- | --- |
| Changes | Décide de ce que font les autres jobs (voir [Exécution sélective](#exécution-sélective)). Sur une pull request, la liste des fichiers modifiés vient de l'API GitHub (`dorny/paths-filter`, épinglé par SHA) ; sur un push ou une exécution manuelle, tout est construit. |
| Secret scan | gitleaks sur tout l'historique git avec `.gitleaks.toml` : règles par défaut plus une règle `connection-string-password` qui détecte les mots de passe dans les chaînes de connexion, ce que les règles par défaut ne font pas. Seuls le placeholder documenté `local-password` et les valeurs `***REMOVED***` de l'historique nettoyé sont autorisés. |
| Build, unit and contract tests | Restauration, compilation Release de la solution (warnings traités comme erreurs), puis tous les projets de test hors intégration : unitaires, contrats, BuildingBlocks et Gateway. Contrôle de dérive des modèles EF Core pour les 8 services (`has-pending-model-changes`) et échec si NuGet signale un package vulnérable, dépendances transitives incluses. |
| Integration tests (matrice) | Deux jobs de quatre services, équilibrés sur la durée des tests : chaque job ne paie qu'une fois l'installation de .NET, la restauration et l'arrondi à la minute. Les tests Testcontainers démarrent PostgreSQL, RabbitMQ et Redis réels sur le runner. Dans un job, les services s'exécutent l'un après l'autre, sans arrêt au premier échec, avec un rapport TRX par service (artefacts `test-results-integration-1` et `-2`). |
| Gateway Kestrel smoke test | Compile la gateway en Debug et exécute `tests/Gateway/kestrel-smoke.mjs` sous Node 22 : corps chunked malformé, corps lent, limite de taille, flux binaire et annulation. |
| Frontends (lint, tests, build) | Dans `frontends/` sous Node 24 : `npm ci`, `npm audit --audit-level=high`, contrôle du formatage, lint, tests unitaires et builds de production des trois portails. |
| Dockerfile lint and compose validation | hadolint sur tous les `Dockerfile` avec `.hadolint.yaml` (règles ignorées justifiées dans le fichier), puis `docker compose config --quiet` avec des secrets factices. |
| Helm chart lint and schema validation | `helm lint --strict` puis `helm template` du chart `deploy/helm/parcelflow` sur quatre profils : local (`values-local.yaml` avec secrets factices), production (`values-production.example.yaml` avec Ingress), Gateway API (HTTPRoute) et Secret existant. Les manifestes sont validés par kubeconform (`-strict`) pour Kubernetes 1.27 (minimum de `Chart.yaml`) et 1.33 ; les CRD ExternalSecret et HTTPRoute utilisent les schémas du catalogue datreeio. |
| Trivy database | Télécharge une fois la base de vulnérabilités de Trivy dans le cache que `trivy-action` restaure dans chaque job d'image (même clé `cache-trivy-<date>`, même répertoire). Il tourne pendant le build ; un job d'image qui démarre avant lui télécharge la base lui-même. |
| Image (matrice) | Matrice générée par le job Changes : 9 images .NET (gateway et 8 services), 3 portails et l'image RabbitMQ, plus les 8 bundles de migration EF Core `<service>-migrations` (`deploy/migrations/Dockerfile`, build arg `SERVICE`) quand `KUBERNETES_DEPLOY` vaut `true`. Build Buildx avec cache GitHub Actions par image, image chargée localement puis scannée par Trivy : vulnérabilités OS et bibliothèques `HIGH`/`CRITICAL` corrigeables, échec du job au moindre résultat. Hors PR, le rapport SARIF est publié dans Code scanning ; sur un dépôt privé (Code scanning y exige GitHub Advanced Security), il est conservé en artefact `trivy-<image>` du workflow. |
| CI OK | Agrégateur toujours exécuté (`if: always()`). Il échoue si un job dont il dépend (tous sauf Trivy database, Deploy et Deploy public demo) a échoué ou a été annulé ; un job sauté par Changes est normal. C'est le seul check à rendre obligatoire. |

Les résultats TRX sont publiés en artefacts, y compris en cas d'échec. Le SDK installé est le dernier 8.0.4xx, la bande imposée par `global.json`.

Les images ne démarrent qu'après le secret scan, le build .NET, les frontends et le lint Docker. Les tests d'intégration et le smoke test s'exécutent en parallèle ; le check `CI OK` les impose avant la fusion.

## Exécution sélective

Le dépôt est privé : chaque job consomme des minutes de runner, arrondies à la minute supérieure. Le job `Changes` limite donc le travail des pull requests, sans rien retirer aux pushes.

| Événement | Jobs de code | Images |
| --- | --- | --- |
| Push sur `develop` ou `main`, exécution manuelle | Tous | Toutes, scannées par Trivy puis publiées sur un push |
| Pull request | Tous, sauf si elle ne modifie que de la documentation | Seulement celles dont une entrée a changé |

- **Documentation seule** : une PR qui ne modifie que `docs/**`, des fichiers `*.md` ou `.github/dependabot.yml` saute le build, les tests (intégration, smoke, e2e), les frontends, le lint Docker, Helm et les images. Seuls `Secret scan`, `Changes` et `CI OK` tournent. Le filtre est fait par des `if:` par job, et non par `paths-ignore` : un workflow non déclenché ne rapporterait jamais ses checks, et une PR qui les exige resterait bloquée.
- **Entrées d'une image** : ce que son Dockerfile copie, plus `.github/workflows/ci.yml`.
  - Image .NET (gateway, services, migrations) : `global.json`, `Directory.Build.props`, `.dockerignore`, `src/BuildingBlocks/**` et le répertoire du service (ou `src/Gateway/**`). Les migrations ajoutent `deploy/migrations/**`.
  - Portails : `frontends/**`, pour les trois.
  - RabbitMQ : `deploy/rabbitmq/**`.
  - Un `.csproj` ailleurs (par exemple un projet de test) n'entre dans aucune image : il ne déclenche que le build et les tests.
- **Images de migration** : les 8 images `<service>-migrations` ne servent qu'aux hooks Helm. Elles ne sont construites que si la variable de dépôt `KUBERNETES_DEPLOY` vaut `true` (Settings → Secrets and variables → Actions → Variables). La démo Dokploy n'en a pas besoin : elle migre au démarrage. Leur Dockerfile reste vérifié par hadolint dans tous les cas.
- **Protection de branche** : rendre obligatoire le seul check `CI OK`, et non les checks `Image (...)` ou `Integration tests (...)`. Une matrice réduite ou un job sauté ne rapporte pas ses checks, et une PR qui les exigerait resterait en attente indéfiniment.

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

## Déploiement continu

Le job `Deploy` de `ci.yml` appelle le workflow réutilisable `.github/workflows/deploy.yml` après un push sur `develop` (environnement `staging`) ou `main` (environnement `production`), une fois les images publiées et les tests d'intégration, le smoke test et le contrôle Helm réussis. Il exécute `helm upgrade --install` avec le tag de commit `sha-<sha court>` des images qui viennent d'être publiées :

- les jobs de migration (hooks Helm `pre-install` / `pre-upgrade`) appliquent les bundles `<service>-migrations` avant la mise à jour des workloads ;
- `--rollback-on-failure --wait --timeout 15m` : une mise à jour qui n'atteint pas l'état prêt revient à la révision précédente. Les migrations déjà appliquées ne sont pas annulées, elles doivent donc rester compatibles avec les images précédentes ;
- un seul déploiement à la fois par environnement, jamais annulé en cours ;
- la production n'accepte que des tags de commit immuables (`sha-xxxxxxx`), jamais une branche ni `latest`.

Le même workflow se lance à la main (Actions → Deploy → Run workflow) pour redéployer ou revenir à un tag de commit antérieur.

### Configuration des environnements

Settings → Environments, un environnement `staging` et un `production` :

| Nom | Type | Rôle |
| --- | --- | --- |
| `KUBECONFIG` | secret | kubeconfig d'un compte de déploiement limité au namespace de la release |
| `HELM_VALUES` | secret | fichier de values de l'environnement, sur le modèle de `deploy/kubernetes/values-production.example.yaml` (aucun secret applicatif : ils viennent de l'External Secrets Operator ou d'un Secret existant) |
| `KUBE_CONTEXT` | variable, optionnelle | contexte du kubeconfig, sinon son contexte courant |
| `HELM_NAMESPACE` | variable, optionnelle | namespace de la release, `parcelflow` par défaut ; il doit exister |
| `HELM_RELEASE` | variable, optionnelle | nom de la release, `parcelflow` par défaut |

Tant qu'un environnement n'a pas `KUBECONFIG` et `HELM_VALUES`, le job réussit sans déployer et l'indique dans le résumé de l'exécution. Avant de configurer un environnement, définir la variable de dépôt `KUBERNETES_DEPLOY` à `true` : sans elle, les images `<service>-migrations` des hooks ne sont pas publiées, et un environnement configuré fait échouer le job au lieu de déployer. Recommandé sur `production` : reviewers obligatoires et branche de déploiement limitée à `main` ; sur `staging` : branche limitée à `develop`.

Si les packages GHCR sont privés, le cluster a besoin d'un secret de pull (`image.pullSecrets` dans les values).

## Recommandations pour le dépôt

- Protéger `main` et `develop` : PR obligatoire et check `CI OK` requis (voir [Exécution sélective](#exécution-sélective)).
- Activer Secret scanning et Push protection (Settings → Code security).

## Vérifications locales

```powershell
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.21.2 git /repo --config /repo/.gitleaks.toml
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.12
Get-Content src/Gateway/ParcelFlow.Gateway/Dockerfile | docker run --rm -i -v "${PWD}/.hadolint.yaml:/.hadolint.yaml" hadolint/hadolint:v2.15.1 hadolint --config /.hadolint.yaml -
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.70.0 image --scanners vuln --severity CRITICAL,HIGH --ignore-unfixed --exit-code 1 parcelflow/gateway:local
```
