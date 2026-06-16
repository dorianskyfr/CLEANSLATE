## v1.3.5

### Nouveautés
- 🎮 **Hydra Launcher — bibliothèque de jeux intégrée** : un nouvel onglet **« 🎮 Bibliothèque »** dans le Mode Jeu liste tous vos jeux installés (Steam, Epic Games, Xbox Game Pass), affiche leurs jaquettes et permet de **lancer n'importe quel jeu directement** depuis CleanSlate. Même infrastructure de scan que le DLSS Enabler — aucune configuration supplémentaire.

### Corrections
- 🐛 **VRAM réelle sur les cartes ≥ 4 Go** : la VRAM affichée dans l'onglet Overclocking était erronée sur toutes les cartes de 8 Go et plus (ex. RTX 3070, 3080, 4080…). WMI `AdapterRAM` est une valeur 32 bits qui sature à 4 294 967 295 octets (~4 Go) pour tout GPU ayant au moins 4 Go de VRAM. CleanSlate détecte désormais ce dépassement et lit la vraie VRAM 64 bits depuis le registre Windows (`HardwareInformation.qwMemorySize`).

---

## v1.3.0

### Nouveautés
- 🧩 **Mode Jeu : suspension d'applications repensée** : fini le champ de texte — la liste des applications à suspendre est maintenant une **liste à cocher, groupée par catégorie** (stockage cloud, lanceurs de jeux, messageries, accès distant, RGB, navigateurs…), avec une **note explicative** sur chaque app. Trois **profils prêts à l'emploi** : **🍃 Léger** (synchro cloud / mises à jour seulement), **⚖️ Équilibré** (recommandé : + messageries, lecteurs, lanceurs, assistants) et **🔥 Agressif** (+ navigateurs et logiciels RGB, gain maximal). Votre sélection est mémorisée d'une session à l'autre. Discord n'est jamais touché (vocal possible).
- 🎛️ **Overclocking : import depuis MSI Afterburner** : un bouton **« 📥 Importer depuis MSI Afterburner »** reprend l'offset cœur/mémoire (et la limite de puissance/température) de votre profil Afterburner existant directement dans le mode « Personnalisé » de CleanSlate — les valeurs sont ajustées aux bornes sûres de votre carte si besoin.

### Corrections
- 🐛 **DLSS Enabler : « Unreal Engine » n'est plus listé comme un jeu** : l'entrée du moteur Unreal Engine (et l'éditeur, les composants UE_…) du launcher Epic Games est désormais filtrée — seuls les vrais jeux apparaissent.
- 🖼️ **Plus de jaquettes trouvées** : le nom des jeux est nettoyé avant la recherche d'image (suppression de « Launcher », des symboles ™/®, des mentions d'édition « Bedrock/Java Edition », « Deluxe »…), ce qui permet de retrouver la jaquette de beaucoup plus de jeux hors Steam (Epic, Game Pass). *(Note : certains jeux comme Minecraft ne sont vendus sur aucune boutique PC indexée — la tuile garde alors son visuel de repli.)*

---

## v1.2.7

### Corrections
- 🐛 **DLSS Enabler — compatibilité : faux négatifs corrigés** : la vérification de compatibilité ajoutée en v1.2.6 ne cherchait les composants DLSS/Streamline/FSR/XeSS que jusqu'à 4 sous-dossiers de profondeur, alors que les jeux Unreal Engine 5 récents (ex. Subnautica 2) les rangent typiquement sous `Plugins\StreamlineCore\Binaries\ThirdParty\Win64`, soit 6 niveaux. La recherche va maintenant jusqu'à 8 niveaux (en ignorant les gros dossiers de contenu cuit comme `Content`/`Paks`/`Movies` pour rester rapide), et reconnaît aussi `sl.reflex.dll`, `sl.pcl.dll` et `sl.nis.dll` comme indices Streamline supplémentaires.
- 📝 **Message « improbable » reformulé** : quand aucun composant n'est trouvé, CleanSlate précise désormais que cette vérification automatique a ses limites et n'est pas une certitude, plutôt que d'affirmer que le jeu « ne supporte probablement pas le DLSS ». L'installation reste possible, sans risque et réversible.

---

## v1.2.6

### Nouveautés
- 🧠 **DLSS Enabler : vérification de compatibilité** : avant d'installer, CleanSlate analyse le jeu et indique s'il **supporte vraiment le DLSS**. Le mod ne fait qu'*activer* le DLSS/Frame Generation là où les composants existent déjà (`nvngx_dlss.dll`, Streamline, FSR3/XeSS) — il n'ajoute pas le DLSS à un jeu qui n'en a pas. Un jeu sans aucun upscaler est désormais signalé (🟢 compatible / 🟡 possible via FSR-XeSS / ⚠️ improbable), avec un avertissement clair à l'installation pour éviter d'installer le mod « pour rien ».
- 🎮 **Game Pass : fini les faux jeux** : les dossiers de service du dossier `XboxGames` (comme **« GameSave »**, qui n'est pas un jeu) ne sont plus listés. Un dossier n'est considéré comme un jeu que s'il contient un `Content` avec un manifeste Xbox ou un exécutable.
- 🖼️ **Jaquettes pour tous** : les jeux sans image (Epic, Game Pass, dossiers manuels) récupèrent maintenant leur jaquette via une recherche par nom sur Steam — beaucoup plus de jeux affichent leur visuel (best-effort, en tâche de fond).

### Améliorations
- 🎯 **DLSS Enabler — Multi Frame Generation jusqu'à x6** : prise en charge du **MFG x5 et x6** introduit par DLSS 4.5 (DLSS Enabler 4.5.3+, sur n'importe quel GPU DX12). Après installation, le message explique comment choisir le multiplicateur (x2 à x6) dans l'overlay du mod, et précise les conditions du x5/x6 : fichiers **Streamline 2.11+** dans le jeu et un framerate de base d'au moins ~40-50 FPS pour rester fluide.
- ✅ Vérifications : tout le pipeline (détection, compatibilité, installation à côté de l'exe, désinstallation) est couvert par de nouveaux tests automatisés.

---

## v1.2.5

### Nouveautés
- 🎛️ **Overclocking : mode « Personnalisé »** : en plus des profils prédéfinis (Sûr / Équilibré / Performance), un nouveau profil **« Personnalisé »** permet de **fixer soi-même** l'offset cœur, l'offset mémoire, la limite de puissance et la température cible, via des **curseurs bornés** (intervalles sûrs adaptés à votre marque de carte — NVIDIA, AMD ou générique). Sur les cartes NVIDIA et AMD dédiées, vos valeurs sont **appliquées directement** par CleanSlate (NVAPI / ADL), avec le bouton « Reset » pour tout annuler ; sur les autres, le profil personnalisé se copie pour être reporté dans l'outil du constructeur. Les bornes empêchent les réglages dangereux tout en laissant la main à l'utilisateur (un léger *underclock* est aussi possible).

---

## v1.2.0

### Nouveautés
- 🎯 **DLSS Enabler : installation à côté de l'exécutable** : le proxy DLL doit être chargé par l'exe du jeu — CleanSlate **localise désormais automatiquement l'exécutable principal** (y compris dans les sous-dossiers type `Binaries\Win64` d'Unreal Engine, `bin\x64`, ou le dossier `Content` des jeux Game Pass) et pose le DLL à côté, en ignorant les utilitaires (installateurs, anticheats, crash handlers). La détection et la désinstallation inspectent aussi ces sous-dossiers.
- 🎮 **DLSS Enabler : vraie méthode Xbox Game Pass** : les dossiers des jeux Game Pass sont verrouillés par Windows. CleanSlate **teste l'écriture avant d'installer**, tente un **déverrouillage automatique** du dossier (icacls, si lancé en administrateur), et sinon affiche la marche à suivre exacte (activer les fonctionnalités de modding dans l'app Xbox, ou relancer CleanSlate en admin). Fini l'échec silencieux.
- 🛡️ **Bloqueur de pub : choix du fournisseur DNS** : AdGuard DNS (pubs + traqueurs, recommandé), AdGuard Family (+ contrôle parental), Cloudflare Security ou Quad9 (domaines malveillants uniquement). Le DNS d'origine reste sauvegardé et restauré à la désactivation — y compris les sauvegardes faites par les versions précédentes.
- 🚀 **RAM : optimisation automatique** : nouvelle option « Optimiser automatiquement quand la RAM dépasse 90 % » (au plus une fois toutes les 10 minutes), persistée entre les sessions.
- 🧹 **Nettoyage : 2 nouvelles catégories** : **Cache des shaders DirectX/GPU** (D3DSCache, NVIDIA, AMD — souvent plusieurs Go, régénéré par les jeux) et **Rapports d'erreurs Windows** (WER). Avec descriptions honnêtes des contreparties.
- 🎮 **Mode Jeu : vos applications à suspendre** : un champ permet d'ajouter vos propres processus (ex. `firefox, obs64`) à la liste blanche de suspension, mémorisés d'une session à l'autre.

### Améliorations
- 🏠 **Accueil** : conseil de redémarrage affiché quand le PC est allumé depuis plus de 7 jours sans reboot complet.
- 🎯 **DLSS Enabler** : le bilan d'installation indique précisément le fichier posé (`winmm.dll`, plugin ASI…) et le dossier exact.

---

## v1.1.5

### Nouveautés
- 📦 **DLSS Enabler : DLL officiel intégré à CleanSlate** : le mod n'est plus téléchargé depuis GitHub à l'installation (le repo GitHub du projet étant souvent indisponible/instable) — le DLL officiel **DLSS Enabler 4.7.8.1** est désormais embarqué directement dans CleanSlate. L'installation copie ce fichier dans le dossier du jeu sous le **nom de proxy le plus sûr** (`winmm.dll`, `dbghelp.dll`, `version.dll` ou `dxgi.dll`, selon le premier nom libre — ou réutilisé s'il s'agit déjà d'une installation précédente de DLSS Enabler), et bascule automatiquement sur la **variante plugin ASI** (`plugins/dlss-enabler.asi`) si les quatre noms sont déjà occupés par un autre mod (ReShade, Special K…). Comme avant, aucun fichier d'un autre mod n'est jamais écrasé ou supprimé sans vérification de ses métadonnées.
- 🎮 **Détection des jeux Xbox Game Pass** : la bibliothèque visuelle détecte désormais aussi les jeux installés via l'app Xbox (dossier `XboxGames` à la racine de chaque disque), avec un badge **« 🎮 Game Pass »** sur la jaquette. Un avertissement s'affiche pour ces jeux : Windows peut supprimer les DLL ajoutées lors d'une vérification d'intégrité du package ou d'une mise à jour — l'installation reste possible mais sa persistance n'est pas garantie.

---

## v1.1.0

### Nouveautés
- 🖼️ **DLSS Enabler : bibliothèque visuelle** : fini le menu déroulant — les jeux détectés s'affichent désormais en **grille de jaquettes** (artwork vertical officiel Steam, récupéré du cache local de Steam ou du CDN officiel ; tuile élégante avec l'initiale du jeu pour Epic Games et les dossiers manuels). Chaque jaquette porte un **badge « ✓ DLSS Enabler »** quand le mod est installé dans ce jeu — l'état de toute la bibliothèque se voit d'un coup d'œil. Un clic sur une jaquette sélectionne le jeu.
- 🔎 **DLSS Enabler : recherche et scan automatique** : un champ « Rechercher un jeu… » filtre la grille en direct, et la détection des jeux se lance **automatiquement** à l'arrivée sur le Mode Jeu (plus besoin de cliquer). Les dossiers de jeux ajoutés à la main sont **mémorisés** d'une session à l'autre.

### Améliorations
- 🚀 **Optimisation RAM en un clic** : le bouton « Optimiser la RAM » agit désormais **immédiatement**, sans fenêtre de confirmation — le résultat s'affiche directement dans la page. Le texte d'explication a été réécrit : l'optimisation (compactage des working sets + purge de la Standby List) fonctionne très bien, y compris sur Windows 11.

---

## v1.0.0

### Nouveautés
- 🏠 **Onglet « Accueil »** : CleanSlate s'ouvre désormais sur un tableau de bord — vue d'ensemble du système (édition de Windows, processeur, carte(s) graphique(s), RAM installée, disques avec espace libre, durée d'allumage) et bouton **« Entretien en 1 clic »** qui enchaîne le nettoyage des seules catégories **sûres** (fichiers temporaires, miniatures…) et l'optimisation de la RAM, avec un bilan détaillé. La corbeille, le cache des navigateurs et les actions sensibles ne sont jamais touchés par l'entretien automatique : ils restent un choix explicite dans l'onglet Nettoyage.
- 🎯 **Mode Jeu → « DLSS Enabler »** : nouveau sous-onglet qui gère le mod open-source [DLSS Enabler](https://github.com/artur-graniszewski/DLSS-Enabler) (aussi distribué sur [Nexus Mods](https://www.nexusmods.com/site/mods/757)) à la manière de DLSS Enabler Manager : détection automatique des jeux installés (bibliothèques **Steam** et **Epic Games**, ou dossier choisi à la main), détection de la présence du mod, **installation en un clic** (téléchargement de l'installateur officiel depuis GitHub puis installation silencieuse dans le dossier du jeu) et **désinstallation propre** (les DLL d'autres mods comme ReShade sont préservées — vérification des métadonnées avant toute suppression). Le mod simule DLSS Super Resolution et Frame Generation — y compris le **Multi Frame Generation** (x2/x3/x4, façon DLSS 4) — sur n'importe quel GPU DirectX 12 dans les jeux compatibles DLSS2/DLSS3. ⚠️ Réservé aux jeux **solo** : ne l'utilisez jamais dans un jeu multijoueur protégé par un anticheat.
- 💾 **Préférences mémorisées** : le thème (sombre/clair) et la taille/l'état de la fenêtre sont désormais conservés d'une session à l'autre.

---

## v0.9.4

### Nouveautés
- 🎮 **Overclocking automatique (AMD)** : sur les cartes Radeon dédiées (Polaris, Vega, RDNA/RDNA2 et au-delà), l'onglet Overclocking applique désormais l'overclock **directement**, comme pour NVIDIA — fréquences cœur/mémoire et limite de puissance posées via **AMD ADL (OverdriveN)**, avec les boutons « Appliquer l'overclock » et « Reset » (retour aux valeurs d'usine). Si OverdriveN n'est pas pris en charge par le pilote/la carte, l'application échoue proprement et le profil guidé reste disponible. Intel reste en profil guidé (pas d'API constructeur fiable disponible).
- 🔔 **Notification de mise à jour persistante** : si une mise à jour est détectée mais pas installée tout de suite, la notification « Mise à jour vX.Y.Z disponible » reste affichée — y compris après avoir fermé puis relancé CleanSlate — jusqu'à ce que la mise à jour soit installée.

---

## v0.9.3

### Nouveautés
- 🔎 **Pilotes GPU à jour** : dans l'onglet Overclocking, le bouton « Vérifier le dernier pilote disponible » interroge **directement le fabricant** (NVIDIA via son catalogue officiel pour les cartes GeForce/RTX/GTX) pour comparer votre version installée à la toute dernière disponible — au-delà du catalogue Windows Update, souvent en retard de plusieurs semaines/mois sur les pilotes « Game Ready ». Affiche la version, la date de sortie, la taille, et propose le téléchargement direct. Pour AMD et Intel (pas d'API par modèle, pilotes unifiés par génération), un lien direct vers l'outil de détection officiel du fabricant est proposé.

### Correctifs
- 🛡️ **Bloqueur de pub remplacé (DNS)** : l'ancien bloqueur basé sur le fichier hosts (~130 000 entrées) ralentissait fortement le PC et ne pouvait être désactivé qu'en Mode sans échec. Il est **entièrement remplacé** par une bascule du DNS système vers **AdGuard DNS** (`94.140.14.14` / `94.140.15.15`) : aussi efficace, instantané, sans impact sur les performances, et désactivable en un clic (le DNS d'origine est sauvegardé puis restauré). Au démarrage, CleanSlate nettoie automatiquement l'ancien blocage hosts s'il est encore présent.
- 🔄 **Mises à jour persistantes** : après une mise à jour, CleanSlate remplace désormais l'exécutable existant par la nouvelle version (au lieu de lancer une copie temporaire), puis relance l'application depuis cet emplacement. Le raccourci/épingle reste donc à jour.

### Améliorations
- 🎮 **Overclocking : plusieurs profils** : chaque carte propose désormais 3 profils — **Sûr**, **Équilibré** (recommandé) et **Performance** — sélectionnables dans l'onglet Overclocking.
- 🎮 **Overclocking : Intel Iris Xe / Arc Graphics reconnus** : les iGPU Intel récents (Iris Xe, Arc Graphics intégré) reçoivent désormais de vrais profils « GPU Performance Boost » au lieu du message générique « aucun overclock recommandé » réservé aux anciens UHD/HD Graphics.
- 🎮 **Overclocking : écrans virtuels filtrés** : les adaptateurs d'affichage virtuels (ex. Parsec Virtual Display, spacedesk, IDD) n'apparaissent plus dans la liste des cartes graphiques.
- 🎮 **Overclocking : bouton MSI Afterburner retiré** — CleanSlate applique lui-même l'overclock sur les cartes NVIDIA compatibles (NVAPI).

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
