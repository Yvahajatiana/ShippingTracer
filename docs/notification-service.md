# Notification Service

Septième service métier de ParcelFlow, en .NET 8/C# 12 : Domain, Application, Infrastructure et Api. Les notifications email et SMS sont simulées ; leur effet est un reçu PostgreSQL `MockSent`. Aucun message réel ne quitte la plateforme.

## Événements et destinataires

| Fait reçu | Notification produite |
| --- | --- |
| ShipmentCreated | Création de l’envoi |
| TrackingStatusChanged | Statut observé, sauf Created déjà couvert par ShipmentCreated |
| InvoiceIssued | Référence de facture et montant EUR |
| PaymentCompleted | Acceptation du paiement simulé |
| PaymentFailed | Refus du paiement simulé, texte fixe |
| CreditNoteIssued | Référence d’avoir et montant EUR |

Chaque fait éligible crée deux reçus : Email et Sms. Les messages de suivi indiquent la date UTC du fait observé ; une livraison tardive d’un événement historique ne prétend donc pas décrire le statut courant du colis. Le motif technique d’un refus de paiement n’est jamais recopié dans le texte destiné au client. Les contenus sont bornés à 200 caractères pour le sujet, 2 000 pour l’email et 320 pour le SMS.

Les contrats existants n’exposent pas les coordonnées. Notification lit le snapshot immuable de l’expéditeur via `GET /api/v1/shipments/{shipmentId}`. Un compte de service Keycloak utilise client_credentials, le rôle operator et l’audience shipment-api. L’expéditeur est le destinataire des notifications, et le propriétaire technique vient de Shipment. Les propriétaires et numéros de suivi présents dans les événements sont comparés à cette source avant tout enregistrement. Aucun producteur validé n’est modifié.

La résolution ne dépend pas de l’ordre d’arrivée des faits : Shipment a déjà persisté son envoi avant sa publication Outbox. Un paiement peut donc être notifié avant que Notification ne reçoive ShipmentCreated. Le snapshot est mis en cache Redis pendant cinq minutes. Une indisponibilité, une entrée invalide ou une lecture dépassant 250 ms entraîne une résolution HTTP. Aucun résultat négatif ni token OAuth n’est stocké dans Redis.

Le token OAuth est conservé en mémoire jusqu’à son expiration moins trente secondes, avec renouvellement synchronisé. Un 401 de Shipment l’invalide et déclenche un seul renouvellement immédiat. Les deux dépendances exigent HTTPS hors Development ; HTTP est réservé au développement local. Chaque appel réseau possède un délai de cinq secondes. Polly borne les reprises des erreurs réseau, timeouts, 429 et 5xx et ouvre un circuit breaker partagé entre événements. Les autres 4xx ne sont pas repris par ce pipeline. Les corps des réponses et les secrets ne sont pas journalisés.

## Persistance et idempotence

La base dédiée `parcelflow_notification` contient `notification_events`, `notification_messages` et les tables Inbox/Outbox MassTransit. La clé d’un fait est son EventId. Un verrou transactionnel PostgreSQL est pris avant sa lecture, même si la ligne n’existe pas encore. Un rejeu des mêmes métadonnées utiles ne résout pas de nouveau les coordonnées et ne republie rien. Une réutilisation contradictoire est rejetée. Les dates sont normalisées en UTC à la microseconde, comme PostgreSQL.

La contrainte unique `(SourceEventId, Channel)` limite chaque fait à un reçu par canal ; une clé étrangère obligatoire relie les reçus au fait. L’événement, ses deux reçus et les deux messages NotificationRecorded sont enregistrés dans la même transaction ReadCommitted. Chaque NotificationRecorded reprend la CorrelationId du fait consommé et a un EventId dérivé de NotificationId. Les consommateurs réutilisent la transaction de l’Outbox EF Core. Après les reprises bornées, un échec reste disponible dans la file d’erreur MassTransit.

NotificationRecorded v1 contient les identifiants du reçu, du fait source et de l’envoi, le propriétaire technique, le canal et la date. Il ne contient aucune adresse email, aucun téléphone, aucun sujet et aucun corps de message. Son nom décrit un enregistrement simulé et ne constitue pas une preuve de livraison réelle.

## API et accès

Audience JWT : `notification-api`. Le rôle customer lit uniquement ses reçus ; admin lit tous les reçus. Un operator seul reçoit 403. Les coordonnées et contenus sont donc réservés au propriétaire et à l’administrateur. Le périmètre vient exclusivement des claims JWT ; aucun paramètre propriétaire ne peut le remplacer.

| Route | Résultat |
| --- | --- |
| GET `/api/v1/notifications` | Liste paginée, filtre optionnel shipmentId |
| GET `/api/v1/notifications/{notificationId}` | Reçu avec canal, destination, contenu et dates |

Les listes acceptent page de 1 à 1 000 000 et pageSize de 1 à 100, avec valeurs par défaut 1 et 50. Elles utilisent PostgreSQL directement avec `Cache-Control: no-store`. Le total et les éléments peuvent refléter des instants proches sous écritures concurrentes. L’API ne permet pas d’envoi manuel, de renvoi ou de modification. Les erreurs sont les ProblemDetails du socle. Swagger en Development expose les canaux comme chaînes.

## Configuration et exploitation

Les secrets sont fournis par l’environnement ; ils ne figurent pas dans appsettings.json.

| Paramètre | Valeur ou fonction |
| --- | --- |
| ConnectionStrings__NotificationDatabase | Connexion PostgreSQL dédiée, incluant le mot de passe à l’exécution |
| ConnectionStrings__Redis | Connexion Redis privée |
| RabbitMq__Host / Port / VirtualHost / Username / Password | Connexion RabbitMQ |
| Keycloak__Authority | URL du realm, HTTP local et HTTPS en production |
| Keycloak__Audience | notification-api |
| ShipmentService__BaseUrl | URL HTTPS de Shipment ; en Development, http://localhost:5082/ est accepté |
| ServiceAuthentication__ClientId | notification-service |
| ServiceAuthentication__ClientSecret | Secret du client confidentiel Keycloak |
| Database__Initialize | Initialisation au démarrage en Development seulement |
| Database__SeedDemoData | Ajout explicite des données fictives |
| OpenTelemetry__Endpoint | Export OTLP optionnel |

Le client Keycloak devra posséder le rôle operator et inclure shipment-api dans l’audience de ses access tokens. Sa configuration conteneurisée sera livrée à l’étape Docker. Les identifiants sont exigés au premier appel de résolution ; les lectures et le seed ne nécessitent pas de contacter Keycloak.

Migration EF Core : InitialNotification. `/health/live` expose la vivacité et `/health/ready` vérifie PostgreSQL, Redis et le bus. `/metrics` expose Prometheus. Serilog journalise les requêtes sans corps métier ; OpenTelemetry instrumente ASP.NET Core, HTTP et MassTransit. L’exposition réseau des endpoints d’exploitation sera configurée lors des étapes de déploiement.

Le seed ajoute deux faits fictifs et quatre reçus email/SMS, sans appel externe ni publication. Les propriétaires sont `10000000-0000-0000-0000-000000000001` et `10000000-0000-0000-0000-000000000002` ; les envois sont `81000000-0000-0000-0000-000000000001` et `81000000-0000-0000-0000-000000000002`. Les adresses utilisent example.test. Il s’agit de reçus de démonstration autonomes, pas d’envois à récupérer dans Shipment.

## Vérifications

Les tests unitaires couvrent les métadonnées, le contenu, l’idempotence et les permissions. Les tests de contrat vérifient le JSON v1 et l’absence de coordonnées dans NotificationRecorded. Les tests d’intégration utilisent PostgreSQL, RabbitMQ et Redis réels via Testcontainers, avec JWT signés pour l’API. Le test transversal passe par la véritable API Shipment protégée et le resolver HTTP de Notification ; seul le serveur OAuth externe est simulé. Les Dockerfiles, Kubernetes, CI/CD et le tutoriel global restent réservés à leurs étapes respectives.
