# Reporting Service

Huitième et dernier microservice métier ParcelFlow : volumes, taux de livraison et délais. Les quatre projets .NET 8/C# 12 suivent Domain, Application, Infrastructure et Api, avec CQRS MediatR, FluentValidation et les ProblemDetails communs.

## Définition des KPIs

La période sélectionne une **cohorte par date de création** : from est inclus, to est exclu, avec des journées UTC et une durée de 1 à 366 jours. Les statuts représentent les derniers faits connus au calcul ; to ne constitue pas une date d’arrêt historique des statuts. Un envoi créé dans la période et livré après celle-ci peut donc être compté comme livré. Aucun SLA, prévision ou calcul de retard contractuel n’est ajouté.

| Champ | Définition |
| --- | --- |
| TotalShipments | Nombre d’envois de la cohorte, y compris ceux encore ouverts ou en échec |
| DeliveredShipments | Envois dont le statut courant est Delivered |
| DeliveryRatePercent | 100 × DeliveredShipments / TotalShipments, null si la cohorte est vide |
| AverageDeliveryHours | Moyenne de la première création à la première livraison, parmi les envois actuellement Delivered |
| AverageTransitHours | Moyenne de la première prise en charge PickedUp à la première livraison, parmi les envois actuellement Delivered |
| DeliveryDurationSamples / TransitDurationSamples | Nombre de paires de dates valides entrant dans chaque moyenne |
| DailyVolumes | Nombre d’envois créés chaque jour UTC, jours sans volume inclus |
| StatusVolumes | Répartition des envois sur les huit statuts, zéros inclus |

Un retour sort du numérateur Delivered et des moyennes de délais. Une date de départ absente ou postérieure à la première livraison exclut seulement la paire correspondante de la moyenne : le nombre d’échantillons reste explicite. Une moyenne sans échantillon est null. Les pourcentages et heures sont arrondis à deux décimales, à mi-chemin en s’éloignant de zéro. Les volumes restent des entiers longs.

Les dates de création absentes ne sont pas inventées : un envoi dont seul un fait logistique est connu reste exclu des cohortes jusqu’à l’arrivée du fait Created. Les métriques sont une projection asynchrone, pas une jointure avec les bases des autres services.

## Faits, ordre et transactions

Reporting consomme uniquement TrackingStatusChanged v1, ce qui reprend les faits rendus disponibles par Tracking sous un numéro de suivi enregistré. Les faits sont triés par date UTC à la microseconde, puis par le rang du statut et le SourceEventId original. Le transport EventId ne décide pas de leur ordre. Le calcul reproduit les règles de Tracking : Created tardif ne réinitialise pas le statut, Delivered reste terminal sauf pour Returned, et Returned absorbe les faits ultérieurs.

ShipmentMetricsUpdated reprend la CorrelationId du TrackingStatusChanged consommé (elle n’est plus remplacée par ShipmentId) et a un EventId dérivé du SourceEventId ; la corrélation n’intervient pas dans les projections. Le numéro de suivi d’un envoi est immuable. SourceEventId identifie un fait métier ; un rejeu compatible, même avec une nouvelle enveloppe EventId, n’ajoute aucun fait et ne publie rien. Chaque nouvelle enveloppe acceptée est mémorisée dans event_envelopes, y compris lors d’un rejeu compatible, pour empêcher sa réutilisation avec une autre source. La réutilisation contradictoire d’un identifiant connu est rejetée. Les contraintes PostgreSQL protègent également l’unicité des faits sources, des enveloppes enregistrées et des numéros de suivi.

La base dédiée parcelflow_reporting contient shipment_metrics, status_facts, event_envelopes et les tables Inbox/Outbox MassTransit. Les faits possèdent une clé étrangère obligatoire restrictive. Les enveloppes référencent leur fait source avec suppression restrictive ; la migration additive RememberEventEnvelopes reprend les identifiants des enveloppes originales déjà persistées. Chaque commande prend des verrous transactionnels PostgreSQL sur l’enveloppe, la source et l’envoi, dans cet ordre ; la projection utilise également xmin et Revision. Les redélivrances concurrentes ne gonflent pas les volumes.

Les projections persistées contiennent les premières dates, le statut courant et les durées valides. Un nouveau fait, la projection et ShipmentMetricsUpdated sont enregistrés dans la même transaction ReadCommitted. Le consommateur réutilise la transaction Outbox EF Core. Le contrat sortant ne contient que les identifiants de l’envoi et du fait source, la corrélation et la date d’enregistrement ; aucune coordonnée ou donnée financière n’y figure.

## Lecture et cache

Les agrégations sont exécutées dans PostgreSQL sur les projections indexées ; la lecture des KPIs ne charge pas les historiques ni une cohorte entière en mémoire. Une transaction de lecture seule RepeatableRead garantit la cohérence entre le résumé, les jours et les statuts. Polly reprend les erreurs transitoires de lecture avec un retry borné et un circuit breaker partagé ; les écritures ne sont pas rejouées par ce pipeline.

Redis conserve les rapports pendant trente secondes. La clé inclut la période et un hash de l’identité non secrète de la base PostgreSQL, afin d’isoler les rapports de bases distinctes. GeneratedAtUtc est conservé lors d’un cache hit. Les entrées invalides, incohérentes, datées dans le futur ou âgées de plus de trente secondes sont ignorées. Une erreur ou un dépassement du budget cache de 250 ms entraîne une lecture PostgreSQL. La fraîcheur dépend donc du délai de messagerie et d’au plus trente secondes de cache. Aucun cache HTTP n’est autorisé.

## API et exploitation

GET `/api/v1/reports/kpis?from=2026-10-15&to=2026-10-18` retourne un rapport agrégé. Les deux dates sont obligatoires. JWT Keycloak, audience reporting-api, rôles operator ou admin. Le rôle customer n’y accède pas. Les rôles viennent de realm_access ; les autres claims de rôle ne peuvent pas élargir les permissions. La réponse ne contient aucun identifiant individuel d’envoi, numéro de suivi, propriétaire ou contact.

Les erreurs utilisent les ProblemDetails JSON communs, y compris avec Accept XML : 400 pour une période invalide, 401/403 pour les accès, 413 au-delà de 1 MiB. Les réponses métier portent Cache-Control no-store. L’API ne propose ni écriture HTTP, ni export, ni détail individuel. Swagger est disponible en Development.

| Configuration | Usage |
| --- | --- |
| ConnectionStrings__ReportingDatabase | PostgreSQL dédié, mot de passe fourni à l’exécution |
| ConnectionStrings__Redis | Cache Redis privé |
| RabbitMq__Host / Port / VirtualHost / Username / Password | Connexion RabbitMQ |
| Keycloak__Authority | URL du realm, HTTPS hors Development |
| Keycloak__Audience | reporting-api |
| Database__Initialize | Migration au démarrage en Development seulement |
| Database__SeedDemoData | Ajout explicite des données fictives |
| OpenTelemetry__Endpoint | Export OTLP optionnel |

Les secrets ne figurent pas dans les fichiers de configuration. La migration InitialReporting crée le modèle. Les endpoints /health/live, /health/ready et /metrics exposent la vivacité, PostgreSQL/Redis/bus et Prometheus. Serilog et OpenTelemetry instrumentent ASP.NET Core et MassTransit ; les corps métier ne sont pas journalisés.

Le seed est opt-in et idempotent, sans publication : trois envois fictifs 90000000-0000-0000-0000-000000000001 à 003, dix faits, un colis livré, un en transit et un retourné. Pour la cohorte du 15 octobre 2026 inclus au 18 exclu : volume 3, taux 33,33 %, moyenne de livraison 26 h et de transit 24 h, avec un échantillon pour chaque délai.

Les tests unitaires vérifient le calcul et l’ordre des faits ; les tests de contrat vérifient le JSON v1. Les tests d’intégration utilisent PostgreSQL, RabbitMQ et Redis réels via Testcontainers. Le workflow transversal passe par Tracking et sa véritable Outbox, puis compare les KPIs au statut de son API publique. Le tutoriel global reste réservé à l’étape finale avec le README.
