# Consignes de travail — ParcelFlow

## Git

- Chaque bloc fonctionnel doit être développé dans une branche dédiée, créée avant de commencer son implémentation.
- Ne pas développer directement sur les branches partagées, notamment main, master ou develop.
- Toujours committer le travail effectué avec un message clair et un périmètre cohérent. Vérifier les changements et exécuter les contrôles adaptés avant le commit.
- Toute fusion doit passer par une pull request. Ne jamais fusionner directement en local ni pousser directement sur une branche partagée.
- Préparer la PR avec un résumé du changement et les résultats des vérifications. La création d'une PR ne vaut pas autorisation de fusion.
- Toujours examiner les reviews du bot Codex sur les PR : corriger les remarques pertinentes, tester, committer et pousser les correctifs, puis répondre aux threads et résoudre ceux qui sont traités avant toute fusion.

## Étapes et validation

- Respecter l'ordre demandé : BuildingBlocks, puis chaque service séparément, gateway, frontends, Docker, Kubernetes, CI/CD et README.
- À la fin de chaque étape, committer le travail, résumer les changements et les vérifications, puis attendre la validation de l'utilisateur avant de continuer.
- Ne pas modifier un fichier déjà validé sans demande explicite.
- Agir en architecte senior et product owner : trancher les choix courants et documenter les décisions. Ne demander un arbitrage que pour un choix structurant impossible à déduire du périmètre ou engageant le produit de façon importante.

- L’ajout des nouveaux projets à la solution et des nouveaux contrats dans BuildingBlocks est autorisé pour intégrer chaque service. Préserver le comportement des composants déjà validés.
