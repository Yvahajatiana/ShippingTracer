# Parcours métier de bout en bout

`business-journey.mjs` vérifie qu'un colis va de la création à la livraison sur la plateforme assemblée par `docker-compose.yml`. Tous les appels métier passent par la gateway (`http://localhost:5100`) avec de vrais jetons Keycloak. Le script n'utilise que Node.js 22 ou plus récent, sans dépendance npm. Il fonctionne sous Windows comme sous Linux.

## Lancement

```bash
cp .env.example .env            # adapter KEYCLOAK_PORT si 8080 est occupé
docker compose -p parcelflow-e2e up -d --build --wait
node tests/e2e/business-journey.mjs
docker compose -p parcelflow-e2e down -v
```

Un nom de projet dédié (`-p parcelflow-e2e`) isole les volumes du parcours de ceux de la plateforme locale habituelle. `down -v` ne supprime ainsi que les données de test. La plateforme locale déjà démarrée sous le projet `parcelflow` convient aussi : le parcours est rejouable sur une base existante.

| Variable | Défaut | Usage |
| --- | --- | --- |
| `GATEWAY_URL` | `http://localhost:5100` | Gateway |
| `KEYCLOAK_URL` | `http://localhost:${KEYCLOAK_PORT:-8080}` | Keycloak public, émetteur des jetons |
| `KEYCLOAK_ADMIN_PASSWORD` | `.env` | Création du compte client jetable |
| `DEMO_USER_PASSWORD` | `.env` | Connexion de l'opératrice-livreuse `camille.martin` |
| `E2E_TIMEOUT_SECONDS` | `90` | Borne de chaque attente asynchrone |
| `E2E_KEEP_USER` | `0` | `1` conserve le compte client Keycloak jetable |

Les variables d'environnement sont prioritaires, puis le script lit `.env` à la racine du dépôt. Le code de sortie vaut 0 si le parcours réussit, 1 en cas d'échec et 2 si la configuration est incomplète. En cas d'échec, le script affiche l'étape en cause, la dernière valeur observée, l'envoi concerné et l'identifiant de corrélation `X-Correlation-Id` commun à toutes les requêtes.

## Jetons

- **Client :** un compte jetable `e2e-<run>` reçoit le rôle `customer`. Il est créé par l'API d'administration Keycloak du conteneur en cours d'exécution, puis supprimé en fin de parcours. Le fichier du realm n'est pas modifié.
- **Opérateur :** `camille.martin` est l'opératrice du seed, et aussi la livreuse `demo-driver-lyon` de Delivery.
- **Flux :** les deux connexions suivent le flux réel des portails, code d'autorisation et PKCE S256, sur `parcelflow-customer` et `parcelflow-operations`. Le script soumet le formulaire de connexion Keycloak et échange le code contre un jeton. L'URI de redirection `http://localhost:420x/` n'est jamais appelée. Aucun accès direct par mot de passe n'est activé sur les clients publics.

## Étapes vérifiées

1. **Accès :** refus anonyme (401), et refus des jetons dont l'audience ne couvre pas le service (Reporting et Delivery pour le client, Billing pour l'opérateur).
2. **Client :** création du profil et d'une adresse, puis devis domestique recalculé à partir du tarif publié. Création de l'envoi au prix du devis, puis étiquette PDF nommée d'après le numéro de suivi.
3. **Billing :** facture `PendingPayment` émise par la saga, puis paiement simulé `Approved`, rejoué à l'identique. La facture passe `Paid` et l'envoi `ReadyForPickup`.
4. **Warehouse :** entrée à `FR-LYON-01` (prise en charge, envoi `PickedUp`) puis sortie. Ensuite, entrée et sortie au centre de tri `FR-PARIS-01`. Le stock est contrôlé après chaque scan. Un scan refusé en 409 avant la propagation de `ShipmentReadyForPickup` est retenté avec le même `ScanId`.
5. **Delivery :** colis disponible à l'affectation, puis tournée créée, livreur et arrêt affectés. Après le démarrage, la preuve de livraison est enregistrée avec le nom du destinataire et une signature PNG, relue à l'octet près. La tournée passe `Completed`.
6. **Tracking :** suivi public anonyme, numéro en minuscules entouré d'espaces. Historique attendu : `Created > PickedUp > InTransit > AtSortingCenter > InTransit > OutForDelivery > Delivered`, sans donnée privée. Le suivi interne opérateur est également consulté.
7. **Notification :** un reçu Email et un reçu Sms `MockSent` pour chacun des neuf faits : création, facture, paiement et six statuts de suivi. Les reçus sont adressés à l'email du client.
8. **Reporting :** les KPI du jour de création comptent l'envoi livré, sur un rapport généré après la livraison (le cache Reporting dure trente secondes).

Les attentes asynchrones sont bornées par `E2E_TIMEOUT_SECONDS` et respectent les quotas de la gateway. Une réponse 429 est renvoyée après `Retry-After`.

## CI

Le job `E2E business journey` de `.github/workflows/ci.yml` construit et démarre la stack avec `docker compose up -d --build --wait`, à partir de `.env.example`, puis lance le script. Seuls la gateway, les huit services et leurs dépendances sont démarrés. Les portails ne sont pas appelés par le parcours, et le job `frontends` les construit déjà. En cas d'échec, il affiche l'état des conteneurs et les journaux. La stack et ses volumes sont toujours supprimés à la fin du job.
