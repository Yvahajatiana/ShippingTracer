# Chapitre 9 — Frontends : trois portails Angular

## Objectif

Construire trois portails qui consomment **uniquement la gateway**, sans modifier ni services ni gateway :

| Portail | Port de dev | Public |
| --- | --- | --- |
| `customer-portal` | 4201 | Clients : suivi public, envois, factures, notifications |
| `operations-portal` | 4202 | Opérateurs et livreurs : scans, tournées, tableau de bord |
| `admin-portal` | 4203 | Administration : entreprises, tarifs, livreurs, avoirs, KPIs |

## Étape 1 — Un seul workspace, une bibliothèque partagée

```text
frontends/
├── angular.json  package.json  tsconfig.json  components.json  proxy.conf.json
└── projects/
    ├── shared/            auth, HTTP, clients API typés, i18n, shell, mocks, composants
    │   └── helm/          composants Spartan générés
    ├── customer-portal/   operations-portal/   admin-portal/
```

La bibliothèque est consommée **depuis ses sources**, par alias TypeScript, sans étape de build intermédiaire (`tsconfig.json`) :

```json
"paths": {
  "@parcelflow/shared": ["./projects/shared/src/public-api.ts"],
  "@parcelflow/shared/testing": ["./projects/shared/src/testing.ts"],
  "@spartan-ng/helm/button": ["./projects/shared/helm/button/src/index.ts"]
}
```

Pile : Angular 22 (standalone, zoneless, signals, `OnPush`), Tailwind CSS 4, `@ngrx/signals`, Transloco, `keycloak-angular`, MSW, Vitest.

## Étape 2 — Le choix de la bibliothèque UI : pourquoi pas PrimeNG

PrimeNG était le premier candidat. Mais **PrimeNG 22**, seule ligne compatible avec Angular 22, exige désormais une clé de licence commerciale (« PrimeUI ») et affiche une bannière sans clé. Pour un projet ouvert, c'est rédhibitoire.

Choix retenu : **Spartan UI**, déclinaison Angular du modèle shadcn, sous licence MIT :

- `@spartan-ng/brain` fournit les primitives accessibles (comportement, clavier, ARIA) en dépendance npm ;
- les composants stylés **helm** sont **générés dans le dépôt** par `@spartan-ng/cli`, selon `components.json` :

```json
{ "componentsPath": "projects/shared/helm", "importAlias": "@spartan-ng/helm", "style": "vega" }
```

> **Conséquence :** le code des composants appartient au projet et reste modifiable. Il est exclu du lint (code généré). Le composant chart de Spartan reposant sur une version 0.x de TanStack Charts, les graphiques utilisent chart.js derrière un composant maison (étape 6).

## Étape 3 — Configuration à l'exécution, pas à la compilation

Chaque portail charge `config.json` **avant** le démarrage d'Angular (`shared/src/lib/providers.ts`, extrait sans les types) :

```ts
export async function bootstrapPortal(root, options, appConfig) {
  try {
    const config = await loadAppConfig();
    if (config.mockApi) {
      const { startMockApi } = await import('./mocks/browser');
      await startMockApi();
    }
    const portal = appConfig(config);
    await bootstrapApplication(root, { providers: [...provideParcelFlow(config, options), ...portal.providers] });
  } catch (error) {
    renderFatalError(error);
  }
}
```

Le `main.ts` d'un portail tient en trois lignes :

```ts
void bootstrapPortal(App, { portal: 'customer', keycloakOnLoad: 'check-sso', mockDefaultSubject: 'customer-alice' }, appConfig);
```

> **Pourquoi ?** Une même image Docker peut être déployée dans tous les environnements : seul `config.json` change (généré par le conteneur au chapitre 10). Les origines sont validées (`safeOrigin`) : HTTPS obligatoire hors boucle locale, pas d'identifiants dans l'URL.

Trois jeux de configuration par portail : `config/mock` (défaut de `ng serve`, aucun backend), `config/gateway` (Keycloak local + gateway via `proxy.conf.json`), `config/production`.

## Étape 4 — Authentification et HTTP

Keycloak en code flow + PKCE, derrière un port `AuthService` remplaçable par `MockAuthService` :

```ts
provideKeycloak({
  config: config.keycloak,
  initOptions: { onLoad: options.keycloakOnLoad, pkceMethod: 'S256', checkLoginIframe: false },
  features: [withAutoRefreshToken({ onInactivityTimeout: 'logout', sessionTimeout: SESSION_TIMEOUT_MS })],
  providers: [AutoRefreshTokenService, UserActivityService],
}),
```

L'intercepteur (`shared/src/lib/http/api.interceptor.ts`) n'ajoute le Bearer qu'aux requêtes `/api/` de l'origine configurée, et refuse les chemins dangereux :

```ts
export function buildApiUrl(apiBaseUrl: string, path: string): string {
  if (!path.startsWith('/api/') || path.includes('\\') || /[\r\n]/.test(path) || path.includes('..')) {
    throw new Error(`Invalid API path: ${path}`);
  }
  return apiBaseUrl + path;
}
```

Le suivi public passe le contexte `ANONYMOUS` (pas de jeton). Les ProblemDetails deviennent des `ApiError` dont le message est une clé de traduction, avec le `traceId` affiché comme référence. Les mutations idempotentes (paiement, avoir, scan, livraison) génèrent un `OperationId` réutilisé en cas de nouvel essai, ce qui exploite directement l'idempotence des services.

> Les rôles côté client ne servent qu'à la navigation : l'autorisation reste appliquée par la gateway et les services.

## Étape 5 — État : un signal store par fonctionnalité

Exemple `customer-portal/src/app/tracking/tracking.store.ts` :

```ts
export const TrackingStore = signalStore(
  withState<TrackingState>({ trackingNumber: null, tracking: null, loading: false, error: null }),
  withComputed(({ error, tracking }) => ({
    notFound: computed(() => error()?.status === 404),
    timeline: computed(() => [...(tracking()?.history ?? [])].reverse()),
  })),
  withMethods((store, api = inject(TrackingApi)) => ({
    load: rxMethod<string>(pipe(
      tap((trackingNumber) => patchState(store, { trackingNumber, tracking: null, loading: true, error: null })),
      switchMap((trackingNumber) => api.publicTracking(trackingNumber).pipe(
        tap((tracking) => patchState(store, { tracking, loading: false })),
        catchError((error: unknown) => { patchState(store, { loading: false, error: toApiError(error) }); return EMPTY; }),
      )),
    )),
  })),
);
```

> Signals pour l'état et le rendu, RxJS là où il excelle (HTTP, `switchMap` pour annuler une recherche obsolète, debounce).

## Étape 6 — Graphiques, localisation, API simulée

- **`pf-chart`** (`shared/src/lib/ui/chart.component.ts`) : chart.js est chargé à la demande au premier rendu, hors du bundle initial ; les couleurs lisent les jetons CSS du thème (`--chart-1`…).
- **Localisation FR/EN** : textes communs dans `shared/i18n`, textes de portail dans `public/i18n`, fusionnés par `MergedTranslationLoader`. Formats via `Intl` (`fr-FR`, `en-GB`). Un test vérifie que les deux langues ont les mêmes clés.
- **MSW** : des handlers couvrent tous les endpoints de la gateway avec les règles principales de rôles et de propriété ; un sélecteur d'identité permet de changer de rôle. Les portails se développent sans aucun backend.

## Étape 7 — Thème « corporate » du portail d'administration

Maquetté dans un canevas Claude Design (vue d'ensemble, liste et détail de facture), puis implémenté dans la PR #43 :

- le shell partagé accepte `layout="sidebar"` et un `groupKey` par entrée de navigation (sections Pilotage, Référentiel, Finance et relation client) ; les deux autres portails gardent `layout="header"` ;
- les jetons propres au portail sont dans `admin-portal/src/theme.css`, importé après le thème partagé, avec IBM Plex Sans/Mono auto-hébergées (`@fontsource`, aucun CDN). Extrait abrégé :

```css
:root {
  --font-sans: 'IBM Plex Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --font-mono: 'IBM Plex Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  --background: #f3f5f8;
  --primary: #1b4db1;
}
```

> Les composants helm ne sont **pas** modifiés : en-têtes et cellules de tableau sont ajustés via leurs attributs `data-slot`. Un thème = des jetons, pas un fork des composants.

## Vérifier

Depuis `frontends/` :

```powershell
npm ci
npm run format:check
npm run lint
npm test
npm run build
npm audit --omit=dev
npm run start:customer        # portail en mode simulé sur http://localhost:4201
```

À la livraison : 34 tests shared, 52 customer, 37 operations, 49 admin ; trois builds de production sans avertissement ; 0 vulnérabilité. Le job CI `frontends` exécute la même séquence.

Référence : [docs/frontends.md](../frontends.md).

Chapitre suivant : [Docker et plateforme locale](10-docker.md).
