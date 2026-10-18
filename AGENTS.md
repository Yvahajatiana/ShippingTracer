# Consignes de travail — ParcelFlow

## Git

- Chaque bloc fonctionnel doit être développé dans une branche dédiée, créée avant de commencer son implémentation.
- Ne pas développer directement sur les branches partagées, notamment main, master ou develop.
- Toujours committer le travail effectué avec un message clair et un périmètre cohérent. Vérifier les changements et exécuter les contrôles adaptés avant le commit.
- Toute fusion doit passer par une pull request. Ne jamais fusionner directement en local ni pousser directement sur une branche partagée.
- Préparer la PR avec un résumé du changement et les résultats des vérifications. L'utilisateur autorise désormais la fusion par PR après contrôles réussis et analyse de la review Codex ; ne pas demander une nouvelle confirmation pour fusionner.
- Toujours examiner les reviews du bot Codex sur les PR et ses commentaires de synthèse. Corriger les défauts pertinents, tester et pousser les correctifs. Pour une suggestion non contraignante écartée, documenter le raisonnement dans le thread ; résoudre les points traités puis fusionner la PR. Vérifier que la review terminée concerne le commit courant.

## Étapes et validation

- Respecter l'ordre demandé : BuildingBlocks, puis chaque service séparément, gateway, frontends, Docker, Kubernetes, CI/CD et README.
- À l’étape finale, avec le README, rédiger dans docs un tutoriel Markdown (un ou plusieurs fichiers) expliquant la construction du projet ; ne pas rédiger ce tutoriel pendant les étapes intermédiaires.
- À la fin de chaque étape, committer le travail, résumer les changements et les vérifications, puis attendre la validation de l'utilisateur avant de continuer.
- Ne pas modifier un fichier déjà validé sans demande explicite.
- Agir en architecte senior et product owner : trancher les choix courants et documenter les décisions. Ne demander un arbitrage que pour un choix structurant impossible à déduire du périmètre ou engageant le produit de façon importante.

- L’ajout des nouveaux projets à la solution et des nouveaux contrats dans BuildingBlocks est autorisé pour intégrer chaque service. Préserver le comportement des composants déjà validés.

- Déléguer davantage lorsque les tâches sont indépendantes : définir les interfaces et la propriété des fichiers avant le travail parallèle ; séquencer les migrations, les compilations et l'intégration finale pour éviter les conflits.
