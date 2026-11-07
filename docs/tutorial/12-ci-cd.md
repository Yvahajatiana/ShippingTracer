# Chapitre 12 — CI/CD complète et déploiement continu

## Objectif

Compléter la première CI du chapitre 8 pour qu'un push sur `develop` ou `main` produise, sans intervention : contrôles de code, images scannées et publiées sur GHCR, chart Helm validé, puis **déploiement** en staging ou en production avec retour arrière automatique.

Deux fichiers : `.github/workflows/ci.yml` (contrôles, images, appel du déploiement) et `.github/workflows/deploy.yml` (workflow réutilisable de déploiement).

## Étape 1 — Vue d'ensemble des jobs

```text
secret-scan ─┐
build ───────┤
frontends ───┼──► images (matrice 21) ──┐
docker-lint ─┘                          │
integration (matrice 8) ────────────────┼──► deploy ──► deploy.yml (staging | production)
gateway-smoke ──────────────────────────┤
helm ───────────────────────────────────┘
```

| Job | Rôle |
| --- | --- |
| Secret scan | gitleaks sur tout l'historique avec `.gitleaks.toml` (chapitre 8) |
| Build, unit and contract tests | Build Release, tests hors intégration, dérive EF Core sur 8 services, NuGet vulnérables |
| Integration tests (matrice) | Un job par service, Testcontainers, sans arrêt au premier échec |
| Gateway Kestrel smoke test | `tests/Gateway/kestrel-smoke.mjs` sous Node 22 |
| Frontends | `npm audit`, Prettier, ESLint, tests Vitest, builds de production |
| Dockerfile lint and compose validation | hadolint sur tous les Dockerfiles, `docker compose config --quiet` avec des secrets factices |
| Helm chart lint and schema validation | `helm lint --strict` + `helm template` sur 4 profils, kubeconform |
| Image (matrice) | Build Buildx, scan Trivy, publication GHCR |
| Deploy | Appel de `deploy.yml` après un push sur `develop` ou `main` |

Le jeton GitHub est en lecture seule par défaut ; seul le job images reçoit `packages: write` et `security-events: write`. Une nouvelle exécution sur la même PR annule la précédente.

## Étape 2 — Contrôles .NET qui comptent

Au-delà de `dotnet test`, deux contrôles évitent des incidents classiques :

```bash
# EF Core model drift : une modification du modèle sans migration fait échouer la CI
dotnet ef migrations has-pending-model-changes --project "$infra" --configuration Release --no-build
# Dépendances vulnérables, transitives comprises
dotnet list ParcelFlow.sln package --vulnerable --include-transitive | tee vulnerable.txt
```

## Étape 3 — Valider le chart sur plusieurs profils

Le job `helm` rend le chart quatre fois : local (secrets factices et realm), production avec Ingress, production avec Gateway API, Secret existant. Chaque rendu est validé par kubeconform pour la version minimale de `Chart.yaml` et une version récente :

```bash
kubeconform -strict -summary -kubernetes-version "$version" \
  -schema-location default \
  -schema-location 'https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json' \
  "$RENDER_DIR"/*.yaml
```

> Les CRD (`ExternalSecret`, `HTTPRoute`) n'ont pas de schéma natif : le catalogue datreeio les couvre. Le binaire kubeconform est téléchargé puis vérifié par checksum avant installation.

## Étape 4 — 21 images : construire, scanner, publier

La matrice `images` couvre 9 images .NET (gateway + 8 services), 3 portails, RabbitMQ et 8 bundles de migration (`deploy/migrations/Dockerfile` avec `SERVICE=<Service>`) :

```yaml
- { name: shipment, context: ., dockerfile: src/Services/Shipment/ParcelFlow.Shipment.Api/Dockerfile, build-arg: '' }
- { name: admin-portal, context: frontends, dockerfile: frontends/Dockerfile, build-arg: PORTAL=admin-portal }
- { name: shipment-migrations, context: ., dockerfile: deploy/migrations/Dockerfile, build-arg: SERVICE=Shipment }
```

Séquence par image :

1. build Buildx avec cache GitHub Actions par image (`cache-from/to: type=gha,scope=<image>`), image chargée localement ;
2. **Trivy** : vulnérabilités OS et bibliothèques `HIGH`/`CRITICAL` corrigeables, échec au moindre résultat ; hors PR, rapport SARIF publié dans Code scanning, ou en artefact du workflow si le dépôt est privé ;
3. **publication GHCR** uniquement sur push vers `main`/`develop` du dépôt d'origine, jamais sur une PR ni depuis un fork.

Nom : `ghcr.io/<owner>/parcelflow-<image>`. Tags : `sha-<sha court>` à chaque build, `develop`/`main` selon la branche, `latest` sur `main`. L'action Trivy est épinglée par **commit** (ses tags ont déjà été réécrits par le passé).

> **Pourquoi scanner l'image construite et non le dépôt ?** Les vulnérabilités viennent aussi de l'image de base. C'est pourquoi les images RabbitMQ et nginx appliquent `apk upgrade` pour intégrer les correctifs Alpine que l'image amont n'a pas encore reçus (chapitre 10).

## Étape 5 — Le déploiement continu

Job `deploy` de `ci.yml` :

```yaml
deploy:
  needs: [images, integration, gateway-smoke, helm]
  if: github.event_name == 'push' && !github.event.repository.fork && (github.ref == 'refs/heads/main' || github.ref == 'refs/heads/develop')
  uses: ./.github/workflows/deploy.yml
  with:
    environment: ${{ github.ref == 'refs/heads/main' && 'production' || 'staging' }}
  secrets: inherit
```

`deploy.yml` (déclencheurs `workflow_call` et `workflow_dispatch`) :

```bash
tag="${INPUT_TAG:-sha-${GITHUB_SHA::7}}"
# Production only runs immutable commit tags, never a branch tag or latest.
if [[ "$TARGET" == production && ! "$tag" =~ ^sha-[0-9a-f]{7,40}$ ]]; then exit 1; fi
# ...
helm upgrade --install "$RELEASE" "$CHART" \
  "${context_args[@]}" --namespace "$NAMESPACE" \
  -f "$RUNNER_TEMP/values.yaml" \
  --set-string "image.tag=$TAG" \
  --rollback-on-failure --wait --timeout 15m --history-max 10
```

Décisions :

- **Tag de commit immuable** : on déploie exactement ce qui vient d'être testé et scanné, jamais `latest`.
- **Migrations avant workloads** : les Jobs hooks du chapitre 11 s'exécutent dans `helm upgrade`.
- **`--rollback-on-failure --wait`** : une release qui n'atteint pas l'état prêt revient à la révision précédente. Les migrations appliquées ne sont pas annulées, d'où la règle de compatibilité ascendante.
- **Un seul déploiement à la fois par environnement**, jamais annulé en cours (`concurrency`, `cancel-in-progress: false`).
- **Retour arrière manuel** : Actions → Deploy → Run workflow avec un tag `sha-…` antérieur.
- **Environnements GitHub** `staging` et `production` : secrets `KUBECONFIG` (compte limité au namespace) et `HELM_VALUES` (sans secret applicatif), variables optionnelles `KUBE_CONTEXT`, `HELM_NAMESPACE`, `HELM_RELEASE`. Reviewers obligatoires recommandés sur `production`.
- **Dégradation propre** : tant qu'un environnement n'est pas configuré, le job réussit sans déployer et l'indique dans le résumé de l'exécution.

## Étape 6 — Dépendances et protection des branches

- `.github/dependabot.yml` : mises à jour hebdomadaires NuGet (groupées Microsoft, MassTransit, tests) et actions GitHub, sur `develop`.
- Recommandé : protéger `main` et `develop` (PR obligatoire, jobs requis) et activer Secret scanning + Push protection.

## Vérifier localement

```powershell
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.12
Get-Content src/Gateway/ParcelFlow.Gateway/Dockerfile | docker run --rm -i -v "${PWD}/.hadolint.yaml:/.hadolint.yaml" hadolint/hadolint:v2.15.1 hadolint --config /.hadolint.yaml -
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.70.0 image --scanners vuln --severity CRITICAL,HIGH --ignore-unfixed --exit-code 1 parcelflow/gateway:local
```

Sur GitHub : ouvrir une PR vers `develop` et vérifier que tous les jobs passent, puis contrôler après fusion que les images `sha-…` apparaissent dans GHCR et que le job Deploy indique « deployed » ou « skipped ».

Référence : [docs/ci-cd.md](../ci-cd.md).

## Conclusion

Vous avez parcouru toute la chaîne : un socle minimal, huit services autonomes reliés par des événements fiables, une gateway, trois portails, des images durcies, un chart Helm prêt pour la production et un pipeline qui teste, scanne, publie et déploie. Les tickets encore ouverts (#17, #19, #24, #39, #42…) sont de bons points de départ pour prolonger le projet.

Retour au [sommaire](README.md).
