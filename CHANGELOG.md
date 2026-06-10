## v0.9

### Nouveautés
- 🎨 **Logo** : la nouvelle icône CleanSlate (balai) remplace l'icône générique du `.exe` et apparaît dans la barre de titre.
- 🎮 **Overclocking** (Mode Jeu) : nouvel onglet qui détecte automatiquement votre carte graphique (NVIDIA / AMD / Intel) et propose le profil d'overclock idéal — le « sweet spot » entre performance et stabilité (offsets cœur/mémoire, limite de puissance, courbe de ventilation, étapes pas à pas + test de stabilité). Profil copiable en un clic.
- 🛡️ **Windows Debloat** (Optimisation) : nouvel onglet pour désactiver la télémétrie Microsoft, renforcer la confidentialité (ID de publicité, historique d'activité, localisation), alléger l'interface (Cortana, recherche Web, suggestions/pubs) et retirer le bloatware préinstallé. Chaque action est cochée par l'utilisateur avant exécution.

### Correctifs
- 🧽 **Nettoyage** : l'analyse scanne désormais **toutes** les catégories — chaque ligne affiche sa taille réelle, qu'elle soit cochée ou non. Les cases ne contrôlent que ce qui sera nettoyé. Fini les « — » sur Corbeille, Cache des navigateurs, Journaux, Prefetch.
- 🗑️ **Corbeille** : correction de la détection (HRESULT `S_FALSE` sur certaines configs Windows 11) avec repli robuste qui mesure directement les dossiers `$Recycle.Bin` — la vraie taille et le nombre d'éléments s'affichent.

### Améliorations
- 🔐 **Administrateur par défaut** : l'application démarre directement avec les droits administrateur (une seule invite UAC), pour que toutes les fonctions système marchent sans relance.
- 🔄 **Mises à jour** : vérification automatique et discrète au démarrage (ne dérange que si une mise à jour est disponible).
- 🧩 **Pilotes** : interface repensée et centrée sur l'essentiel — on ne liste plus les pilotes installés ; on recherche et on installe les mises à jour de pilotes (Windows Update) en un clic, avec un bouton « Tout installer ».

---

## v0.3

### Nouveautés
- 🛡️ **Bloqueur de publicités** : nouvel onglet AdBlock système bloquant ~130 000 domaines via le fichier hosts Windows (comme AdGuard Home, sans proxy). Fonctionne pour tous les navigateurs et toutes les applications. Nécessite des droits administrateur. Désactivation instantanée et réversible.
- 🌐 **Cache navigateurs** : détection dynamique de tous les profils Chrome, Edge, Brave, Vivaldi, Opera et Opera GX (profils multiples « Profile N » inclus) — correction de l'affichage « 0 o ».

---

## v0.2.7

### Améliorations
- 📋 **Notes de version** : historique complet de toutes les versions intégré dans l'application (menu CleanSlate ▾ → Notes de mise à jour).

---

## v0.2.6

### Correctifs
- 🔍 **Analyse complète** : le bouton « Analyser » scanne désormais **toutes** les catégories, même les non cochées — chaque ligne affiche sa taille réelle. La sélection contrôle uniquement ce qui sera nettoyé.
- 🗑️ **Corbeille** : détection améliorée via le dossier `$Recycle.Bin\{SID}` de l'utilisateur courant — plus fiable sur les configs Windows 11 avec plusieurs comptes.

---

## v0.2.5

### Correctifs
- 🗑️ **Corbeille** : correction de l'affichage « — » même quand des fichiers sont présents (HRESULT S_FALSE sur certaines configs Windows 11).

### Améliorations
- 🎮 **Mode Jeu** : liste étendue de 6 à 22 processus d'arrière-plan suspendus — stockage cloud (OneDrive, Dropbox, Google Drive), communication (Slack, Teams, Zoom, Telegram, WhatsApp), lanceurs de jeux inactifs (Epic, GOG, Ubisoft Connect, EA App), suite Adobe, apps Windows inutiles. Discord reste actif (vocal gaming).

---

## v0.2

- 🌙/☀️ Thème sombre et clair basculable à chaud
- 📊 Barre de progression avec % lors de l'analyse
- 💾 Vue des lecteurs disponibles avec espace libre
- 🚀 Optimisation RAM avancée (purge Standby List, comme Wise Memory Optimizer)
- 🧩 Mises à jour de pilotes via Windows Update (WUApi)
- 🛠️ Onglet « Réparation rapide » : diagnostic en 6 points + corrections auto
- 🔄 Vérificateur de mises à jour intégré (téléchargement + installation depuis GitHub)
- Menu « CleanSlate ▾ » : À propos, Notes de version, Vérifier les mises à jour

---

## v0.1

- Première version publique
- Cinq modules : nettoyage de fichiers, surveillance mémoire, inventaire des pilotes, Mode Jeu, optimisation système
- Sauvegarde obligatoire avant toute modification du registre

---
