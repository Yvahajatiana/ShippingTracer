# ADR 0001 — Sortie Warehouse automatique au départ en livraison

- Statut : accepté
- Date : 2026-10-22
- Ticket : #16

## Contexte

La documentation impose une sortie Warehouse (scan Exit) avant la mise en livraison, mais rien ne l’applique. Delivery peut démarrer une tournée pour un colis encore en stock ; le colis livré reste alors indéfiniment dans le stock Warehouse, qui devient faux (stock affiché, impossibilité de désactiver le site).

Delivery et Warehouse ont des bases et des transactions indépendantes et communiquent uniquement par événements d’intégration (MassTransit, Outbox/Inbox EF). Aucun contrat propre à Delivery n’existe dans BuildingBlocks : au démarrage d’une tournée, Delivery publie pour chaque arrêt `Tracking.V1.ParcelStatusRecorded` avec le statut `OutForDelivery`, puis `Delivered` ou `Failed` à la clôture de l’arrêt. Delivery est la seule source de ces deux statuts ; Warehouse publie le même contrat, mais uniquement avec `InTransit` et `AtSortingCenter`.

## Décision

Warehouse réagit au départ en livraison et enregistre une sortie automatique et idempotente. Delivery reste autonome.

- Warehouse consomme `ParcelStatusRecorded` (file `warehouse-parcel-delivery-started`, Inbox/Outbox) et ne retient que `OutForDelivery`, ainsi que `Delivered` comme filet de sécurité si le départ n’a pas été traité. Aucun nouveau contrat partagé n’est créé : l’événement existant, publié par la source de vérité, suffit.
- Si le colis est en stock, la commande `RecordDeliveryExit` applique un scan Exit dans son site courant via le modèle de domaine (`ParcelInventory.ApplyScan`) et le même chemin de persistance que les sorties manuelles : verrou par numéro de suivi, scan d’audit, transaction de l’Outbox du consommateur.
- Le scan porte le sujet `system:delivery-start`, l’heure de l’événement source et un ScanId UUID v8 dérivé par SHA-256 de l’EventId source. Une redélivrance, même avec un nouvel identifiant de transport qui contourne l’Inbox, retrouve ce scan et ne produit aucun mouvement.
- Aucune action si le colis est hors stock (déjà sorti ou jamais entré), inconnu, associé à une autre expédition, ou s’il a bougé après l’heure de l’événement (retour au dépôt) : le consommateur journalise le résultat en Debug.
- La sortie automatique ne publie aucun événement : Tracking affiche déjà En livraison ou Livré, et un `InTransit` tardif ferait régresser l’historique public.

## Alternatives envisagées

**Delivery projette les mouvements Warehouse et refuse le démarrage d’une tournée contenant un colis en stock.** Écartée : elle crée un couplage fort (Delivery devrait consommer et maintenir une projection du stock, avec sa latence de propagation), bloque les livreurs sur une discipline de scan au moment du départ et fait d’une erreur d’entrepôt un incident de livraison. Le colis est physiquement dans le véhicule : refuser le démarrage ne rend pas le stock plus juste.

**Appel synchrone de Delivery vers Warehouse au démarrage.** Écartée : dépendance de disponibilité entre services, contraire à l’architecture événementielle du projet.

**Nouveau contrat `Delivery.V1.RouteStarted`.** Écartée à ce stade : `ParcelStatusRecorded` `OutForDelivery` porte déjà ShipmentId, numéro de suivi et heure, et il est publié atomiquement par Delivery via son Outbox. Un contrat dédié pourra être introduit si d’autres consommateurs en ont besoin.

## Conséquences

- Le stock Warehouse est cohérent à terme : un colis parti en livraison disparaît du stock dès la consommation de l’événement, sans action opérateur.
- Le scan Exit manuel reste la pratique attendue ; les sorties automatiques sont identifiables par leur sujet `system:delivery-start` et permettent de mesurer les écarts de discipline de scan.
- Warehouse reçoit aussi ses propres `ParcelStatusRecorded`, filtrés sans effet ; le coût est négligeable.
- La règle « heure de l’événement postérieure au dernier mouvement » suppose des horloges de services raisonnablement synchronisées ; un mouvement scanné après le départ en livraison l’emporte sur un événement tardif.
- Aucune migration : le modèle de données est inchangé.
