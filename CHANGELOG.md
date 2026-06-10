## v0.9.3

### Correctifs
- 🛡️ **Bloqueur de pub remplacé (DNS)** : l'ancien bloqueur basé sur le fichier hosts (~130 000 entrées) ralentissait fortement le PC et ne pouvait être désactivé qu'en Mode sans échec. Il est **entièrement remplacé** par une bascule du DNS système vers **AdGuard DNS** (`94.140.14.14` / `94.140.15.15`) : aussi efficace, instantané, sans impact sur les performances, et désactivable en un clic (le DNS d'origine est sauvegardé puis restauré). Au démarrage, CleanSlate nettoie automatiquement l'ancien blocage hosts s'il est encore présent.
- 🔄 **Mises à jour persistantes** : après une mise à jour, CleanSlate remplace désormais l'exécutable existant par la nouvelle version (au lieu de lancer une copie temporaire), puis relance l'application depuis cet emplacement. Le raccourci/épingle reste donc à jour.

### Améliorations
- 🎮 **Overclocking : plusieurs profils** : chaque carte propose désormais 3 profils — **Sûr**, **Équilibré** (recommandé) et **Performance** — sélectionnables dans l'onglet Overclocking.
- 🎮 **Overclocking : Intel Iris Xe / Arc Graphics reconnus** : les iGPU Intel récents (Iris Xe, Arc Graphics intégré) reçoivent désormais de vrais profils « GPU Performance Boost » au lieu du message générique « aucun overclock recommandé » réservé aux anciens UHD/HD Graphics.
- 🎮 **Overclocking : écrans virtuels filtrés** : les adaptateurs d'affichage virtuels (ex. Parsec Virtual Display, spacedesk, IDD) n'apparaissent plus dans la liste des cartes graphiques.
- 🎮 **Overclocking : bouton MSI Afterburner retiré** — CleanSlate applique lui-même l'overclock sur les cartes NVIDIA compatibles (NVAPI).

---

## v0.9.2

### Nouveautés
- 🔎 **Pilotes GPU à jour** : dans l'onglet Overclocking, le bouton « Vérifier le dernier pilote disponible » interroge **directement le fabricant** (NVIDIA via son catalogue officiel pour les cartes GeForce/RTX/GTX) pour comparer votre version installée à la toute dernière disponible — au-delà du catalogue Windows Update, souvent en retard de plusieurs semaines/mois sur les pilotes « Game Ready ». Affiche la version, la date de sortie, la taille, et propose le téléchargement direct. Pour AMD et Intel (pas d'API par modèle, pilotes unifiés par génération), un lien direct vers l'outil de détection officiel du fabricant est proposé.

---

## v0.9.1

### Nouveautés
- 🚀 **Overclocking automatique** (NVIDIA) : l'onglet Overclocking applique désormais l'overclock **directement** sur les cartes NVIDIA dédiées — offsets de fréquence cœur et mémoire posés via NVAPI (l'API officielle NVIDIA), avec un bouton **« Appliquer l'overclock »** et un bouton **« Reset »** qui remet tout à zéro. Les offsets ne survivent pas à un redémarrage (sécurité). Les cartes AMD/Intel conservent le profil guidé à appliquer manuellement (ADL/IGCL non encore implémentés). Fonctionnalité expérimentale : NVAPI valide chaque structure, donc une incompatibilité échoue proprement sans rien modifier.

---

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
