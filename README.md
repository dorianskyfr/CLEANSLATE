# CleanSlate

**CleanSlate** est un utilitaire d'optimisation et de nettoyage pour Windows 10/11
(64 bits), inspiré de CCleaner. Interface en français, code modulaire et commenté.

> ⚠️ **Philosophie du projet : honnêteté technique.**
> Beaucoup d'utilitaires de « boost PC » promettent des gains spectaculaires qui
> n'existent pas. CleanSlate fait l'inverse : chaque fonctionnalité est accompagnée
> d'une explication claire de **ce qu'elle fait réellement** et de **ses limites**.
> Voir [`docs/LIMITES-TECHNIQUES.md`](docs/LIMITES-TECHNIQUES.md).

## Sommaire
- [État d'avancement](#état-davancement)
- [Choix de la stack](#choix-de-la-stack-justifié)
- [Architecture](#architecture)
- [Compiler et lancer](#compiler-et-lancer)
- [Sécurité](#sécurité)
- [Limites techniques](#limites-techniques)

## État d'avancement

| Module | État | Détails |
|---|---|---|
| 1. Nettoyage fichiers temporaires | ✅ **Implémenté** | `%temp%`, cache navigateurs, logs Windows, corbeille, prefetch, miniatures. Aperçu + confirmation. |
| 2. Détection pilotes obsolètes | ✅ **Implémenté** | Inventaire WMI complet + raccourcis Windows Update / recherche constructeur. Pas de fausse « dernière version » (voir docs). |
| 3. Surveillance / libération RAM | ✅ **Implémenté** | Lecture temps réel (timer) ; libération possible avec avertissement honnête sur son inefficacité. |
| 4. Mode Jeu | ✅ **Implémenté** | Suspension/reprise réversible des processus + services, snapshot persistant pour restauration après fermeture brutale. |
| 5. Optimisation système | ✅ **Implémenté** | Démarrage (activer/désactiver réversible) + registre conservateur avec sauvegarde `.reg` **obligatoire** avant toute action. |

Les cinq modules sont implémentés derrière une interface multi-onglets. Voir la
réserve ci-dessous sur la compilation.

> ⚠️ **Compilation non vérifiée par l'auteur du commit.** Le code a été écrit et
> relu sous Linux (sans SDK .NET, et WPF ne se construit que sous Windows). La
> **CI GitHub Actions** (`.github/workflows/build.yml`, sur `windows-latest`)
> compile, teste et publie l'exécutable à chaque push — c'est elle qui fait foi.
> L'artefact `CleanSlate.exe` est téléchargeable depuis l'onglet **Actions**.

## Choix de la stack justifié

**Retenu : C# / .NET 8 + WPF (`net8.0-windows`).**

| Critère | C# .NET + WPF (retenu) | Electron | Python + Qt/Tkinter |
|---|---|---|---|
| Accès Win32 / WMI / registre | ✅ Natif (BCL + P/Invoke) | ⚠️ via add-ons natifs | ⚠️ via `pywin32`/`ctypes`, fragile |
| Élévation UAC (manifest) | ✅ Intégré | ⚠️ contournements | ⚠️ relances manuelles |
| Empreinte mémoire / démarrage | ✅ Légère, natif | ❌ ~150 Mo, Chromium | 🟡 moyenne |
| UI moderne et thématisable | ✅ XAML/MVVM | ✅ HTML/CSS | 🟡 correcte |
| Déploiement (single-file / AOT) | ✅ `dotnet publish` | ⚠️ gros bundle | ⚠️ PyInstaller |
| Adéquation « outil système Windows » | ✅✅ idéale | ❌ surdimensionné | 🟡 |

**Pourquoi WPF plutôt qu'Electron** : un nettoyeur PC est avant tout de
l'**intégration système profonde** (registre, services, WMI, API shell pour la
corbeille, gestion de processus). .NET expose tout cela nativement, sans pont
JS↔natif. Electron embarquerait un navigateur entier (≈150 Mo) pour un outil dont
99 % de la valeur est côté système — mauvais compromis.

**Pourquoi WPF plutôt que Python** : la distribution d'un binaire Windows propre,
l'élévation UAC par manifest, et l'accès typé/robuste à WMI et au registre sont
nettement plus solides en .NET.

> Alternative envisageable : **WinUI 3 / Windows App SDK** pour un look Windows 11
> plus natif. WPF reste choisi ici pour sa maturité, sa stabilité et son immense
> base documentaire.

## Architecture

```
CleanSlate/
├── CleanSlate.sln
├── docs/
│   ├── ARCHITECTURE.md          # Détail des couches et du flux de données
│   └── LIMITES-TECHNIQUES.md    # Limites honnêtes, fonctionnalité par fonctionnalité
├── src/
│   ├── CleanSlate.Core/         # Logique métier — AUCUNE dépendance UI
│   │   ├── Abstractions/        # Interfaces (ICleaningProvider, IBackupService…)
│   │   ├── Models/              # CleanableItem, ScanResult, CleanResult…
│   │   ├── Cleaning/            # Module 1 (FONCTIONNEL) : providers de nettoyage
│   │   │   ├── FileCleaningProviderBase.cs
│   │   │   ├── CleaningTarget.cs
│   │   │   ├── TempFilesProvider.cs
│   │   │   ├── BrowserCacheProvider.cs
│   │   │   ├── WindowsArtifactsProvider.cs  # logs, prefetch, miniatures
│   │   │   ├── RecycleBinProvider.cs         # API shell (SHEmptyRecycleBin)
│   │   │   └── CleaningEngine.cs             # orchestration scan/clean
│   │   ├── Modules/             # Interfaces des modules 2 à 5 (stubs documentés)
│   │   ├── Native/              # P/Invoke (NativeMethods)
│   │   └── Diagnostics/         # Journalisation des actions
│   └── CleanSlate.App/          # UI WPF (MVVM)
│       ├── app.manifest         # Élévation UAC (asInvoker + relance admin)
│       ├── App.xaml(.cs)
│       ├── MainWindow.xaml(.cs)
│       ├── Views/               # CleaningView (module 1)
│       ├── ViewModels/          # MVVM
│       └── Infrastructure/      # ObservableObject, RelayCommand
└── tests/
    └── CleanSlate.Core.Tests/   # Tests unitaires (logique de scan sécurisée)
```

Voir [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) pour les diagrammes de flux.

## Compiler et lancer

> Prérequis : **Windows 10/11 64 bits** et le **SDK .NET 8** (`dotnet --version` ≥ 8).
> Le projet cible `net8.0-windows` (WPF) : il **se compile et s'exécute uniquement
> sous Windows**.

```powershell
# Restauration et compilation
dotnet build CleanSlate.sln -c Release

# Lancement de l'application
dotnet run --project src/CleanSlate.App

# Tests unitaires (exécutables aussi en CI)
dotnet test

# Publication d'un EXÉCUTABLE UNIQUE et autonome (un seul .exe)
dotnet publish src/CleanSlate.App -c Release -p:PublishProfile=SingleFile
```

### Un seul `.exe`, rien à installer

Le profil [`SingleFile.pubxml`](src/CleanSlate.App/Properties/PublishProfiles/SingleFile.pubxml)
produit **un unique fichier** :

```
src/CleanSlate.App/bin/Release/net8.0-windows/win-x64/publish/CleanSlate.exe
```

Cet exécutable embarque **le runtime .NET 8 + WPF** : il se lance par simple
double-clic sur n'importe quel Windows 10/11 64 bits, **sans installer .NET**.

| Réglage | Choix | Pourquoi |
|---|---|---|
| `SelfContained` | `true` | Aucun prérequis sur le PC cible |
| `PublishSingleFile` | `true` | Tout dans un seul `.exe` |
| `EnableCompressionInSingleFile` | `true` | Réduit la taille (~150 Mo → ~70 Mo) |
| `PublishReadyToRun` | `true` | Démarrage plus rapide |
| `PublishTrimmed` | `false` | ⚠️ Le *trimming* casse WPF (XAML par réflexion) — fiabilité d'abord |

> Taille attendue : ~70–150 Mo. C'est le prix d'un exe **totalement autonome**
> (le runtime .NET + WPF y sont inclus). Pour un exe minuscule (~3 Mo) mais
> nécessitant le *.NET Desktop Runtime* installé sur la cible, retirer
> `SelfContained`/`PublishSingleFile` du profil.

## Sécurité

CleanSlate applique le principe **« rien d'irréversible sans confirmation »** :

1. **Aperçu obligatoire** : tout nettoyage commence par un *scan* qui liste les
   éléments et la taille récupérable. Rien n'est supprimé pendant le scan.
2. **Confirmation explicite** avant suppression, avec décompte des éléments.
3. **Listes blanches de sécurité** : les providers ne ciblent que des
   emplacements connus (`%temp%`, caches navigateurs…). Refus catégorique de
   supprimer des chemins racines, profils utilisateur complets, etc.
   (voir `FileCleaningProviderBase.IsPathSafeToDelete`).
4. **Sauvegarde obligatoire** pour les actions risquées (registre) : aucune
   modification de registre sans export `.reg` préalable + restauration en un clic.
5. **Journalisation** de chaque action dans `%LOCALAPPDATA%\CleanSlate\logs`.

## Limites techniques

Résumé (détails dans [`docs/LIMITES-TECHNIQUES.md`](docs/LIMITES-TECHNIQUES.md)) :

- **Pilotes obsolètes** : *il n'existe pas d'API universelle et gratuite* donnant
  « la dernière version du pilote X ». CleanSlate peut **inventorier** les pilotes
  et déléguer la recherche de mises à jour à **Windows Update** ; comparer aux
  versions constructeurs reste partiel et non garanti.
- **Libération de RAM** : sur Windows moderne, « vider la RAM » est généralement
  **inutile voire contre-productif** (le cache mémoire est bénéfique). La fonction
  est fournie à titre de mesure/diagnostic, avec avertissement honnête.
- **Nettoyage du registre** : le gain de performance est **quasi nul** en pratique ;
  le risque existe. D'où la **sauvegarde obligatoire**.
- **Mode Jeu** : les gains dépendent fortement de la machine ; suspendre des
  processus système peut nuire. CleanSlate se limite à une liste sûre et restaure
  systématiquement l'état initial.