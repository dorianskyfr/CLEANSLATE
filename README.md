# CleanSlate

**CleanSlate** est un utilitaire open source d'optimisation et de nettoyage pour **Windows 10/11 (64 bits)**, écrit en **C# / .NET 8 + WPF**. Interface entièrement en français, code modulaire et commenté.

> **Philosophie : honnêteté technique.**
> Beaucoup d'utilitaires « boost PC » promettent des gains spectaculaires qui n'existent pas.
> CleanSlate fait l'inverse : chaque fonctionnalité est accompagnée d'une explication claire
> de **ce qu'elle fait réellement** et de **ses limites honnêtes**.

---

## Téléchargement

**[Dernière Release — CleanSlate.exe](https://github.com/dorianskyfr/CLEANSLATE/releases/latest)**

Exécutable **unique et autonome** (~70 Mo) — runtime .NET 8 + WPF inclus. Aucune installation requise, double-cliquez simplement.

> Windows SmartScreen peut afficher un avertissement au premier lancement (exécutable non signé).
> Cliquez sur « Informations complémentaires » → « Exécuter quand même ».

---

## Fonctionnalités

### Module 1 — Nettoyage de fichiers temporaires
Analyse et supprime les fichiers inutiles accumulés par Windows et les navigateurs :
- `%TEMP%` et `%WINDIR%\Temp`
- Caches de navigateurs (Chrome, Edge, Firefox, Opera, Brave)
- Cache des miniatures Windows
- Corbeille (via l'API shell `SHEmptyRecycleBin`)
- Journaux Windows (`%WINDIR%\Logs`)
- Prefetch (`%WINDIR%\Prefetch`)

**Fonctionnement en deux phases** : scan préalable (rien n'est supprimé) + confirmation explicite avec décompte des fichiers et de l'espace récupérable.

### Module 2 — Inventaire des pilotes
Inventaire complet des pilotes installés via WMI (`Win32_PnPSignedDriver`) :
- Nom du périphérique, version, date, fabricant, classe
- Raccourci vers Windows Update pour chercher des mises à jour

> **Limite honnête** : il n'existe aucune API universelle et gratuite donnant « la dernière version officielle » d'un pilote. CleanSlate inventorie et délègue à Windows Update — pas de fausse comparaison inventée.

### Module 3 — Surveillance et libération de RAM
- Lecture en temps réel de la mémoire utilisée / disponible (rafraîchissement automatique)
- Libération possible via `EmptyWorkingSet` (P/Invoke `psapi.dll`)

> **Limite honnête** : sur Windows moderne, « vider la RAM » est généralement inutile voire contre-productif (le cache mémoire est bénéfique). La fonction est fournie avec un avertissement explicite.

### Module 4 — Mode Jeu
Optimisation temporaire des ressources pour les sessions de jeu :
- Suspension réversible de processus non essentiels (P/Invoke `NtSuspendProcess` / `NtResumeProcess`)
- Arrêt / redémarrage de services sélectionnés
- Snapshot JSON persistant pour restauration automatique après fermeture brutale
- Restauration complète en un clic

> **Limite honnête** : les gains dépendent fortement de la machine. CleanSlate se limite à une liste sûre et restaure systématiquement l'état initial.

### Module 5 — Optimisation système
#### 5a — Gestion du démarrage
- Liste les programmes au démarrage (clés `Run` HKCU + HKLM + dossier Startup)
- Activation / désactivation **réversible** : les entrées sont déplacées dans une clé de sauvegarde CleanSlate, jamais supprimées définitivement
- Restauration en un clic

#### 5b — Registre conservateur
- Analyse les entrées orphelines du registre
- **Sauvegarde `.reg` obligatoire** avant toute modification (export `reg.exe`)
- Restauration en un clic via `reg.exe import`

> **Limite honnête** : le gain de performance du nettoyage de registre est quasi nul en pratique. La fonction existe mais la sauvegarde est imposée et l'avertissement est affiché.

### Modules complémentaires

Au-delà des cinq modules historiques, CleanSlate intègre :

- **Accueil / tableau de bord** — vue d'ensemble du système (Windows, CPU, GPU, RAM, disques, uptime), **« Entretien en 1 clic »** (nettoyage des catégories sûres + optimisation RAM) et **entretien automatique programmé** (récurrent, en arrière-plan, désactivé par défaut).
- **Analyseur d'espace disque** — scanne un lecteur/dossier et liste ses plus gros sous-dossiers et fichiers (lecture seule ; ouvre l'emplacement dans l'Explorateur).
- **Overclocking** — détection GPU et profils d'overclock, avec application directe sur NVIDIA (NVAPI) et AMD (ADL), import MSI Afterburner, et vérification du dernier pilote auprès du fabricant.
- **DLSS Enabler** — installe/désinstalle le mod open-source DLSS Enabler par jeu (Steam / Epic / Game Pass), réversible. Réservé aux jeux solo.
- **Windows Debloat** — désactivation de la télémétrie/confidentialité et retrait du bloatware préinstallé, chaque action cochée par l'utilisateur. **Réversible** : l'état d'origine (registre, services, tâches) est sauvegardé avant modification, avec un bouton « Tout restaurer ».
- **Bloqueur de pub (DNS)** — bascule du DNS système vers un fournisseur filtrant (AdGuard, Cloudflare, Quad9), réversible (DNS d'origine sauvegardé/restauré).
- **Réparation rapide** — diagnostic système en plusieurs points avec corrections automatiques.
- **Mises à jour intégrées** — vérification discrète au démarrage et installation depuis GitHub.

---

## Architecture

```
CleanSlate/
├── CleanSlate.sln
├── docs/
│   ├── ARCHITECTURE.md          # Diagrammes de flux et couches
│   └── LIMITES-TECHNIQUES.md    # Limites honnêtes par fonctionnalité
├── src/
│   ├── CleanSlate.Core/         # Logique métier — zéro dépendance UI
│   │   ├── Abstractions/        # Interfaces (ICleaningProvider, IActionLogger…)
│   │   ├── Models/              # CleanableItem, ScanResult, CleanResult…
│   │   ├── Cleaning/            # Module 1 : providers + orchestrateur
│   │   │   ├── FileCleaningProviderBase.cs   # Base + liste blanche de sécurité
│   │   │   ├── TempFilesProvider.cs
│   │   │   ├── BrowserCacheProvider.cs
│   │   │   ├── WindowsArtifactsProvider.cs   # Logs, prefetch, miniatures
│   │   │   ├── RecycleBinProvider.cs          # API shell (SHEmptyRecycleBin)
│   │   │   └── CleaningEngine.cs              # Orchestration scan / clean
│   │   ├── Modules/
│   │   │   ├── DriverInventory.cs    # Module 2 : WMI Win32_PnPSignedDriver
│   │   │   ├── MemoryMonitor.cs      # Module 3 : GlobalMemoryStatusEx + EmptyWorkingSet
│   │   │   ├── GameModeService.cs    # Module 4 : suspend/resume + snapshot JSON
│   │   │   ├── StartupManager.cs     # Module 5a : clés Run + dossier Startup
│   │   │   ├── RegistryCleaner.cs    # Module 5b : scan orphelins + fix avec backup
│   │   │   └── SystemOptimization.cs # Interfaces + records partagés
│   │   ├── Native/
│   │   │   └── NativeMethods.cs      # P/Invoke (shell32, kernel32, psapi, ntdll)
│   │   └── Diagnostics/
│   │       └── FileActionLogger.cs   # Logs dans %LOCALAPPDATA%\CleanSlate\logs
│   └── CleanSlate.App/          # Couche présentation WPF (MVVM)
│       ├── app.manifest         # UAC : asInvoker + relance admin si nécessaire
│       ├── App.xaml(.cs)        # Composition root : instanciation des services
│       ├── MainWindow.xaml(.cs)
│       ├── Views/               # Vues XAML par module
│       ├── ViewModels/          # Un ViewModel par module + MainViewModel
│       └── Infrastructure/      # ObservableObject, RelayCommand, DialogService
└── tests/
    └── CleanSlate.Core.Tests/   # Tests unitaires et d'intégration légers
        ├── CleaningFlowTests.cs       # Flux scan → clean sur dossier temporaire réel
        ├── SafetyTests.cs             # Liste blanche IsPathSafeToDelete
        ├── RegistryCleanerTests.cs    # Extraction chemin + détection d'orphelins
        ├── UpdateServiceTests.cs      # Persistance + comparaison de versions
        ├── GpuDriverCheckerTests.cs   # Conversion/comparaison de versions NVIDIA
        ├── DiskAnalyzerTests.cs       # Analyse d'espace disque (tailles, tri, topN)
        ├── MaintenanceSchedulerTests.cs # Déclenchement de l'entretien automatique
        ├── CleaningProvidersTests.cs  # Intégrité des providers de nettoyage
        ├── WindowsDebloatTests.cs     # Catalogue + réversibilité du debloat
        ├── FileActionLoggerTests.cs   # Formatage des tailles
        ├── AdBlockServiceTests.cs     # Parsing/sérialisation des sauvegardes DNS
        ├── AppSettingsServiceTests.cs # Persistance des préférences
        ├── DlssEnablerTests.cs        # Détection/installation du mod
        ├── GameModeExtrasTests.cs     # Catalogue de suspension + options
        ├── MaintenanceServiceTests.cs # Entretien en 1 clic
        ├── OverclockingAdvisorTests.cs # Profils d'overclock
        └── TestSupport.cs             # Helpers (NullLogger, TestableFileProvider)
```

### Principes de conception

| Couche | Règle |
|--------|-------|
| `CleanSlate.Core` | Zéro dépendance WPF. Testable en isolation. |
| `CleanSlate.App` | Pur MVVM. Les ViewModels ne touchent pas le système de fichiers directement. |
| Interfaces | Chaque module expose une interface (`ICleaningProvider`, `IMemoryMonitor`…) pour la testabilité. |
| Deux phases | Tout nettoyage : `ScanAsync` (lecture seule) → confirmation → `CleanAsync`. |

---

## Compiler et lancer

> **Prérequis :** Windows 10/11 64 bits + [SDK .NET 8](https://dotnet.microsoft.com/download) (`dotnet --version` ≥ 8.0)

```powershell
# Cloner le dépôt
git clone https://github.com/dorianskyfr/CLEANSLATE.git
cd CLEANSLATE

# Compiler
dotnet build CleanSlate.sln -c Release

# Lancer l'application
dotnet run --project src/CleanSlate.App

# Tests unitaires
dotnet test CleanSlate.sln --verbosity normal

# Produire un exécutable unique autonome
dotnet publish src/CleanSlate.App -c Release -p:PublishProfile=SingleFile
# → src/CleanSlate.App/bin/Release/net8.0-windows/win-x64/publish/CleanSlate.exe
```

### Exécutable unique autonome

Le profil `SingleFile.pubxml` produit **un seul fichier** embarquant le runtime .NET 8 + WPF :

| Réglage | Valeur | Raison |
|---------|--------|--------|
| `SelfContained` | `true` | Aucun prérequis sur le PC cible |
| `PublishSingleFile` | `true` | Tout dans un seul `.exe` |
| `EnableCompressionInSingleFile` | `true` | ~150 Mo → ~70 Mo |
| `PublishReadyToRun` | `true` | Démarrage plus rapide |
| `PublishTrimmed` | `false` | Le trimming casse WPF (XAML par réflexion) |

---

## CI / CD

Le fichier [`.github/workflows/build.yml`](.github/workflows/build.yml) tourne sur `windows-latest` à chaque push et pull request :

1. Restauration des dépendances
2. Compilation Release
3. Tests unitaires
4. Publication en `.exe` autonome
5. Upload de l'artefact `CleanSlate.exe`
6. Création d'une Release GitHub (sur tag `v*` ou déclenchement manuel)

---

## Sécurité

CleanSlate applique le principe **« rien d'irréversible sans confirmation »** :

1. **Aperçu obligatoire** — le scan liste les éléments et l'espace récupérable, sans rien supprimer.
2. **Confirmation explicite** — la suppression ne démarre qu'après validation, avec décompte affiché.
3. **Listes blanches** — `FileCleaningProviderBase.IsPathSafeToDelete` refuse catégoriquement tout chemin racine, `System32`, `Program Files`, profil utilisateur entier, etc.
4. **Sauvegarde obligatoire** — aucune modification du registre sans export `.reg` préalable + restauration en un clic.
5. **Désactivation réversible** — les entrées de démarrage ne sont jamais supprimées, seulement déplacées (clé de backup ou sous-dossier).
6. **Journalisation** — chaque action est enregistrée dans `%LOCALAPPDATA%\CleanSlate\logs`.

---

## Limites techniques honnêtes

> Détails complets dans [`docs/LIMITES-TECHNIQUES.md`](docs/LIMITES-TECHNIQUES.md)

| Module | Limite |
|--------|--------|
| Pilotes | Pas d'API universelle pour « la dernière version ». Inventaire uniquement + renvoi vers Windows Update. |
| RAM | « Vider la RAM » est inutile sur Windows moderne. Fourni avec avertissement explicite. |
| Registre | Gain de performance quasi nul. Sauvegarde imposée avant toute action. |
| Mode Jeu | Gains variables selon la machine. Liste sûre uniquement, restauration systématique. |

---

## Stack technique

| Critère | C# .NET 8 + WPF (retenu) | Electron | Python + Qt |
|---------|--------------------------|----------|-------------|
| Accès Win32 / WMI / registre | Natif (BCL + P/Invoke) | via add-ons | via `pywin32`, fragile |
| Élévation UAC | Intégré (manifest) | Contournements | Relances manuelles |
| Empreinte mémoire | Légère, natif | ~150 Mo (Chromium) | Moyenne |
| Déploiement single-file | `dotnet publish` | Bundle lourd | PyInstaller |

---

## Contribuer

Les contributions sont les bienvenues ! Quelques pistes :

- **Nouveaux providers de nettoyage** — étendre `FileCleaningProviderBase`
- **Recherche de mises à jour pilotes** — brancher `WUApiLib` (Microsoft.Update.Session) sur le module 2
- **Tâches planifiées** — lister/désactiver via le planificateur Windows dans le module 5a
- **Thème sombre** — le XAML utilise les ressources système, un thème sombre serait un plus
- **Tests** — les modules Core sont testables en isolation via leurs interfaces

```
Fork → branche feature/ma-feature → PR vers main
```

---

## Licence

Ce projet est distribué sous licence **MIT**. Voir [`LICENSE`](LICENSE).

---

## Auteur

Projet initialement créé et architecturé avec [Claude Code](https://claude.ai/code) (Anthropic).
