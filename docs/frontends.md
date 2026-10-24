# Frontends

Trois portails Angular consomment la gateway : `customer-portal` pour les clients, `operations-portal` pour les opérateurs et livreurs, `admin-portal` pour l'administration. Ils partagent une bibliothèque `shared` et vivent dans un unique workspace Angular CLI sous `frontends/`. Aucun service ni la gateway ne sont modifiés par cette étape.

## Pile technique

| Rôle | Choix |
| --- | --- |
| Framework | Angular 22 : composants standalone, zoneless, signals, `OnPush` |
| UI | Spartan UI (`@spartan-ng/brain` et composants helm générés dans `projects/shared/helm`), Tailwind CSS 4, icônes Lucide (`@ng-icons`) |
| État | `@ngrx/signals` (signal stores par fonctionnalité) ; RxJS pour HTTP, debounce et annulation |
| Authentification | Keycloak (code flow PKCE) via `keycloak-angular`, derrière un port `AuthService` |
| Localisation | Transloco, français par défaut et anglais ; formats de dates et montants via `Intl` |
| Données simulées | MSW (service worker) en développement |
| Graphiques | chart.js chargé à la demande derrière le composant `pf-chart` |
| Tests | Vitest via le builder `@angular/build:unit-test`, ESLint (angular-eslint), Prettier |

PrimeNG avait été envisagé. La version 22, seule compatible avec Angular 22, exige désormais une clé de licence commerciale « PrimeUI » et affiche une bannière sans clé. Spartan UI, déclinaison Angular du modèle shadcn sous licence MIT, l'a remplacé : les composants sont générés dans le dépôt et restent modifiables. Le composant chart de Spartan reposant sur une version 0.x de TanStack Charts, chart.js est conservé pour les tableaux de bord.

## Organisation

```
frontends/
├── angular.json  package.json  tsconfig.json  components.json  proxy.conf.json
└── projects/
    ├── shared/
    │   ├── helm/      composants Spartan générés (non lintés, modèle shadcn)
    │   ├── i18n/      traductions communes fr/en
    │   ├── public/    mockServiceWorker.js
    │   ├── styles/    thème partagé (jetons zinc, primaire indigo)
    │   └── src/       auth, HTTP, clients API typés, i18n, shell, composants, mocks
    ├── customer-portal/     port 4201
    ├── operations-portal/   port 4202
    └── admin-portal/        port 4203
```

### Thème de l'administration

Le portail d'administration adopte une identité « corporate », maquettée dans un canevas Claude Design (vue d'ensemble, liste et détail de facture) : barre latérale marine avec navigation par sections (Pilotage, Référentiel, Finance et relation client), espace de travail gris clair, primaire bleu acier, IBM Plex Sans pour le texte et IBM Plex Mono pour les références et montants. Les polices sont auto-hébergées (`@fontsource`), sans appel à un CDN.

- Le shell partagé accepte `layout="sidebar"` et un `groupKey` par entrée de navigation ; les portails client et opérations conservent la mise en page `header` par défaut.
- Les jetons propres au portail sont dans `admin-portal/src/theme.css`, importé après le thème partagé. Les composants helm ne sont pas modifiés : les en-têtes et cellules de tableau sont ajustés via leurs attributs `data-slot`.
- Les pages suivent un même gabarit : en-tête avec libellé de section, panneaux blancs à bandeau de titre, tableaux bord à bord avec barre de filtres et pagination intégrées.

La bibliothèque est consommée depuis ses sources par l'alias `@parcelflow/shared`, sans étape de build intermédiaire. Les helpers de test sont exposés par `@parcelflow/shared/testing`.

## Configuration d'exécution

Chaque portail charge `config.json` avant le démarrage. Une même image peut ainsi être déployée dans tous les environnements.

| Champ | Usage |
| --- | --- |
| `apiBaseUrl` | Origine de la gateway ; vide pour la même origine (proxy de dev ou ingress) |
| `authMode` | `keycloak` ou `mock` |
| `mockApi` | Démarre l'API simulée MSW |
| `keycloak.url`, `keycloak.realm`, `keycloak.clientId` | Client public du portail (`parcelflow-customer`, `parcelflow-operations`, `parcelflow-admin`) |

Les origines sont validées : HTTPS obligatoire hors boucle locale, pas d'identifiants dans l'URL. Trois jeux sont fournis par portail :

| Configuration Angular | Fichier | Usage |
| --- | --- | --- |
| `development` (défaut de `ng serve`) | `config/mock` | API MSW et identités simulées, aucun backend nécessaire |
| `gateway` | `config/gateway` | Keycloak local et gateway `localhost:5100` via `proxy.conf.json` |
| `production` | `config/production` | Valeurs remplacées au déploiement |

Le proxy de développement place la gateway sur la même origine que le portail. Les origines CORS de la gateway restent donc vides tant que l'étape Docker n'a pas fixé les domaines publics.

## Sécurité

- L'intercepteur n'ajoute le jeton Bearer qu'aux requêtes `/api/` de l'origine configurée. Les autres requêtes (traductions, ressources) n'en portent jamais. Le suivi public utilise un contexte HTTP `ANONYMOUS`.
- Les chemins d'API sont construits par segments encodés. Un chemin contenant `..`, `\` ou un saut de ligne est rejeté.
- Les ProblemDetails deviennent des `ApiError` dont le message est une clé de traduction ; le `traceId` est affiché comme référence. Les erreurs de validation sont reportées sur les champs de formulaire.
- Le jeton est rafraîchi trente secondes avant expiration. Un échec de rafraîchissement vide la session, et une inactivité de trente minutes déconnecte l'utilisateur.
- Les noms de fichiers proposés par le serveur (étiquettes, preuves) sont assainis avant téléchargement.
- Les mutations idempotentes (paiement, avoir, scan, livraison) génèrent un `OperationId` réutilisé en cas de nouvel essai.

Les rôles affichés côté client ne servent qu'à la navigation ; l'autorisation reste appliquée par la gateway et les services.

## Localisation

Les textes communs (actions, erreurs HTTP, validation, libellés des énumérations des contrats) sont dans `projects/shared/i18n`. Chaque portail ajoute ses clés dans `public/i18n`, et les deux fichiers sont fusionnés au chargement. Le choix de langue est mémorisé dans le navigateur et met à jour Transloco, l'attribut `lang`, les calendriers et le formatage `Intl` (`fr-FR`, `en-GB`). Un test vérifie que le français et l'anglais définissent les mêmes clés.

## API simulée

Les handlers MSW couvrent tous les endpoints exposés par la gateway, avec les règles principales de rôles et de propriété : un client ne voit que ses envois, factures et notifications ; les paiements sont idempotents ; les tournées respectent leurs transitions d'état. Shipment renvoie ses énumérations en entiers (#23), comme le service réel, et le client API les normalise. Le sélecteur d'identité du bandeau permet de changer de rôle. Les données sont réinitialisées à chaque rechargement.

## Commandes

Depuis `frontends/` :

| Commande | Effet |
| --- | --- |
| `npm ci` | Installation |
| `npm run start:customer` (`:operations`, `:admin`) | Portail en mode simulé |
| `npx ng serve <portail> --configuration gateway` | Portail contre Keycloak et la gateway locale |
| `npm run lint`, `npm test`, `npm run build` | Contrôles et builds de production |
| `npm run format:check` | Vérification Prettier |

Le job CI `frontends` exécute audit npm, formatage, lint, tests et builds de production.

## Limites connues

- Le realm Keycloak (clients publics, rôles, mappers d'audience) est versionné dans `deploy/keycloak` et importé par la plateforme Docker Compose (voir `docs/docker.md`).
- Les données simulées simplifient certaines règles métier (tarification, saga de paiement) et ne remplacent pas les tests d'intégration des services.
